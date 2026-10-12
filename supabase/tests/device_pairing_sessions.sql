-- Transaction-only tests. No credential, key or real user data is created.
begin;
insert into auth.users(id,aud,role,email) values
 ('33333333-3333-4333-8333-333333333333','authenticated','authenticated','pairing-a@example.invalid'),
 ('44444444-4444-4444-8444-444444444444','authenticated','authenticated','pairing-b@example.invalid');
set local role authenticated;
select set_config('request.jwt.claims','{"sub":"33333333-3333-4333-8333-333333333333","role":"authenticated","aal":"aal1"}',true);
insert into public.device_pairing_sessions(session_id,owner_id,initiator_public_key) values
 ('55555555-5555-4555-8555-555555555555','33333333-3333-4333-8333-333333333333',repeat('A',124));
do $$ begin
 if (select count(*) from public.device_pairing_sessions) <> 1 then raise exception 'Owner SELECT failed'; end if;
end $$;
update public.device_pairing_sessions
set state='requested', joiner_public_key=repeat('B',124)
where session_id='55555555-5555-4555-8555-555555555555';
do $$ begin
 if not exists(select 1 from public.device_pairing_sessions where state='requested')
 then raise exception 'One-time pairing request failed'; end if;
end $$;
-- Illegal state reset or changing the recipient's identity must fail.
do $$ declare failed boolean:=false; begin
 begin
  update public.device_pairing_sessions set state='waiting',joiner_public_key=null
   where session_id='55555555-5555-4555-8555-555555555555';
 exception when others then failed:=true; end;
 if not failed then raise exception 'Pairing state rollback was permitted'; end if;
end $$;
update public.device_pairing_sessions set state='approved',sealed_pairing=repeat('C',200)
 where session_id='55555555-5555-4555-8555-555555555555';
do $$ declare failed boolean:=false; begin
 begin
  update public.device_pairing_sessions set joiner_public_key=repeat('D',124)
   where session_id='55555555-5555-4555-8555-555555555555';
 exception when others then failed:=true; end;
 if not failed then raise exception 'Approved recipient could be replaced'; end if;
end $$;
select set_config('request.jwt.claims','{"sub":"44444444-4444-4444-8444-444444444444","role":"authenticated","aal":"aal1"}',true);
do $$ declare n integer; begin
 if exists(select 1 from public.device_pairing_sessions) then raise exception 'Cross-owner SELECT leaked sessions'; end if;
 update public.device_pairing_sessions set state='cancelled';
 get diagnostics n=row_count;
 if n<>0 then raise exception 'Cross-owner UPDATE touched sessions'; end if;
 delete from public.device_pairing_sessions;
 get diagnostics n=row_count;
 if n<>0 then raise exception 'Cross-owner DELETE touched sessions'; end if;
end $$;
set local role anon;
do $$ declare blocked boolean:=false; begin
 begin perform 1 from public.device_pairing_sessions; exception when insufficient_privilege then blocked:=true; end;
 if not blocked then raise exception 'Anonymous sessions visible'; end if;
end $$;
rollback;
select 'device_pairing_rls_passed' as result;
