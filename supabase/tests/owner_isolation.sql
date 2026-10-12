-- Transaction-only tests: synthetic identities, no email or persistent user data.
-- These exercise Postgres roles/RLS; HTTP/JWT validation is tested separately.
begin;
insert into auth.users(id,aud,role,email) values
 ('11111111-1111-4111-8111-111111111111','authenticated','authenticated','boshavault-rls-a@example.invalid'),
 ('22222222-2222-4222-8222-222222222222','authenticated','authenticated','boshavault-rls-b@example.invalid');
set local role authenticated;
select set_config('request.jwt.claims','{"sub":"11111111-1111-4111-8111-111111111111","role":"authenticated"}',true);
insert into public.opaque_device_snapshots(owner_id,vault_slot,device_slot,ciphertext_base64,ciphertext_sha256)
values ('11111111-1111-4111-8111-111111111111','aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa','bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',repeat('A',200),repeat('0',64));
do $$ begin
 if (select count(*) from public.opaque_device_snapshots) <> 1 then raise exception 'Owner SELECT failed'; end if;
 begin
  insert into public.opaque_device_snapshots(owner_id,vault_slot,device_slot,ciphertext_base64,ciphertext_sha256)
  values ('22222222-2222-4222-8222-222222222222','aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa','bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',repeat('A',200),repeat('0',64));
  raise exception 'Cross-owner INSERT was allowed';
 exception when insufficient_privilege then null; end;
 begin
  update public.opaque_device_snapshots set owner_id='22222222-2222-4222-8222-222222222222';
  raise exception 'Owner reassignment was allowed';
 exception when insufficient_privilege then null; end;
end $$;
update public.opaque_device_snapshots set ciphertext_sha256=repeat('1',64),updated_at='2000-01-01';
do $$ begin
 if not exists(select 1 from public.opaque_device_snapshots where ciphertext_sha256=repeat('1',64) and updated_at >= now()) then raise exception 'Owner UPDATE or timestamp trigger failed'; end if;
end $$;
select set_config('request.jwt.claims','{"sub":"22222222-2222-4222-8222-222222222222","role":"authenticated"}',true);
do $$ declare n integer; begin
 if exists(select 1 from public.opaque_device_snapshots) then raise exception 'Cross-owner SELECT exposed data'; end if;
 update public.opaque_device_snapshots set ciphertext_sha256=repeat('2',64); get diagnostics n=row_count;
 if n <> 0 then raise exception 'Cross-owner UPDATE changed data'; end if;
 delete from public.opaque_device_snapshots; get diagnostics n=row_count;
 if n <> 0 then raise exception 'Cross-owner DELETE removed data'; end if;
end $$;
select set_config('request.jwt.claims','{}',true);
do $$ begin
 if exists(select 1 from public.opaque_device_snapshots) then raise exception 'Missing identity exposed data'; end if;
end $$;
set local role anon;
do $$ begin
 begin perform 1 from public.opaque_device_snapshots; raise exception 'Anon SELECT was allowed'; exception when insufficient_privilege then null; end;
 begin delete from public.opaque_device_snapshots; raise exception 'Anon DELETE was allowed'; exception when insufficient_privilege then null; end;
end $$;
set local role authenticated;
select set_config('request.jwt.claims','{"sub":"11111111-1111-4111-8111-111111111111","role":"authenticated"}',true);
delete from public.opaque_device_snapshots;
do $$ begin if exists(select 1 from public.opaque_device_snapshots) then raise exception 'Owner DELETE failed'; end if; end $$;
rollback;
select 'owner_isolation_passed' as result;
