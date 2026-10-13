-- Phase 05: synthetic-only sync quota, telemetry, and reversible suspension checks.
-- Runs against the disposable local Supabase database and rolls back all rows.
begin;

insert into auth.users(id,aud,role,email,email_confirmed_at,created_at,updated_at) values
 ('f5000000-0000-4000-8000-000000000001','authenticated','authenticated','sync-admin@example.invalid',now(),now(),now()),
 ('f5000000-0000-4000-8000-000000000002','authenticated','authenticated','sync-target@example.invalid',now(),now(),now()),
 ('f5000000-0000-4000-8000-000000000003','authenticated','authenticated','sync-other@example.invalid',now(),now(),now());
insert into auth.sessions(id,user_id) values
 ('f6000000-0000-4000-8000-000000000001','f5000000-0000-4000-8000-000000000001'),
 ('f6000000-0000-4000-8000-000000000002','f5000000-0000-4000-8000-000000000002'),
 ('f6000000-0000-4000-8000-000000000003','f5000000-0000-4000-8000-000000000003');
insert into public.lockvyn_admins(user_id)
values ('f5000000-0000-4000-8000-000000000001');
insert into lockvyn_private.cloud_sync_quota_overrides(owner_id,quota_bytes)
values ('f5000000-0000-4000-8000-000000000002',250);

-- Register only after explicit synthetic consent. Old clients omit ciphertext_bytes.
set local role authenticated;
select set_config('request.jwt.claims',
 '{"sub":"f5000000-0000-4000-8000-000000000002","role":"authenticated","aal":"aal2","session_id":"f6000000-0000-4000-8000-000000000002"}',true);
select public.lockvyn_register_cloud_device(true,'windows','synthetic','5.0.0',
 'Synthetic laptop',1::smallint);

insert into public.opaque_device_snapshots
 (owner_id,vault_slot,device_slot,ciphertext_base64,ciphertext_sha256)
values
 ('f5000000-0000-4000-8000-000000000002','f7000000-0000-4000-8000-000000000001','f8000000-0000-4000-8000-000000000001',repeat('A',136),repeat('0',64)),
 ('f5000000-0000-4000-8000-000000000002','f7000000-0000-4000-8000-000000000002','f8000000-0000-4000-8000-000000000002',repeat('B',136),repeat('1',64));

-- A client cannot forge the derived byte count by updating that column directly.
update public.opaque_device_snapshots set ciphertext_bytes=1
where owner_id='f5000000-0000-4000-8000-000000000002'
  and vault_slot='f7000000-0000-4000-8000-000000000001';

do $phase05$
declare v_status jsonb;
begin
  if (select sum(ciphertext_bytes) from public.opaque_device_snapshots
      where owner_id='f5000000-0000-4000-8000-000000000002') <> 204 then
    raise exception 'Server did not derive actual ciphertext sizes for an old-client insert';
  end if;
  v_status:=public.lockvyn_cloud_sync_status();
  if (v_status->>'snapshotCount')::integer<>2
     or (v_status->>'ciphertextBytes')::bigint<>204
     or (v_status->>'quotaBytes')::bigint<>250
     or (v_status->>'remainingBytes')::bigint<>46
     or v_status->>'suspended'<>'false' then
    raise exception 'Owner sync status/usage response was incorrect: %',v_status;
  end if;
  begin
    insert into public.opaque_device_snapshots
      (owner_id,vault_slot,device_slot,ciphertext_base64,ciphertext_sha256)
    values ('f5000000-0000-4000-8000-000000000002','f7000000-0000-4000-8000-000000000003',
      'f8000000-0000-4000-8000-000000000003',repeat('C',136),repeat('2',64));
    raise exception 'Snapshot upload crossed the configured quota';
  exception when sqlstate 'BV001' then null;
  end;
  if (select count(*) from public.opaque_device_snapshots
      where owner_id='f5000000-0000-4000-8000-000000000002')<>2 then
    raise exception 'Rejected upload changed existing encrypted snapshots';
  end if;
  if not public.lockvyn_record_cloud_sync_outcome('success',204,null) then
    raise exception 'Successful opted-in sync outcome was not recorded';
  end if;
end;
$phase05$;
reset role;

-- A separate synthetic owner proves the account-wide 64-row cap.
set local role authenticated;
select set_config('request.jwt.claims',
 '{"sub":"f5000000-0000-4000-8000-000000000003","role":"authenticated","aal":"aal2","session_id":"f6000000-0000-4000-8000-000000000003"}',true);
insert into public.opaque_device_snapshots
 (owner_id,vault_slot,device_slot,ciphertext_base64,ciphertext_sha256)
select 'f5000000-0000-4000-8000-000000000003',
       ('f7000000-0000-4000-8000-'||lpad(g::text,12,'0'))::uuid,
       ('f8000000-0000-4000-8000-'||lpad(g::text,12,'0'))::uuid,
       repeat('D',136),repeat('3',64)
from generate_series(1,64) g;
do $phase05$
begin
  if (select count(*) from public.opaque_device_snapshots
      where owner_id='f5000000-0000-4000-8000-000000000003')<>64 then
    raise exception 'Account could not store exactly 64 bounded snapshots';
  end if;
  begin
    insert into public.opaque_device_snapshots
      (owner_id,vault_slot,device_slot,ciphertext_base64,ciphertext_sha256)
    values ('f5000000-0000-4000-8000-000000000003','f7000000-0000-4000-8000-000000000065',
      'f8000000-0000-4000-8000-000000000065',repeat('E',136),repeat('4',64));
    raise exception 'The 65th snapshot was accepted';
  exception when sqlstate 'BV001' then null;
  end;
end;
$phase05$;
reset role;

-- Make the next opted-in outcome eligible under the one-minute metadata limit.
update lockvyn_private.cloud_device_registry
set last_sync_attempt_at=statement_timestamp()-interval '2 minutes'
where owner_id='f5000000-0000-4000-8000-000000000002';
set local role authenticated;
select set_config('request.jwt.claims',
 '{"sub":"f5000000-0000-4000-8000-000000000002","role":"authenticated","aal":"aal2","session_id":"f6000000-0000-4000-8000-000000000002"}',true);
select public.lockvyn_record_cloud_sync_outcome('failure',204,'network');
reset role;

-- Service-role RPCs are the only admin interface. All reads/actions require a
-- live allowlisted AAL2 session; mutations additionally require fresh TOTP and email.
set local role service_role;
do $phase05$
declare v_overview jsonb; v_events jsonb;
begin
  if has_function_privilege('authenticated',
       'public.lockvyn_admin_cloud_sync_overview(uuid,uuid,text,uuid,text,text,integer,text)','execute')
     or not has_function_privilege('service_role',
       'public.lockvyn_admin_cloud_sync_overview(uuid,uuid,text,uuid,text,text,integer,text)','execute')
     or has_table_privilege('service_role','lockvyn_private.cloud_sync_admin_audit','select')
     or has_table_privilege('authenticated','lockvyn_private.cloud_sync_suspensions','select') then
    raise exception 'Sync admin APIs or private-table grants are too broad';
  end if;

  begin
    perform public.lockvyn_admin_cloud_sync_overview(
      'f5000000-0000-4000-8000-000000000001','f6000000-0000-4000-8000-000000000001',
      'aal1','f5000000-0000-4000-8000-000000000002',null,null,30,null);
    raise exception 'AAL1 could read sync inventory';
  exception when insufficient_privilege then null;
  end;
  begin
    perform public.lockvyn_admin_cloud_sync_overview(
      'f5000000-0000-4000-8000-000000000003','f6000000-0000-4000-8000-000000000003',
      'aal2','f5000000-0000-4000-8000-000000000002',null,null,30,null);
    raise exception 'Nonadmin could read sync inventory';
  exception when insufficient_privilege then null;
  end;

  begin
    perform public.lockvyn_admin_set_cloud_sync_suspension(
      'f5000000-0000-4000-8000-000000000001','f6000000-0000-4000-8000-000000000001',
      'aal1',true,'f5000000-0000-4000-8000-000000000002',true,
      'support_review','sync-target@example.invalid');
    raise exception 'AAL1 could suspend cloud sync';
  exception when insufficient_privilege then null;
  end;
  begin
    perform public.lockvyn_admin_set_cloud_sync_suspension(
      'f5000000-0000-4000-8000-000000000001','f6000000-0000-4000-8000-000000000001',
      'aal2',false,'f5000000-0000-4000-8000-000000000002',true,
      'support_review','sync-target@example.invalid');
    raise exception 'AAL2 without fresh TOTP could suspend cloud sync';
  exception when insufficient_privilege then null;
  end;
  begin
    perform public.lockvyn_admin_set_cloud_sync_suspension(
      'f5000000-0000-4000-8000-000000000001','f6000000-0000-4000-8000-000000000001',
      'aal2',false,'f5000000-0000-4000-8000-000000000002',true,
      'support_review','wrong@example.invalid');
    raise exception 'Wrong typed target email was accepted';
  exception when insufficient_privilege then null;
  end;
  insert into lockvyn_private.cloud_sync_admin_audit(
    actor_id,target_id,actor_session_id,event_type,reason_code
  )
  select 'f5000000-0000-4000-8000-000000000001',
         'f5000000-0000-4000-8000-000000000003',
         'f6000000-0000-4000-8000-000000000001',
         'sync_suspended','support_review'
  from generate_series(1,5);
  begin
    perform public.lockvyn_admin_set_cloud_sync_suspension(
      'f5000000-0000-4000-8000-000000000001','f6000000-0000-4000-8000-000000000001',
      'aal2',true,'f5000000-0000-4000-8000-000000000003',true,
      'support_review','sync-other@example.invalid');
    raise exception 'Cloud sync control rate limit was not enforced';
  exception when sqlstate '54000' then null;
  end;
  delete from lockvyn_private.cloud_sync_admin_audit
  where actor_id='f5000000-0000-4000-8000-000000000001'
    and target_id='f5000000-0000-4000-8000-000000000003'
    and actor_session_id='f6000000-0000-4000-8000-000000000001';
  if not public.lockvyn_admin_set_cloud_sync_suspension(
      'f5000000-0000-4000-8000-000000000001','f6000000-0000-4000-8000-000000000001',
      'aal2',true,'f5000000-0000-4000-8000-000000000002',true,
      'support_review','sync-target@example.invalid') then
    raise exception 'Fresh AAL2 suspension did not apply';
  end if;
  v_overview:=public.lockvyn_admin_cloud_sync_overview(
      'f5000000-0000-4000-8000-000000000001','f6000000-0000-4000-8000-000000000001',
      'aal2','f5000000-0000-4000-8000-000000000002','windows','failure',30,'Synthetic laptop');
  if v_overview->>'suspended'<>'true'
     or jsonb_array_length(v_overview->'devices')<>1
     or v_overview->'devices'->0->>'last_sync_outcome'<>'failure'
     or v_overview->'devices'->0->>'last_sync_error_code'<>'network'
     or (v_overview->>'ciphertextBytes')::bigint<>204 then
    raise exception 'Filtered admin sync overview was incorrect: %',v_overview;
  end if;
  if v_overview::text like '%@example.invalid%' or v_overview::text ilike '%session_id%' then
    raise exception 'Admin sync overview exposed unnecessary identity/session data';
  end if;
  v_events:=public.lockvyn_admin_security_feed(
      'f5000000-0000-4000-8000-000000000001',
      'f6000000-0000-4000-8000-000000000001','aal2',100);
  if not exists(select 1 from jsonb_array_elements(v_events->'events') e
      where e->>'eventType'='cloud_sync_suspended') then
    raise exception 'Cloud sync suspension was not copied to the Security Center';
  end if;
end;
$phase05$;
reset role;

-- RLS hides rows and rejects writes while suspended; no bytes are removed.
set local role authenticated;
select set_config('request.jwt.claims',
 '{"sub":"f5000000-0000-4000-8000-000000000002","role":"authenticated","aal":"aal2","session_id":"f6000000-0000-4000-8000-000000000002"}',true);
do $phase05$
declare v_status jsonb;
begin
  v_status:=public.lockvyn_cloud_sync_status();
  if v_status->>'suspended'<>'true' or (v_status->>'snapshotCount')::integer<>2 then
    raise exception 'Suspended owner could not see safe sync status: %',v_status;
  end if;
  if exists(select 1 from public.opaque_device_snapshots
      where owner_id='f5000000-0000-4000-8000-000000000002') then
    raise exception 'Suspended owner could still read cloud snapshots';
  end if;
  begin
    insert into public.opaque_device_snapshots
      (owner_id,vault_slot,device_slot,ciphertext_base64,ciphertext_sha256)
    values ('f5000000-0000-4000-8000-000000000002','f7000000-0000-4000-8000-000000000009',
      'f8000000-0000-4000-8000-000000000009',repeat('F',136),repeat('5',64));
    raise exception 'Suspended owner could upload another snapshot';
  exception when insufficient_privilege then null;
  end;
  begin
    perform public.lockvyn_record_cloud_sync_outcome('success',204,null);
    raise exception 'Suspended owner recorded a successful sync';
  exception when sqlstate 'BV002' then null;
  end;
end;
$phase05$;
reset role;

do $phase05$
begin
  if (select count(*) from public.opaque_device_snapshots
      where owner_id='f5000000-0000-4000-8000-000000000002')<>2 then
    raise exception 'Suspension deleted encrypted cloud snapshots';
  end if;
end;
$phase05$;

set local role service_role;
do $phase05$
begin
  if not public.lockvyn_admin_set_cloud_sync_suspension(
      'f5000000-0000-4000-8000-000000000001','f6000000-0000-4000-8000-000000000001',
      'aal2',true,'f5000000-0000-4000-8000-000000000002',false,
      'support_review','sync-target@example.invalid') then
    raise exception 'Fresh AAL2 resume did not apply';
  end if;
  if (select count(*) from lockvyn_private.cloud_sync_admin_audit
      where target_id='f5000000-0000-4000-8000-000000000002'
        and event_type in ('sync_suspended','sync_resumed'))<>2 then
    raise exception 'Suspension/resume audit is incomplete';
  end if;
  begin
    perform public.lockvyn_admin_set_cloud_sync_suspension(
      'f5000000-0000-4000-8000-000000000001','f6000000-0000-4000-8000-000000000001',
      'aal2',true,'f5000000-0000-4000-8000-000000000002',false,
      'support_review','sync-target@example.invalid');
    if (select count(*) from lockvyn_private.cloud_sync_admin_audit
        where target_id='f5000000-0000-4000-8000-000000000002')<>2 then
      raise exception 'No-op resume created an audit row';
    end if;
  end;
end;
$phase05$;
reset role;

set local role authenticated;
select set_config('request.jwt.claims',
 '{"sub":"f5000000-0000-4000-8000-000000000002","role":"authenticated","aal":"aal2","session_id":"f6000000-0000-4000-8000-000000000002"}',true);
do $phase05$
begin
  if (select count(*) from public.opaque_device_snapshots
      where owner_id='f5000000-0000-4000-8000-000000000002')<>2 then
    raise exception 'Resume did not restore cloud snapshot access';
  end if;
end;
$phase05$;
reset role;

rollback;
select 'PASS Phase 05 sync: old-client inserts, derived ciphertext sizes, transactional 64 MiB/64-row quota gates, consented bounded outcome telemetry, AAL2/fresh-TOTP/typed-email admin filters, reversible cloud-only suspension, RLS isolation and ciphertext retention; fixtures rolled back' as result;
