-- Phase 09: explicit opt-in, aggregate-only privacy analytics.
begin;
create schema if not exists lockvyn_private;
create table lockvyn_private.analytics_consents(
  user_id uuid primary key references auth.users(id) on delete cascade,
  consented_at timestamptz not null default clock_timestamp(),
  consent_version text not null default '2026-10-14-v1' check(consent_version='2026-10-14-v1')
);
create table lockvyn_private.analytics_events(
  event_id bigint generated always as identity primary key,
  user_id uuid not null references auth.users(id) on delete cascade,
  event_type text not null check(event_type in ('app_active','update_applied','sync_result')),
  event_day date not null default ((now() at time zone 'UTC')::date),
  platform text not null check(platform in ('windows','android')),
  app_version text not null check(char_length(app_version) between 1 and 50 and
    app_version ~ '^([0-9]{1,5}([.][0-9]{1,5}){0,3}(-[A-Za-z0-9.-]{1,20})?|unknown)$'),
  outcome text check(outcome in ('success','failure')),
  resource_bucket text check(resource_bucket in ('none','lt_1mib','1_10mib','gt_10mib')),
  created_at timestamptz not null default clock_timestamp(),
  check((event_type in ('app_active','update_applied') and outcome is null and resource_bucket is null)
     or (event_type='sync_result' and outcome in ('success','failure')
       and resource_bucket in ('none','lt_1mib','1_10mib','gt_10mib')))
);
create unique index analytics_one_active_version_per_user_day
 on lockvyn_private.analytics_events(user_id,event_day,platform,app_version) where event_type='app_active';
create index analytics_events_report_idx on lockvyn_private.analytics_events(event_day,event_type,platform,app_version);
create index analytics_events_retention_idx on lockvyn_private.analytics_events(created_at);
create index analytics_events_user_recent_idx on lockvyn_private.analytics_events(user_id,created_at desc);
create table lockvyn_private.analytics_admin_reads(
  read_id bigint generated always as identity primary key,
  actor_id uuid not null references auth.users(id) on delete cascade,
  actor_session_id uuid not null,
  created_at timestamptz not null default clock_timestamp()
);
create index analytics_admin_reads_rate_idx on lockvyn_private.analytics_admin_reads(actor_id,created_at desc);
create index analytics_admin_reads_retention_idx on lockvyn_private.analytics_admin_reads(created_at);
alter table lockvyn_private.analytics_consents enable row level security;
alter table lockvyn_private.analytics_consents force row level security;
alter table lockvyn_private.analytics_events enable row level security;
alter table lockvyn_private.analytics_events force row level security;
alter table lockvyn_private.analytics_admin_reads enable row level security;
alter table lockvyn_private.analytics_admin_reads force row level security;
revoke all on lockvyn_private.analytics_consents from public,anon,authenticated,service_role;
revoke all on lockvyn_private.analytics_events from public,anon,authenticated,service_role;
revoke all on lockvyn_private.analytics_admin_reads from public,anon,authenticated,service_role;
comment on table lockvyn_private.analytics_events is
 'Allowlisted coarse events only. User UUID is internal for dedupe/deletion and never returned. Purged after 90 days.';
comment on table lockvyn_private.analytics_consents is
 'No row means opted out. Opt-out deletes this row and all account events.';

create or replace function public.lockvyn_analytics_consent_status()
returns boolean language plpgsql security definer set search_path=''
as $$
declare u uuid:=(select auth.uid()); c jsonb:=(select auth.jwt()); s text;
begin
 s:=c->>'session_id';
 if u is null or c->>'role' is distinct from 'authenticated'
   or s !~* '^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$'
   or not exists(select 1 from auth.users where id=u and email_confirmed_at is not null)
   or public.lockvyn_auth_session_is_active(u,s::uuid) is distinct from true
 then raise exception 'Verified active session required' using errcode='42501'; end if;
 return exists(select 1 from lockvyn_private.analytics_consents where user_id=u);
end; $$;
revoke all on function public.lockvyn_analytics_consent_status() from public,anon,service_role;
grant execute on function public.lockvyn_analytics_consent_status() to authenticated;

create or replace function public.lockvyn_analytics_set_consent(p_enabled boolean)
returns boolean language plpgsql security definer set search_path=''
as $$
declare u uuid:=(select auth.uid()); c jsonb:=(select auth.jwt()); s text;
begin
 s:=c->>'session_id';
 if p_enabled is null or u is null or c->>'role' is distinct from 'authenticated'
   or s !~* '^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$'
   or not exists(select 1 from auth.users where id=u and email_confirmed_at is not null)
   or public.lockvyn_auth_session_is_active(u,s::uuid) is distinct from true
 then raise exception 'Verified active session required' using errcode='42501'; end if;
 if p_enabled then
   insert into lockvyn_private.analytics_consents(user_id,consented_at)
   values(u,clock_timestamp()) on conflict(user_id) do update set consented_at=excluded.consented_at;
 else
   delete from lockvyn_private.analytics_events where user_id=u;
   delete from lockvyn_private.analytics_consents where user_id=u;
 end if;
 return p_enabled;
end; $$;
revoke all on function public.lockvyn_analytics_set_consent(boolean) from public,anon,service_role;
grant execute on function public.lockvyn_analytics_set_consent(boolean) to authenticated;

create or replace function public.lockvyn_analytics_record_event(
 p_event text,p_platform text,p_app_version text,p_outcome text default null,p_resource_bucket text default null)
returns boolean language plpgsql security definer set search_path=''
as $$
declare u uuid:=(select auth.uid()); c jsonb:=(select auth.jwt()); s text; prev text; n integer;
begin
 s:=c->>'session_id';
 if u is null or c->>'role' is distinct from 'authenticated'
   or s !~* '^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$'
   or not exists(select 1 from auth.users where id=u and email_confirmed_at is not null)
   or public.lockvyn_auth_session_is_active(u,s::uuid) is distinct from true
   or not exists(select 1 from lockvyn_private.analytics_consents where user_id=u)
 then raise exception 'Verified session and explicit consent required' using errcode='42501'; end if;
 if p_event is null or p_event not in ('app_active','sync_result')
   or p_platform is null or p_platform not in ('windows','android')
   or p_app_version is null or char_length(p_app_version) not between 1 and 50
   or p_app_version !~ '^([0-9]{1,5}([.][0-9]{1,5}){0,3}(-[A-Za-z0-9.-]{1,20})?|unknown)$'
   or (p_event='app_active' and (p_outcome is not null or p_resource_bucket is not null))
   or (p_event='sync_result' and (p_outcome is null or p_outcome not in ('success','failure')
     or p_resource_bucket is null or p_resource_bucket not in ('none','lt_1mib','1_10mib','gt_10mib')))
 then raise exception 'Invalid analytics event' using errcode='22023'; end if;
 perform pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended(u::text,90909));
 if p_event='app_active' then
   select app_version into prev from lockvyn_private.analytics_events
     where user_id=u and event_type='app_active' order by created_at desc limit 1;
   if prev is not null and prev<>p_app_version and not exists(
     select 1 from lockvyn_private.analytics_events where user_id=u
       and event_type='update_applied' and app_version=p_app_version
       and event_day=(now() at time zone 'UTC')::date)
   then insert into lockvyn_private.analytics_events(user_id,event_type,platform,app_version)
     values(u,'update_applied',p_platform,p_app_version); end if;
   insert into lockvyn_private.analytics_events(user_id,event_type,platform,app_version)
     values(u,'app_active',p_platform,p_app_version) on conflict do nothing;
 else
   select count(*) into n from lockvyn_private.analytics_events
     where user_id=u and event_type='sync_result' and created_at>clock_timestamp()-interval '24 hours';
   if n>=30 then raise exception 'Analytics event rate limit reached' using errcode='54000'; end if;
   insert into lockvyn_private.analytics_events(user_id,event_type,platform,app_version,outcome,resource_bucket)
     values(u,'sync_result',p_platform,p_app_version,p_outcome,p_resource_bucket);
 end if;
 delete from lockvyn_private.analytics_events where created_at<clock_timestamp()-interval '90 days';
 return true;
end; $$;
revoke all on function public.lockvyn_analytics_record_event(text,text,text,text,text) from public,anon,service_role;
grant execute on function public.lockvyn_analytics_record_event(text,text,text,text,text) to authenticated;

create or replace function public.lockvyn_analytics_purge_expired()
returns integer language plpgsql security definer set search_path=''
as $$
declare n integer;
begin
 delete from lockvyn_private.analytics_events where created_at<clock_timestamp()-interval '90 days';
 get diagnostics n=row_count;
 delete from lockvyn_private.analytics_admin_reads where created_at<clock_timestamp()-interval '180 days';
 return n;
end; $$;
revoke all on function public.lockvyn_analytics_purge_expired() from public,anon,authenticated;
grant execute on function public.lockvyn_analytics_purge_expired() to service_role;

create extension if not exists pg_cron with schema pg_catalog;
do $schedule$
begin
 if not exists(select 1 from cron.job where jobname='lockvyn-analytics-retention') then
   perform cron.schedule(
     'lockvyn-analytics-retention',
     '37 3 * * *',
     'select public.lockvyn_analytics_purge_expired();'
   );
 end if;
end
$schedule$;

create or replace function public.lockvyn_admin_privacy_analytics(
 p_actor uuid,p_actor_session uuid,p_actor_aal text,p_from date,p_to date)
returns jsonb language plpgsql security definer set search_path=''
as $$
declare r text:=coalesce(auth.jwt()->>'role',current_setting('request.jwt.claim.role',true),''); n integer; rows jsonb;
begin
 if r<>'service_role' or p_actor is null or p_actor_session is null or p_actor_aal is distinct from 'aal2'
   or not exists(select 1 from public.lockvyn_admins where user_id=p_actor)
   or public.lockvyn_auth_session_is_active(p_actor,p_actor_session) is distinct from true
 then raise exception 'Active allowlisted AAL2 administrator required' using errcode='42501'; end if;
 if p_from is null or p_to is null or p_from>p_to or p_to>(now() at time zone 'UTC')::date
   or p_from<((now() at time zone 'UTC')::date-89) or p_to-p_from>89
 then raise exception 'Analytics date range must be within 90 days' using errcode='22023'; end if;
 perform public.lockvyn_analytics_purge_expired();
 perform pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended(p_actor::text,90910));
 select count(*) into n from lockvyn_private.analytics_admin_reads
   where actor_id=p_actor and created_at>clock_timestamp()-interval '5 minutes';
 if n>=30 then raise exception 'Analytics report rate limit reached' using errcode='54000'; end if;
 insert into lockvyn_private.analytics_admin_reads(actor_id,actor_session_id) values(p_actor,p_actor_session);
 with raw as (
   select e.event_day day,
     case e.event_type when 'app_active' then 'active_app_versions'
       when 'update_applied' then 'update_uptake' else 'sync_reliability' end metric,
     e.user_id,e.platform,e.app_version,e.outcome,null::text bucket
   from lockvyn_private.analytics_events e
   where e.event_day between p_from and p_to and e.created_at>=clock_timestamp()-interval '90 days'
   union all
   select e.event_day,'resource_usage',e.user_id,e.platform,e.app_version,null::text,e.resource_bucket
   from lockvyn_private.analytics_events e
   where e.event_type='sync_result' and e.event_day between p_from and p_to
     and e.created_at>=clock_timestamp()-interval '90 days'
   union all
   select (u.created_at at time zone 'UTC')::date,'signups',u.id,'all','all',null::text,null::text
   from auth.users u join lockvyn_private.analytics_consents c on c.user_id=u.id
   where (u.created_at at time zone 'UTC')::date between p_from and p_to
     and u.created_at>=clock_timestamp()-interval '90 days'
 ), grouped as (
   select day,metric,platform,app_version,outcome,bucket,
     count(distinct user_id)::integer users,count(*)::integer events
   from raw group by day,metric,platform,app_version,outcome,bucket
   having count(distinct user_id)>=5
 )
 select coalesce(jsonb_agg(jsonb_build_object(
   'day',day,'metric',metric,'platform',platform,'appVersion',app_version,
   'outcome',outcome,'resourceBucket',bucket,'users',users,'events',events
 ) order by day,metric,platform,app_version,outcome,bucket),'[]'::jsonb)
 into rows from grouped;
 return jsonb_build_object('fromDate',p_from,'toDate',p_to,'minimumCohort',5,'rows',rows);
end; $$;
revoke all on function public.lockvyn_admin_privacy_analytics(uuid,uuid,text,date,date) from public,anon,authenticated;
grant execute on function public.lockvyn_admin_privacy_analytics(uuid,uuid,text,date,date) to service_role;
commit;
