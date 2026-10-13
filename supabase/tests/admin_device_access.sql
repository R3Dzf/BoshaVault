-- Service-only administrator device actions; all identities and sessions are synthetic.
-- Transaction rollback removes fixtures, registry rows, and audit events.
begin;

insert into auth.users(id,aud,role,email,email_confirmed_at,created_at,updated_at) values
 ('00000000-0000-4000-8000-00000000e001','authenticated','authenticated','admin-device-a@example.invalid',now(),now(),now()),
 ('00000000-0000-4000-8000-00000000e002','authenticated','authenticated','admin-device-b@example.invalid',now(),now(),now()),
 ('00000000-0000-4000-8000-00000000e003','authenticated','authenticated','target-device@example.invalid',now(),now(),now());
insert into auth.sessions(id,user_id) values
 ('10000000-0000-4000-8000-00000000e001','00000000-0000-4000-8000-00000000e001'),
 ('10000000-0000-4000-8000-00000000e003','00000000-0000-4000-8000-00000000e003'),
 ('10000000-0000-4000-8000-00000000e004','00000000-0000-4000-8000-00000000e003');
insert into public.lockvyn_admins(user_id) values
 ('00000000-0000-4000-8000-00000000e001'),
 ('00000000-0000-4000-8000-00000000e002');

insert into lockvyn_private.cloud_device_registry(
  id,owner_id,auth_session_id,schema_version,platform,os_version,app_version,display_name
) values
 ('60000000-0000-4000-8000-00000000e001','00000000-0000-4000-8000-00000000e003',
  '10000000-0000-4000-8000-00000000e003',1,'windows','11','3.1.0','Synthetic laptop'),
 ('60000000-0000-4000-8000-00000000e002','00000000-0000-4000-8000-00000000e003',
  '10000000-0000-4000-8000-00000000e004',1,'android','15','3.1.0','Synthetic phone');
insert into public.opaque_device_snapshots(owner_id,vault_slot,device_slot,ciphertext_base64,ciphertext_sha256)
values ('00000000-0000-4000-8000-00000000e003','20000000-0000-4000-8000-00000000e001',
        '30000000-0000-4000-8000-00000000e001',repeat('A',136),repeat('0',64));

set local role authenticated;
select set_config('request.jwt.claims','{"sub":"00000000-0000-4000-8000-00000000e003","role":"authenticated","aal":"aal1","session_id":"10000000-0000-4000-8000-00000000e003"}',true);
do $$ begin
  if (select count(*) from public.opaque_device_snapshots) <> 1 then
    raise exception 'Target session was not active before admin revocation';
  end if;
end $$;
reset role;

set local role service_role;
do $$ begin
  if (select count(*) from public.lockvyn_admin_list_cloud_devices(
      '00000000-0000-4000-8000-00000000e001','00000000-0000-4000-8000-00000000e003')) <> 2 then
    raise exception 'Allowlisted admin could not list opted-in sessions';
  end if;
  if exists (
    select 1 from public.lockvyn_admin_list_cloud_devices(
      '00000000-0000-4000-8000-00000000e001','00000000-0000-4000-8000-00000000e003')
    where platform not in ('windows','android') or schema_version <> 1
  ) then
    raise exception 'Admin inventory metadata was incomplete';
  end if;
  begin
    perform 1 from public.lockvyn_admin_list_cloud_devices(
      '00000000-0000-4000-8000-00000000e001','00000000-0000-4000-8000-00000000e002');
    raise exception 'Admin could list another administrator';
  exception when insufficient_privilege then null;
  end;
  begin
    perform public.lockvyn_admin_revoke_cloud_device(
      '00000000-0000-4000-8000-00000000e001','10000000-0000-4000-8000-00000000e001',
      'aal1',true,'00000000-0000-4000-8000-00000000e003',
      '60000000-0000-4000-8000-00000000e001','target-device@example.invalid');
    raise exception 'AAL1 admin revoked a device session';
  exception when insufficient_privilege then null;
  end;
  begin
    perform public.lockvyn_admin_revoke_cloud_device(
      '00000000-0000-4000-8000-00000000e001','10000000-0000-4000-8000-00000000e001',
      'aal2',false,'00000000-0000-4000-8000-00000000e003',
      '60000000-0000-4000-8000-00000000e001','target-device@example.invalid');
    raise exception 'Admin action without fresh TOTP was allowed';
  exception when insufficient_privilege then null;
  end;
  begin
    perform public.lockvyn_admin_revoke_cloud_device(
      '00000000-0000-4000-8000-00000000e001','10000000-0000-4000-8000-00000000e001',
      'aal2',true,'00000000-0000-4000-8000-00000000e003',
      '60000000-0000-4000-8000-00000000e001','wrong@example.invalid');
    raise exception 'Incorrect target email was accepted';
  exception when insufficient_privilege then null;
  end;
  begin
    perform public.lockvyn_admin_revoke_cloud_device(
      '00000000-0000-4000-8000-00000000e002','10000000-0000-4000-8000-00000000e001',
      'aal2',true,'00000000-0000-4000-8000-00000000e003',
      '60000000-0000-4000-8000-00000000e001','target-device@example.invalid');
    raise exception 'Non-allowlisted actor revoked a device';
  exception when insufficient_privilege then null;
  end;
  if not public.lockvyn_admin_revoke_cloud_device(
      '00000000-0000-4000-8000-00000000e001','10000000-0000-4000-8000-00000000e001',
      'aal2',true,'00000000-0000-4000-8000-00000000e003',
      '60000000-0000-4000-8000-00000000e001','target-device@example.invalid') then
    raise exception 'Fresh AAL2 admin revocation failed';
  end if;
  if not exists (
    select 1 from public.lockvyn_admin_list_cloud_devices(
      '00000000-0000-4000-8000-00000000e001','00000000-0000-4000-8000-00000000e003')
    where device_id='60000000-0000-4000-8000-00000000e001' and revoked_at is not null
  ) then
    raise exception 'Revocation state not visible to administrator';
  end if;
end $$;
reset role;

-- Ten prior actions in one minute stop an eleventh across target accounts.
insert into lockvyn_private.cloud_device_audit(
  owner_id,actor_user_id,device_id,actor_session_id,target_session_id,action
)
select '00000000-0000-4000-8000-00000000e003',
       '00000000-0000-4000-8000-00000000e001',
       '60000000-0000-4000-8000-00000000e002',
       '10000000-0000-4000-8000-00000000e001',
       ('80000000-0000-4000-8000-' || lpad(g::text,12,'0'))::uuid,
       'cloud_session_revoked'
from generate_series(1,9) as g;

set local role service_role;
do $$ begin
  begin
    perform public.lockvyn_admin_revoke_cloud_device(
      '00000000-0000-4000-8000-00000000e001','10000000-0000-4000-8000-00000000e001',
      'aal2',true,'00000000-0000-4000-8000-00000000e003',
      '60000000-0000-4000-8000-00000000e002','target-device@example.invalid');
    raise exception 'Admin revocation rate limit was not enforced';
  exception when sqlstate '54000' then null;
  end;
end $$;
reset role;
delete from lockvyn_private.cloud_device_audit
where owner_id='00000000-0000-4000-8000-00000000e003'
  and actor_user_id='00000000-0000-4000-8000-00000000e001'
  and actor_session_id='10000000-0000-4000-8000-00000000e001'
  and device_id='60000000-0000-4000-8000-00000000e002';

-- The revoked target session is denied while a distinct registered session stays active.
set local role authenticated;
select set_config('request.jwt.claims','{"sub":"00000000-0000-4000-8000-00000000e003","role":"authenticated","aal":"aal1","session_id":"10000000-0000-4000-8000-00000000e003"}',true);
do $$ begin
  if exists (select 1 from public.opaque_device_snapshots) then
    raise exception 'Admin-revoked session could still read snapshots';
  end if;
end $$;
select set_config('request.jwt.claims','{"sub":"00000000-0000-4000-8000-00000000e003","role":"authenticated","aal":"aal1","session_id":"10000000-0000-4000-8000-00000000e004"}',true);
do $$ begin
  if (select count(*) from public.opaque_device_snapshots) <> 1 then
    raise exception 'Second device session was blocked';
  end if;
end $$;
reset role;

do $$ begin
  if (select count(*) from public.opaque_device_snapshots
      where owner_id='00000000-0000-4000-8000-00000000e003') <> 1 then
    raise exception 'Admin revocation deleted encrypted data';
  end if;
  if (select count(*) from lockvyn_private.cloud_device_audit
      where owner_id='00000000-0000-4000-8000-00000000e003'
        and actor_user_id='00000000-0000-4000-8000-00000000e001'
        and actor_session_id='10000000-0000-4000-8000-00000000e001'
        and target_session_id='10000000-0000-4000-8000-00000000e003') <> 1 then
    raise exception 'Administrator revocation was not audited exactly once';
  end if;
end $$;

rollback;
select 'PASS admin device access: allowlist, protected targets, AAL2, fresh TOTP, email confirmation, cross-target rate limit, session RLS, second-session access, ciphertext retention, audit; fixtures rolled back' as result;
