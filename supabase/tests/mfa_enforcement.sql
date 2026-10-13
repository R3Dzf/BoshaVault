-- Synthetic accounts and rows only. Rollback leaves no test records.
begin;
insert into auth.users(id,aud,role,email,created_at,updated_at)
values ('00000000-0000-4000-8000-000000000fa1','authenticated','authenticated','mfa-rls-synthetic@example.invalid',now(),now()),
       ('00000000-0000-4000-8000-000000000fb1','authenticated','authenticated','mfa-rls-other@example.invalid',now(),now());
insert into auth.sessions(id,user_id) values
 ('10000000-0000-4000-8000-00000000fa11','00000000-0000-4000-8000-000000000fa1'),
 ('10000000-0000-4000-8000-00000000fb11','00000000-0000-4000-8000-000000000fb1');
insert into public.opaque_device_snapshots(owner_id,vault_slot,device_slot,ciphertext_base64,ciphertext_sha256)
values ('00000000-0000-4000-8000-000000000fa1','00000000-0000-4000-8000-000000000fa2','00000000-0000-4000-8000-000000000fa3',repeat('A',136),repeat('0',64));
set local role authenticated;
select set_config('request.jwt.claims','{"sub":"00000000-0000-4000-8000-000000000fa1","role":"authenticated","aal":"aal1","session_id":"10000000-0000-4000-8000-00000000fa11"}',true);
do $$ begin
    if (select count(*) from public.opaque_device_snapshots) <> 1 then raise exception 'Non-MFA account was blocked'; end if;
end $$;
reset role;
insert into auth.mfa_factors(id,user_id,friendly_name,factor_type,status,secret,created_at,updated_at)
values ('00000000-0000-4000-8000-000000000fa4','00000000-0000-4000-8000-000000000fa1','Synthetic pending','totp','unverified','synthetic-not-real-secret',now(),now());
set local role authenticated;
do $$ begin
    if (select count(*) from public.opaque_device_snapshots) <> 1 then raise exception 'Unverified enrollment was treated as enabled'; end if;
end $$;
reset role;
update auth.mfa_factors set status='verified' where id='00000000-0000-4000-8000-000000000fa4';
set local role authenticated;
do $$ begin
    if (select count(*) from public.opaque_device_snapshots) <> 0 then raise exception 'AAL1 bypassed MFA'; end if;
    update public.opaque_device_snapshots set ciphertext_sha256=repeat('1',64);
    if found then raise exception 'AAL1 updated a row'; end if;
    delete from public.opaque_device_snapshots;
    if found then raise exception 'AAL1 deleted a row'; end if;
    begin
        insert into public.opaque_device_snapshots(owner_id,vault_slot,device_slot,ciphertext_base64,ciphertext_sha256)
        values ('00000000-0000-4000-8000-000000000fa1','00000000-0000-4000-8000-000000000fa2','00000000-0000-4000-8000-000000000fa5',repeat('A',136),repeat('0',64));
        raise exception 'AAL1 inserted a row';
    exception when insufficient_privilege then null;
    end;
end $$;
select set_config('request.jwt.claims','{"sub":"00000000-0000-4000-8000-000000000fa1","role":"authenticated","aal":"aal2","session_id":"10000000-0000-4000-8000-00000000fa11"}',true);
do $$ begin
    if (select count(*) from public.opaque_device_snapshots) <> 1 then raise exception 'AAL2 owner could not read'; end if;
    update public.opaque_device_snapshots set ciphertext_sha256=repeat('2',64);
    if not found then raise exception 'AAL2 owner could not update'; end if;
end $$;
select set_config('request.jwt.claims','{"sub":"00000000-0000-4000-8000-000000000fb1","role":"authenticated","aal":"aal2","session_id":"10000000-0000-4000-8000-00000000fb11"}',true);
do $$ begin
    if (select count(*) from public.opaque_device_snapshots) <> 0 then raise exception 'AAL2 bypassed ownership'; end if;
end $$;
select set_config('request.jwt.claims','{"sub":"00000000-0000-4000-8000-000000000fa1","role":"authenticated","session_id":"10000000-0000-4000-8000-00000000fa11"}',true);
do $$ begin
    if (select count(*) from public.opaque_device_snapshots) <> 0 then raise exception 'Missing aal bypassed MFA'; end if;
end $$;
reset role;
rollback;
select 'PASS optional MFA: pending enrollment, AAL1 read/write denial, AAL2 owner access, cross-owner denial, missing claim denial; all synthetic changes rolled back' as result;
