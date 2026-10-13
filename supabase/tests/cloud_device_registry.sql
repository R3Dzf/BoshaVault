-- Transaction-only tests. All users, Auth sessions, snapshots, and audit rows are synthetic.
-- Run after the local Auth/session, snapshot, pairing, and device-registry migrations.
begin;

insert into auth.users(id,aud,role,email,created_at,updated_at) values
 ('00000000-0000-4000-8000-000000000d01','authenticated','authenticated','device-target@example.invalid',now(),now()),
 ('00000000-0000-4000-8000-000000000d02','authenticated','authenticated','device-owner@example.invalid',now(),now());
insert into auth.sessions(id,user_id) values
 ('10000000-0000-4000-8000-000000000d01','00000000-0000-4000-8000-000000000d01'),
 ('10000000-0000-4000-8000-000000000d02','00000000-0000-4000-8000-000000000d01'),
 ('10000000-0000-4000-8000-000000000d03','00000000-0000-4000-8000-000000000d02');

set local role authenticated;
select set_config('request.jwt.claims','{"sub":"00000000-0000-4000-8000-000000000d01","role":"authenticated","aal":"aal1","session_id":"10000000-0000-4000-8000-000000000d01"}',true);

-- Existing, unregistered sessions retain the old cloud behavior during opt-in rollout.
insert into public.opaque_device_snapshots(owner_id,vault_slot,device_slot,ciphertext_base64,ciphertext_sha256)
values ('00000000-0000-4000-8000-000000000d01','20000000-0000-4000-8000-000000000d01','30000000-0000-4000-8000-000000000d01',repeat('A',136),repeat('0',64));

select set_config(
  'lockvyn.test.device_target',
  public.lockvyn_register_cloud_device(true,'windows','11 24H2','3.1.0','Test laptop',1::smallint)::text,
  true
);

do $$ begin
  if (select count(*) from public.lockvyn_list_cloud_devices()) <> 1 then
    raise exception 'Owner could not list its consented device';
  end if;
  if not exists (
    select 1 from public.lockvyn_list_cloud_devices()
    where device_id = current_setting('lockvyn.test.device_target')::uuid
      and platform = 'windows' and os_version = '11 24H2'
      and app_version = '3.1.0' and registered_at <= clock_timestamp()
  ) then
    raise exception 'Versioned server-timestamped device metadata missing';
  end if;
end $$;

do $$ begin
  begin
    perform public.lockvyn_register_cloud_device(false,'windows','11','3.1.0',null,1::smallint);
    raise exception 'Registration without explicit consent was allowed';
  exception when insufficient_privilege then null;
  end;
  begin
    perform public.lockvyn_revoke_cloud_device(current_setting('lockvyn.test.device_target')::uuid);
    raise exception 'AAL1 revoked a cloud device';
  exception when insufficient_privilege then null;
  end;
end $$;

select public.lockvyn_record_cloud_device_sync();
do $$ begin
  if not exists (
    select 1 from public.lockvyn_list_cloud_devices()
    where device_id = current_setting('lockvyn.test.device_target')::uuid
      and last_sync_at is not null and last_sync_at >= registered_at
  ) then
    raise exception 'Server-side heartbeat timestamp was not recorded';
  end if;
end $$;

-- A second live session for the same owner remains usable after the target is revoked.
select set_config('request.jwt.claims','{"sub":"00000000-0000-4000-8000-000000000d01","role":"authenticated","aal":"aal2","session_id":"10000000-0000-4000-8000-000000000d02"}',true);
select set_config(
  'lockvyn.test.device_peer',
  public.lockvyn_register_cloud_device(true,'android','15','3.1.0',null,1::smallint)::text,
  true
);

-- Another owner cannot enumerate or revoke the target owner’s device.
select set_config('request.jwt.claims','{"sub":"00000000-0000-4000-8000-000000000d02","role":"authenticated","aal":"aal2","session_id":"10000000-0000-4000-8000-000000000d03"}',true);
do $$ begin
  if (select count(*) from public.lockvyn_list_cloud_devices()) <> 0 then
    raise exception 'Cross-owner device listing exposed metadata';
  end if;
  if public.lockvyn_revoke_cloud_device(current_setting('lockvyn.test.device_target')::uuid) then
    raise exception 'Cross-owner device revocation succeeded';
  end if;
end $$;
insert into public.opaque_device_snapshots(owner_id,vault_slot,device_slot,ciphertext_base64,ciphertext_sha256)
values ('00000000-0000-4000-8000-000000000d02','20000000-0000-4000-8000-000000000d02','30000000-0000-4000-8000-000000000d02',repeat('B',136),repeat('1',64));

-- The server caps revocation at 10 actions per minute per actor Auth session.
reset role;
insert into lockvyn_private.cloud_device_audit(owner_id,actor_user_id,device_id,actor_session_id,target_session_id,action)
select '00000000-0000-4000-8000-000000000d01',
       '00000000-0000-4000-8000-000000000d01',
       current_setting('lockvyn.test.device_target')::uuid,
       '10000000-0000-4000-8000-000000000d02',
       ('50000000-0000-4000-8000-' || lpad(g::text,12,'0'))::uuid,
       'cloud_session_revoked'
from generate_series(1,10) as g;
set local role authenticated;
select set_config('request.jwt.claims','{"sub":"00000000-0000-4000-8000-000000000d01","role":"authenticated","aal":"aal2","session_id":"10000000-0000-4000-8000-000000000d02"}',true);
do $$ begin
  begin
    perform public.lockvyn_revoke_cloud_device(current_setting('lockvyn.test.device_target')::uuid);
    raise exception 'Revocation rate limit was not enforced';
  exception when sqlstate '54000' then null;
  end;
end $$;
reset role;
delete from lockvyn_private.cloud_device_audit
where owner_id='00000000-0000-4000-8000-000000000d01'
  and actor_session_id='10000000-0000-4000-8000-000000000d02'
  and device_id=current_setting('lockvyn.test.device_target')::uuid
  and action='cloud_session_revoked';
set local role authenticated;
select set_config('request.jwt.claims','{"sub":"00000000-0000-4000-8000-000000000d01","role":"authenticated","aal":"aal2","session_id":"10000000-0000-4000-8000-000000000d02"}',true);
-- The owner's AAL2 session revokes the selected Auth session and creates one audit record.
do $$ begin
  if not public.lockvyn_revoke_cloud_device(current_setting('lockvyn.test.device_target')::uuid) then
    raise exception 'Owner AAL2 revocation failed';
  end if;
  if not exists (
    select 1 from public.lockvyn_list_cloud_devices()
    where device_id = current_setting('lockvyn.test.device_target')::uuid and revoked_at is not null
  ) then
    raise exception 'Revoked device status missing from owner listing';
  end if;
end $$;

-- The other session can still sync. The revoked session loses snapshot read/write and pairing access.
insert into public.opaque_device_snapshots(owner_id,vault_slot,device_slot,ciphertext_base64,ciphertext_sha256)
values ('00000000-0000-4000-8000-000000000d01','20000000-0000-4000-8000-000000000d01','30000000-0000-4000-8000-000000000d02',repeat('C',136),repeat('2',64));
do $$ begin
  if (select count(*) from public.opaque_device_snapshots
      where owner_id='00000000-0000-4000-8000-000000000d01') <> 2 then
    raise exception 'A separate active session was blocked';
  end if;
end $$;

select set_config('request.jwt.claims','{"sub":"00000000-0000-4000-8000-000000000d01","role":"authenticated","aal":"aal1","session_id":"10000000-0000-4000-8000-000000000d01"}',true);
do $$ declare n integer; begin
  if exists (select 1 from public.opaque_device_snapshots
             where owner_id='00000000-0000-4000-8000-000000000d01') then
    raise exception 'Revoked session could still read snapshots';
  end if;
  update public.opaque_device_snapshots set ciphertext_sha256=repeat('3',64)
    where owner_id='00000000-0000-4000-8000-000000000d01';
  get diagnostics n = row_count;
  if n <> 0 then raise exception 'Revoked session could update snapshots'; end if;
  begin
    insert into public.opaque_device_snapshots(owner_id,vault_slot,device_slot,ciphertext_base64,ciphertext_sha256)
    values ('00000000-0000-4000-8000-000000000d01','20000000-0000-4000-8000-000000000d01','30000000-0000-4000-8000-000000000d03',repeat('D',136),repeat('4',64));
    raise exception 'Revoked session could insert snapshots';
  exception when insufficient_privilege then null;
  end;
  if exists (select 1 from public.device_pairing_sessions
             where owner_id='00000000-0000-4000-8000-000000000d01') then
    raise exception 'Revoked session could read pairing data';
  end if;
  begin
    insert into public.device_pairing_sessions(session_id,owner_id,initiator_public_key)
    values ('40000000-0000-4000-8000-000000000d01','00000000-0000-4000-8000-000000000d01',repeat('A',120));
    raise exception 'Revoked session could create pairing data';
  exception when insufficient_privilege then null;
  end;
  begin
    perform public.lockvyn_register_cloud_device(true,'windows','11','3.1.0',null,1::smallint);
    raise exception 'Revoked Auth session registered again';
  exception when insufficient_privilege then null;
  end;
  begin
    perform public.lockvyn_record_cloud_device_sync();
    raise exception 'Revoked Auth session recorded a sync heartbeat';
  exception when insufficient_privilege then null;
  end;
  begin
    perform 1 from lockvyn_private.cloud_device_registry;
    raise exception 'Authenticated role directly read private device registry';
  exception when insufficient_privilege then null;
  end;
end $$;

reset role;
do $$ begin
  if (select count(*) from public.opaque_device_snapshots
      where owner_id='00000000-0000-4000-8000-000000000d01'
        and device_slot='30000000-0000-4000-8000-000000000d01') <> 1 then
    raise exception 'Revocation deleted encrypted snapshot data';
  end if;
  if (select count(*) from lockvyn_private.cloud_device_audit
      where owner_id='00000000-0000-4000-8000-000000000d01'
        and target_session_id='10000000-0000-4000-8000-000000000d01'
        and actor_session_id='10000000-0000-4000-8000-000000000d02'
        and action='cloud_session_revoked') <> 1 then
    raise exception 'Revocation audit record missing or duplicated';
  end if;
end $$;

rollback;
select 'PASS synthetic device registry: opt-in, metadata, heartbeat, AAL2, owner isolation, session-only revocation, pairing denial, audit, legacy compatibility, ciphertext retention; rolled back' as result;
