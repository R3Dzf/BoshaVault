-- Synthetic-only Phase 09 fixtures. All consent, users, events and audit rows roll back.
begin;
insert into auth.users(id,aud,role,email,email_confirmed_at,created_at,updated_at)
values('b7000000-0000-4000-8000-000000000001','authenticated','authenticated','analytics-admin@example.invalid',now(),now(),now());
insert into auth.sessions(id,user_id) values('b7100000-0000-4000-8000-000000000001','b7000000-0000-4000-8000-000000000001');
insert into public.lockvyn_admins(user_id) values('b7000000-0000-4000-8000-000000000001');
insert into auth.users(id,aud,role,email,email_confirmed_at,created_at,updated_at)
select ('b8000000-0000-4000-8000-'||lpad(to_hex(n),12,'0'))::uuid,'authenticated','authenticated',
  'analytics-'||n||'@example.invalid',now(),now(),now() from generate_series(1,6) g(n);
insert into auth.sessions(id,user_id)
select ('b8100000-0000-4000-8000-'||lpad(to_hex(n),12,'0'))::uuid,
  ('b8000000-0000-4000-8000-'||lpad(to_hex(n),12,'0'))::uuid from generate_series(1,6) g(n);

do $privileges$
begin
 if not exists(select 1 from cron.job where jobname='lockvyn-analytics-retention'
   and command like '%lockvyn_analytics_purge_expired%')
 then raise exception 'Daily analytics retention job is missing'; end if;
 if has_table_privilege('anon','lockvyn_private.analytics_consents','select')
  or has_table_privilege('authenticated','lockvyn_private.analytics_events','select')
  or has_table_privilege('service_role','lockvyn_private.analytics_admin_reads','select')
  or has_function_privilege('anon','public.lockvyn_analytics_set_consent(boolean)','execute')
  or has_function_privilege('service_role','public.lockvyn_analytics_record_event(text,text,text,text,text)','execute')
  or has_function_privilege('authenticated','public.lockvyn_admin_privacy_analytics(uuid,uuid,text,date,date)','execute')
  or has_function_privilege('anon','public.lockvyn_analytics_purge_expired()','execute')
 then raise exception 'Analytics grants are too broad'; end if;
 if not exists(select 1 from pg_class c join pg_namespace n on n.oid=c.relnamespace
  where n.nspname='lockvyn_private' and c.relname='analytics_events' and c.relrowsecurity and c.relforcerowsecurity)
 then raise exception 'Analytics events must force RLS'; end if;
end; $privileges$;

do $events$
declare i integer; u uuid; s uuid; report jsonb; rows jsonb;
begin
 u:='b8000000-0000-4000-8000-000000000006'; s:='b8100000-0000-4000-8000-000000000006';
 perform set_config('request.jwt.claim.role','authenticated',true);
 perform set_config('request.jwt.claims',jsonb_build_object('role','authenticated','sub',u::text,'session_id',s::text,'aal','aal1')::text,true);
 if public.lockvyn_analytics_consent_status() is distinct from false then raise exception 'Consent must default off'; end if;
 begin
  perform public.lockvyn_analytics_record_event('app_active','windows','3.1.0',null,null);
  raise exception 'Event accepted before consent';
 exception when insufficient_privilege then null; end;

 for i in 1..5 loop
  u:=('b8000000-0000-4000-8000-'||lpad(to_hex(i),12,'0'))::uuid;
  s:=('b8100000-0000-4000-8000-'||lpad(to_hex(i),12,'0'))::uuid;
  perform set_config('request.jwt.claims',jsonb_build_object('role','authenticated','sub',u::text,'session_id',s::text,'aal','aal1')::text,true);
  if public.lockvyn_analytics_set_consent(true) is distinct from true then raise exception 'Opt-in failed'; end if;
  perform public.lockvyn_analytics_record_event('app_active','windows','3.1.0',null,null);
  perform public.lockvyn_analytics_record_event('app_active','windows','3.1.0',null,null);
  perform public.lockvyn_analytics_record_event('app_active','windows','3.2.0',null,null);
  perform public.lockvyn_analytics_record_event('sync_result','windows','3.2.0','success','lt_1mib');
 end loop;

 perform set_config('request.jwt.claim.role','service_role',true);
 perform set_config('request.jwt.claims','{"role":"service_role"}',true);
 report:=public.lockvyn_admin_privacy_analytics(
  'b7000000-0000-4000-8000-000000000001','b7100000-0000-4000-8000-000000000001',
  'aal2',(now() at time zone 'UTC')::date,(now() at time zone 'UTC')::date);
 rows:=report->'rows';
 if jsonb_array_length(rows)<5 then raise exception 'Expected signup, version, update, sync and resource rows'; end if;
 if report::text like '%analytics-1@%' or report::text like '%b8000000-0000-4000-8000-%' or report::text like '%email%'
 then raise exception 'Aggregate response exposed account data'; end if;
 if not exists(select 1 from jsonb_array_elements(rows) r where r->>'metric'='active_app_versions'
   and r->>'appVersion'='3.2.0' and (r->>'users')::integer=5)
 then raise exception 'Version cohort missing'; end if;
 if not exists(select 1 from jsonb_array_elements(rows) r where r->>'metric'='update_uptake'
   and (r->>'users')::integer=5)
 then raise exception 'Update uptake missing'; end if;
 if not exists(select 1 from jsonb_array_elements(rows) r where r->>'metric'='sync_reliability'
   and r->>'outcome'='success' and (r->>'events')::integer=5)
 then raise exception 'Sync reliability missing'; end if;
 if not exists(select 1 from jsonb_array_elements(rows) r where r->>'metric'='resource_usage'
   and r->>'resourceBucket'='lt_1mib' and (r->>'users')::integer=5)
 then raise exception 'Coarse resource metrics missing'; end if;
 if not exists(select 1 from jsonb_array_elements(rows) r where r->>'metric'='signups' and (r->>'users')::integer=5)
 then raise exception 'Consented signup cohort missing'; end if;

 begin
  perform public.lockvyn_admin_privacy_analytics('b7000000-0000-4000-8000-000000000001',
   'b7100000-0000-4000-8000-000000000001','aal1',current_date,current_date);
  raise exception 'AAL1 analytics read allowed';
 exception when insufficient_privilege then null; end;
 begin
  perform public.lockvyn_admin_privacy_analytics('b8000000-0000-4000-8000-000000000002',
   'b8100000-0000-4000-8000-000000000002','aal2',current_date,current_date);
  raise exception 'Nonadmin analytics read allowed';
 exception when insufficient_privilege then null; end;

 u:='b8000000-0000-4000-8000-000000000001'; s:='b8100000-0000-4000-8000-000000000001';
 perform set_config('request.jwt.claim.role','authenticated',true);
 perform set_config('request.jwt.claims',jsonb_build_object('role','authenticated','sub',u::text,'session_id',s::text,'aal','aal1')::text,true);
 if public.lockvyn_analytics_set_consent(false) is distinct from false
  or public.lockvyn_analytics_consent_status() is distinct from false
  or exists(select 1 from lockvyn_private.analytics_consents where user_id=u)
  or exists(select 1 from lockvyn_private.analytics_events where user_id=u)
 then raise exception 'Opt-out must delete consent and events'; end if;
 perform set_config('request.jwt.claim.role','service_role',true);
 perform set_config('request.jwt.claims','{"role":"service_role"}',true);
 report:=public.lockvyn_admin_privacy_analytics('b7000000-0000-4000-8000-000000000001',
  'b7100000-0000-4000-8000-000000000001','aal2',current_date,current_date);
 if jsonb_array_length(report->'rows')<>0 then raise exception 'Cohort below five must be suppressed'; end if;

 insert into lockvyn_private.analytics_events(user_id,event_type,event_day,platform,app_version,outcome,resource_bucket,created_at)
 values(u,'sync_result',(now() at time zone 'UTC')::date-91,'windows','3.1.0','success','none',clock_timestamp()-interval '91 days');
 perform public.lockvyn_analytics_purge_expired();
 if exists(select 1 from lockvyn_private.analytics_events where created_at<clock_timestamp()-interval '90 days')
 then raise exception '90-day retention purge failed'; end if;

 u:='b8000000-0000-4000-8000-000000000006'; s:='b8100000-0000-4000-8000-000000000006';
 perform set_config('request.jwt.claim.role','authenticated',true);
 perform set_config('request.jwt.claims',jsonb_build_object('role','authenticated','sub',u::text,'session_id',s::text,'aal','aal1')::text,true);
 perform public.lockvyn_analytics_set_consent(true);
 for i in 1..30 loop perform public.lockvyn_analytics_record_event('sync_result','android','1.4.0','failure','none'); end loop;
 begin
  perform public.lockvyn_analytics_record_event('sync_result','android','1.4.0','failure','none');
  raise exception 'Event rate limit missing';
 exception when program_limit_exceeded then null; end;
 perform public.lockvyn_analytics_set_consent(false);
end; $events$;
rollback;
