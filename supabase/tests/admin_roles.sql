-- Synthetic role management and immutable audit tests. Transaction rolls back.
begin;
insert into auth.users(id,aud,role,email,email_confirmed_at,created_at,updated_at) values
 ('c7100000-0000-4000-8000-000000000001','authenticated','authenticated','role-owner@example.invalid',now(),now(),now()),
 ('c7100000-0000-4000-8000-000000000002','authenticated','authenticated','role-support@example.invalid',now(),now(),now()),
 ('c7100000-0000-4000-8000-000000000003','authenticated','authenticated','role-security@example.invalid',now(),now(),now());
insert into auth.sessions(id,user_id) values
 ('c7200000-0000-4000-8000-000000000001','c7100000-0000-4000-8000-000000000001'),
 ('c7200000-0000-4000-8000-000000000002','c7100000-0000-4000-8000-000000000002'),
 ('c7200000-0000-4000-8000-000000000003','c7100000-0000-4000-8000-000000000003');
insert into public.lockvyn_admins(user_id) select id from auth.users where email like 'role-%@example.invalid';

do $privileges$
begin
 if not has_table_privilege('service_role','public.lockvyn_admin_roles','select')
   or not has_table_privilege('service_role','public.lockvyn_admin_role_audit','select')
   or has_table_privilege('anon','public.lockvyn_admin_roles','select')
   or has_table_privilege('authenticated','public.lockvyn_admin_roles','select')
   or has_table_privilege('service_role','public.lockvyn_admin_roles','insert')
   or has_table_privilege('service_role','public.lockvyn_admin_role_audit','update')
   or has_table_privilege('service_role','public.lockvyn_admin_role_audit','delete')
   or has_function_privilege('authenticated','public.lockvyn_admin_manage_roles(uuid,uuid,text,boolean,text,uuid,text,uuid)','execute')
 then raise exception 'Admin role grants are too broad'; end if;
 if not exists(select 1 from pg_class c join pg_namespace n on n.oid=c.relnamespace
   where n.nspname='public' and c.relname='lockvyn_admin_roles' and c.relrowsecurity and c.relforcerowsecurity)
 then raise exception 'Admin roles must force RLS'; end if;
end; $privileges$;

do $roles$
declare response jsonb; owner_id uuid:='c7100000-0000-4000-8000-000000000001';
begin
 perform set_config('request.jwt.claim.role','service_role',true);
 perform set_config('request.jwt.claims','{"role":"service_role"}',true);
 response:=public.lockvyn_admin_manage_roles(owner_id,'c7200000-0000-4000-8000-000000000001','aal2',false,'status');
 if response->>'initialized'<>'false' or response->>'role' is not null then raise exception 'Initial role status is incorrect'; end if;
 response:=public.lockvyn_admin_manage_roles(owner_id,'c7200000-0000-4000-8000-000000000001','aal2',true,'bootstrap',
   owner_id,'owner',owner_id);
 if response->>'role'<>'owner' then raise exception 'Owner bootstrap failed'; end if;
 begin
  perform public.lockvyn_admin_manage_roles('c7100000-0000-4000-8000-000000000002',
   'c7200000-0000-4000-8000-000000000002','aal2',true,'assign',
   'c7100000-0000-4000-8000-000000000002','support','c7100000-0000-4000-8000-000000000002');
  raise exception 'Nonowner assigned a role';
 exception when insufficient_privilege then null; end;
 begin
  perform public.lockvyn_admin_manage_roles(owner_id,'c7200000-0000-4000-8000-000000000001','aal2',false,'assign',
   'c7100000-0000-4000-8000-000000000002','support','c7100000-0000-4000-8000-000000000002');
  raise exception 'Role mutation accepted without fresh TOTP';
 exception when insufficient_privilege then null; end;
 response:=public.lockvyn_admin_manage_roles(owner_id,'c7200000-0000-4000-8000-000000000001','aal2',true,'assign',
   'c7100000-0000-4000-8000-000000000002','support','c7100000-0000-4000-8000-000000000002');
 if response->>'role'<>'support' then raise exception 'Support role assignment failed'; end if;
 response:=public.lockvyn_admin_manage_roles(owner_id,'c7200000-0000-4000-8000-000000000001','aal2',true,'assign',
   'c7100000-0000-4000-8000-000000000003','security','c7100000-0000-4000-8000-000000000003');
 if response->>'role'<>'security' then raise exception 'Security role assignment failed'; end if;
 begin
  perform public.lockvyn_admin_manage_roles(owner_id,'c7200000-0000-4000-8000-000000000001','aal2',true,'assign',
   owner_id,'support',owner_id);
  raise exception 'Last owner role was demoted';
 exception when object_not_in_prerequisite_state then null; end;
 begin
  perform public.lockvyn_admin_manage_roles(owner_id,'c7200000-0000-4000-8000-000000000001','aal2',true,'revoke',
   owner_id,'owner',owner_id);
  raise exception 'Last owner role was removed';
 exception when object_not_in_prerequisite_state then null; end;
 begin
  update public.lockvyn_admin_role_audit set action='role_revoked';
  raise exception 'Role audit update was accepted';
 exception when insufficient_privilege then null; end;
 begin
  delete from public.lockvyn_admin_role_audit;
  raise exception 'Role audit deletion was accepted';
 exception when insufficient_privilege then null; end;
 begin
  truncate public.lockvyn_admin_role_audit;
  raise exception 'Role audit truncate was accepted';
 exception when insufficient_privilege then null; end;
 if (select count(*) from public.lockvyn_admin_role_audit)<>3 then
   raise exception 'Expected immutable bootstrap plus two assignment audit events';
 end if;
 if (select count(*) from public.lockvyn_admin_roles)<>3 then raise exception 'Role assignments are missing'; end if;
end; $roles$;
rollback;
