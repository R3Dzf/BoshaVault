-- Phase 04 security feed fixtures use only synthetic identities and roll back.
begin;

insert into auth.users(id,aud,role,email,email_confirmed_at,created_at,updated_at) values
 ('a4000000-0000-4000-8000-000000000001','authenticated','authenticated','security-admin@example.invalid',now(),now(),now()),
 ('b4000000-0000-4000-8000-000000000002','authenticated','authenticated','security-target@example.invalid',now(),now(),now());
insert into auth.sessions(id,user_id) values
 ('c4000000-0000-4000-8000-000000000003','a4000000-0000-4000-8000-000000000001'),
 ('d4000000-0000-4000-8000-000000000004','b4000000-0000-4000-8000-000000000002');
insert into public.lockvyn_admins(user_id) values
 ('a4000000-0000-4000-8000-000000000001');

insert into lockvyn_private.cloud_device_registry(
  id,owner_id,auth_session_id,schema_version,platform,os_version,app_version,display_name
) values (
 'e4000000-0000-4000-8000-000000000005',
 'b4000000-0000-4000-8000-000000000002',
 'd4000000-0000-4000-8000-000000000004',
 1,'windows','11','3.1.0','Synthetic security test'
);
insert into public.lockvyn_admin_audit(actor_id,target_id,action)
values ('a4000000-0000-4000-8000-000000000001',
        'b4000000-0000-4000-8000-000000000002','recovery_requested');
insert into public.lockvyn_admin_support_cases(actor_id,target_id,kind,reason)
values ('a4000000-0000-4000-8000-000000000001',
        'b4000000-0000-4000-8000-000000000002','suspend_24h','lost_device');
update public.lockvyn_admin_support_cases
set status='running'
where actor_id='a4000000-0000-4000-8000-000000000001'
  and target_id='b4000000-0000-4000-8000-000000000002';
update public.lockvyn_admin_support_cases
set status='completed',processed_at=now()
where actor_id='a4000000-0000-4000-8000-000000000001'
  and target_id='b4000000-0000-4000-8000-000000000002';
insert into lockvyn_private.cloud_device_audit(
  owner_id,actor_user_id,device_id,actor_session_id,target_session_id,action
) values (
 'b4000000-0000-4000-8000-000000000002',
 'a4000000-0000-4000-8000-000000000001',
 'e4000000-0000-4000-8000-000000000005',
 'c4000000-0000-4000-8000-000000000003',
 'd4000000-0000-4000-8000-000000000004',
 'cloud_session_revoked'
);

set local role service_role;
do $$
begin
  if not public.lockvyn_admin_record_security_signal(
    'a4000000-0000-4000-8000-000000000001','admin_step_up_denied') then
    raise exception 'Verified-user denial signal was not recorded';
  end if;
  if public.lockvyn_admin_record_security_signal(
    'a4000000-0000-4000-8000-000000000001','admin_step_up_denied') then
    raise exception 'Repeated denial signal bypassed the two-minute dedupe';
  end if;
  if not public.lockvyn_admin_record_security_signal(
    'b4000000-0000-4000-8000-000000000002','admin_allowlist_denied') then
    raise exception 'Non-allowlisted actor denial signal was not recorded';
  end if;
end $$;
reset role;

-- Synthetic recent events exercise the repeated-actor alert threshold.
insert into lockvyn_private.lockvyn_admin_security_events(actor_id,event_type)
select 'a4000000-0000-4000-8000-000000000001','admin_step_up_denied'
from generate_series(1,4);

do $$
declare
  v_feed jsonb;
begin
  if not has_table_privilege('anon','lockvyn_private.lockvyn_admin_security_events','select')
     or not has_table_privilege('authenticated','lockvyn_private.lockvyn_admin_security_events','select')
     or not has_table_privilege('service_role','lockvyn_private.lockvyn_admin_security_events','select') then
    raise exception 'Security event table must not be directly readable by API roles';
  end if;
  if has_function_privilege('authenticated',
       'public.lockvyn_admin_security_feed(uuid,uuid,text,integer)','execute')
     or has_function_privilege('anon',
       'public.lockvyn_admin_security_feed(uuid,uuid,text,integer)','execute')
     or not has_function_privilege('service_role',
       'public.lockvyn_admin_security_feed(uuid,uuid,text,integer)','execute') then
    raise exception 'Security feed execute grants are incorrect';
  end if;

  begin
    perform public.lockvyn_admin_security_feed(
      'a4000000-0000-4000-8000-000000000001',
      'c4000000-0000-4000-8000-000000000003','aal1',50);
    raise exception 'AAL1 administrator could read the security feed';
  exception when insufficient_privilege then null;
  end;
  begin
    perform public.lockvyn_admin_security_feed(
      'b4000000-0000-4000-8000-000000000002',
      'd4000000-0000-4000-8000-000000000004','aal2',50);
    raise exception 'Non-allowlisted user could read the security feed';
  exception when insufficient_privilege then null;
  end;
end $$;

set local role service_role;
do $$
declare v_feed jsonb;
begin
  begin
    perform count(*) from lockvyn_private.lockvyn_admin_security_events;
    raise exception 'Service role could directly read security events';
  exception when insufficient_privilege then null;
  end;
  v_feed := public.lockvyn_admin_security_feed(
    'a4000000-0000-4000-8000-000000000001',
    'c4000000-0000-4000-8000-000000000003','aal2',100);
  if jsonb_array_length(v_feed->'events') < 7 then
    raise exception 'Event source triggers did not produce the expected feed: %', v_feed;
  end if;
  if (v_feed->'alerts'->>'adminStepUpDenials15m')::integer < 5
     or (v_feed->'alerts'->>'repeatedDeniedActors15m')::integer <> 1
     or (v_feed->'alerts'->>'newCloudSessions24h')::integer <> 1
     or (v_feed->'alerts'->>'failedSupportActions24h')::integer <> 0 then
    raise exception 'Security alert counts were incorrect: %', v_feed->'alerts';
  end if;
  if v_feed::text like '%@example.invalid%' or v_feed::text ilike '%ip_address%'
     or v_feed::text ilike '%user_agent%' or v_feed::text ilike '%session_id%' then
    raise exception 'Security feed leaked email or network/session metadata';
  end if;
end $$;
reset role;

insert into lockvyn_private.lockvyn_admin_security_events(actor_id,event_type,occurred_at)
values ('a4000000-0000-4000-8000-000000000001','admin_step_up_denied',now()-interval '91 days');
select lockvyn_private.purge_expired_security_events();
do $$ begin
  if exists (
    select 1 from lockvyn_private.lockvyn_admin_security_events
    where occurred_at <= statement_timestamp() - interval '90 days'
  ) then raise exception 'Expired event survived retention purge'; end if;
  if not exists (
    select 1 from cron.job where jobname='lockvyn-admin-security-retention'
  ) then raise exception 'Daily security event retention schedule is missing'; end if;
end $$;

rollback;
select 'PASS Phase 04 security events: event sources, AAL2/allowlist gates, direct-table isolation, 2-minute signal dedupe, repeated-denial alert, 90-day retention; fixtures rolled back' as result;
