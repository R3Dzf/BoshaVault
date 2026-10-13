-- Phase 04: metadata-only admin security events with bounded retention.
-- No passwords, OTPs, emails, IP addresses, tokens, device IDs, or vault data.
begin;

create table lockvyn_private.lockvyn_admin_security_events (
  event_id uuid primary key default gen_random_uuid(),
  occurred_at timestamptz not null default clock_timestamp(),
  event_type text not null check (event_type in (
    'account_recovery_requested',
    'cloud_device_unregistered',
    'cloud_session_registered',
    'cloud_session_revoked',
    'support_case_opened',
    'support_action_completed',
    'support_action_failed',
    'support_review_pending',
    'admin_step_up_denied',
    'admin_allowlist_denied',
    'admin_inactive_session'
  )),
  actor_id uuid,
  subject_id uuid,
  detail_code text check (detail_code is null or detail_code in (
    'recovery_requested',
    'cloud_device_unregistered',
    'suspend_24h',
    'restore_login',
    'session_review',
    'mfa_review',
    'deletion_review'
  )),
  expires_at timestamptz not null
);
create or replace function lockvyn_private.set_admin_security_event_expiry()
returns trigger
language plpgsql
set search_path = ''
as $
begin
  new.expires_at := new.occurred_at + interval '90 days';
  return new;
end;
$;
revoke all on function lockvyn_private.set_admin_security_event_expiry()
  from public, anon, authenticated, service_role;
create trigger lockvyn_admin_security_event_expiry
  before insert on lockvyn_private.lockvyn_admin_security_events
  for each row execute function lockvyn_private.set_admin_security_event_expiry();
create index lockvyn_admin_security_events_recent_idx
  on lockvyn_private.lockvyn_admin_security_events(occurred_at desc);
create index lockvyn_admin_security_events_actor_type_recent_idx
  on lockvyn_private.lockvyn_admin_security_events(actor_id, event_type, occurred_at desc);
alter table lockvyn_private.lockvyn_admin_security_events enable row level security;
alter table lockvyn_private.lockvyn_admin_security_events force row level security;
revoke all on lockvyn_private.lockvyn_admin_security_events
  from public, anon, authenticated, service_role;
comment on table lockvyn_private.lockvyn_admin_security_events is
  'Append-only metadata-only admin security events during a 90-day retention window. Expired rows are purged by a bounded scheduled job.';

create or replace function lockvyn_private.write_admin_security_event(
  p_actor uuid,
  p_subject uuid,
  p_event_type text,
  p_detail_code text default null
)
returns void
language sql
security definer
set search_path = ''
as $$
  insert into lockvyn_private.lockvyn_admin_security_events
    (actor_id, subject_id, event_type, detail_code)
  values (p_actor, p_subject, p_event_type, p_detail_code);
$$;
revoke all on function lockvyn_private.write_admin_security_event(uuid,uuid,text,text)
  from public, anon, authenticated, service_role;

create or replace function lockvyn_private.capture_recovery_audit_event()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
  if new.action = 'recovery_requested' then
    perform lockvyn_private.write_admin_security_event(
      new.actor_id, new.target_id, 'account_recovery_requested', 'recovery_requested'
    );
  elsif new.action = 'cloud_device_unregistered' then
    perform lockvyn_private.write_admin_security_event(
      new.actor_id, new.target_id, 'cloud_device_unregistered', 'cloud_device_unregistered'
    );
  end if;
  return new;
end;
$$;
revoke all on function lockvyn_private.capture_recovery_audit_event()
  from public, anon, authenticated, service_role;
create trigger lockvyn_capture_admin_recovery_security_event
  after insert on public.lockvyn_admin_audit
  for each row execute function lockvyn_private.capture_recovery_audit_event();

create or replace function lockvyn_private.capture_support_security_event()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_event_type text;
begin
  if tg_op = 'INSERT' then
    perform lockvyn_private.write_admin_security_event(
      new.actor_id, new.target_id, 'support_case_opened', new.kind
    );
  elsif new.status is distinct from old.status then
    v_event_type := case new.status
      when 'completed' then 'support_action_completed'
      when 'failed' then 'support_action_failed'
      when 'review_pending' then 'support_review_pending'
      else null
    end;
    if v_event_type is not null then
      perform lockvyn_private.write_admin_security_event(
        new.actor_id, new.target_id, v_event_type, new.kind
      );
    end if;
  end if;
  return new;
end;
$$;
revoke all on function lockvyn_private.capture_support_security_event()
  from public, anon, authenticated, service_role;
create trigger lockvyn_capture_support_security_event
  after insert or update of status on public.lockvyn_admin_support_cases
  for each row execute function lockvyn_private.capture_support_security_event();

create or replace function lockvyn_private.capture_cloud_session_registration()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
  if new.revoked_at is null then
    perform lockvyn_private.write_admin_security_event(
      new.owner_id, new.owner_id, 'cloud_session_registered', null
    );
  end if;
  return new;
end;
$$;
revoke all on function lockvyn_private.capture_cloud_session_registration()
  from public, anon, authenticated, service_role;
create trigger lockvyn_capture_cloud_session_registration
  after insert on lockvyn_private.cloud_device_registry
  for each row execute function lockvyn_private.capture_cloud_session_registration();

create or replace function lockvyn_private.capture_cloud_session_audit()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
  if new.action = 'cloud_session_revoked' then
    perform lockvyn_private.write_admin_security_event(
      new.actor_user_id, new.owner_id, 'cloud_session_revoked', null
    );
  elsif new.action = 'cloud_session_unregistered' then
    perform lockvyn_private.write_admin_security_event(
      new.actor_user_id, new.owner_id, 'cloud_device_unregistered', 'cloud_device_unregistered'
    );
  end if;
  return new;
end;
$$;
revoke all on function lockvyn_private.capture_cloud_session_audit()
  from public, anon, authenticated, service_role;
create trigger lockvyn_capture_cloud_session_audit
  after insert on lockvyn_private.cloud_device_audit
  for each row execute function lockvyn_private.capture_cloud_session_audit();

-- The Edge Function calls this only after Supabase Auth has validated the JWT.
-- Log bounded, pseudonymous privileged-access denials; never failed passwords.
create or replace function public.lockvyn_admin_record_security_signal(
  p_actor uuid,
  p_signal text
)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
begin
  if p_actor is null or p_signal is null or p_signal not in (
    'admin_step_up_denied', 'admin_allowlist_denied', 'admin_inactive_session'
  ) then
    raise exception 'Invalid security signal' using errcode = '22023';
  end if;
  if not exists (select 1 from auth.users u where u.id = p_actor) then
    return false;
  end if;

  perform pg_catalog.pg_advisory_xact_lock(
    pg_catalog.hashtextextended(p_actor::text || ':' || p_signal, 82104)
  );
  if exists (
    select 1 from lockvyn_private.lockvyn_admin_security_events e
    where e.actor_id = p_actor and e.event_type = p_signal
      and e.occurred_at > statement_timestamp() - interval '2 minutes'
  ) then
    return false;
  end if;
  perform lockvyn_private.write_admin_security_event(p_actor, null, p_signal, null);
  return true;
end;
$$;
revoke all on function public.lockvyn_admin_record_security_signal(uuid,text)
  from public, anon, authenticated;
grant execute on function public.lockvyn_admin_record_security_signal(uuid,text)
  to service_role;
comment on function public.lockvyn_admin_record_security_signal(uuid,text) is
  'Service-role-only, rate-limited ingestion for verified-user admin access denials; stores no credentials or network metadata.';

create or replace function public.lockvyn_admin_security_feed(
  p_actor uuid,
  p_actor_session uuid,
  p_actor_aal text,
  p_limit integer default 50
)
returns jsonb
language plpgsql
volatile
security definer
set search_path = ''
as $$
declare
  v_limit integer := greatest(1, least(coalesce(p_limit, 50), 100));
  v_events jsonb;
  v_alerts jsonb;
begin
  if p_actor is null or p_actor_session is null
     or p_actor_aal is distinct from 'aal2'
     or not exists (select 1 from public.lockvyn_admins a where a.user_id = p_actor)
     or not public.lockvyn_auth_session_is_active(p_actor, p_actor_session) then
    raise exception 'A live AAL2 administrator session is required' using errcode = '42501';
  end if;

  perform lockvyn_private.purge_expired_security_events();

  select coalesce(jsonb_agg(jsonb_build_object(
    'id', page.event_id,
    'occurredAt', page.occurred_at,
    'eventType', page.event_type,
    'actorId', page.actor_id,
    'subjectId', page.subject_id,
    'detailCode', page.detail_code
  ) order by page.occurred_at desc), '[]'::jsonb)
    into v_events
  from (
    select e.event_id, e.occurred_at, e.event_type, e.actor_id, e.subject_id, e.detail_code
    from lockvyn_private.lockvyn_admin_security_events e
    where e.expires_at > statement_timestamp()
    order by e.occurred_at desc
    limit v_limit
  ) page;

  select jsonb_build_object(
    'adminStepUpDenials15m', count(*) filter (
      where e.event_type = 'admin_step_up_denied'
        and e.occurred_at > statement_timestamp() - interval '15 minutes'
    ),
    'adminAllowlistDenials15m', count(*) filter (
      where e.event_type = 'admin_allowlist_denied'
        and e.occurred_at > statement_timestamp() - interval '15 minutes'
    ),
    'repeatedDeniedActors15m', (
      select count(*) from (
        select d.actor_id
        from lockvyn_private.lockvyn_admin_security_events d
        where d.event_type in ('admin_step_up_denied','admin_allowlist_denied','admin_inactive_session')
          and d.occurred_at > statement_timestamp() - interval '15 minutes'
          and d.actor_id is not null
        group by d.actor_id
        having count(*) >= 5
      ) repeated
    ),
    'newCloudSessions24h', count(*) filter (
      where e.event_type = 'cloud_session_registered'
    ),
    'failedSupportActions24h', count(*) filter (
      where e.event_type = 'support_action_failed'
    )
  ) into v_alerts
  from lockvyn_private.lockvyn_admin_security_events e
  where e.occurred_at > statement_timestamp() - interval '24 hours';

  return jsonb_build_object('events', v_events, 'alerts', v_alerts);
end;
$$;
revoke all on function public.lockvyn_admin_security_feed(uuid,uuid,text,integer)
  from public, anon, authenticated;
grant execute on function public.lockvyn_admin_security_feed(uuid,uuid,text,integer)
  to service_role;
comment on function public.lockvyn_admin_security_feed(uuid,uuid,text,integer) is
  'Service-role-only, allowlist/AAL2/live-session checked feed; returns bounded metadata without email, session IDs, IP, tokens or vault data.';

create or replace function lockvyn_private.purge_expired_security_events()
returns integer
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_deleted integer;
begin
  delete from lockvyn_private.lockvyn_admin_security_events
    where expires_at <= statement_timestamp();
  get diagnostics v_deleted = row_count;
  return v_deleted;
end;
$$;
revoke all on function lockvyn_private.purge_expired_security_events()
  from public, anon, authenticated, service_role;
grant execute on function lockvyn_private.purge_expired_security_events() to postgres;

create extension if not exists pg_cron with schema pg_catalog;
do $schedule$
begin
  if not exists (
    select 1 from cron.job where jobname = 'lockvyn-admin-security-retention'
  ) then
    perform cron.schedule(
      'lockvyn-admin-security-retention',
      '17 3 * * *',
      'select lockvyn_private.purge_expired_security_events();'
    );
  end if;
end
$schedule$;

commit;
