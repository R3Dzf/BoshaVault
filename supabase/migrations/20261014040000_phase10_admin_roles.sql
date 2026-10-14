-- Phase 10: deny-by-default admin roles and immutable role-change audit.
begin;
create table public.lockvyn_admin_roles(
  user_id uuid primary key references public.lockvyn_admins(user_id) on delete cascade,
  role text not null check(role in ('owner','support','security','release')),
  assigned_at timestamptz not null default clock_timestamp()
);
alter table public.lockvyn_admin_roles enable row level security;
alter table public.lockvyn_admin_roles force row level security;
revoke all on public.lockvyn_admin_roles from public,anon,authenticated,service_role;
grant select on public.lockvyn_admin_roles to service_role;
comment on table public.lockvyn_admin_roles is
  'Deny-by-default scoped administrator role. Missing role rows grant no dashboard permissions.';

create table public.lockvyn_admin_role_audit(
  event_id bigint generated always as identity primary key,
  actor_id uuid not null,
  actor_session_id uuid not null,
  target_id uuid not null,
  action text not null check(action in ('owner_bootstrap','role_assigned','role_revoked')),
  role_before text check(role_before is null or role_before in ('owner','support','security','release')),
  role_after text check(role_after is null or role_after in ('owner','support','security','release')),
  created_at timestamptz not null default clock_timestamp(),
  check(role_before is distinct from role_after)
);
create index lockvyn_admin_role_audit_recent_idx on public.lockvyn_admin_role_audit(created_at desc);
alter table public.lockvyn_admin_role_audit enable row level security;
alter table public.lockvyn_admin_role_audit force row level security;
revoke all on public.lockvyn_admin_role_audit from public,anon,authenticated,service_role;
grant select on public.lockvyn_admin_role_audit to service_role;
comment on table public.lockvyn_admin_role_audit is
  'Append-only metadata for scoped admin role changes. No email, password, token, or vault data.';

create or replace function lockvyn_private.reject_admin_role_audit_mutation()
returns trigger language plpgsql security definer set search_path=''
as $$
begin
  raise exception 'Administrator role audit is append-only' using errcode='42501';
end; $$;
revoke all on function lockvyn_private.reject_admin_role_audit_mutation() from public,anon,authenticated,service_role;
create trigger lockvyn_admin_role_audit_immutable
  before update or delete on public.lockvyn_admin_role_audit
  for each row execute function lockvyn_private.reject_admin_role_audit_mutation();
create trigger lockvyn_admin_role_audit_no_truncate
  before truncate on public.lockvyn_admin_role_audit
  for each statement execute function lockvyn_private.reject_admin_role_audit_mutation();

create or replace function public.lockvyn_admin_manage_roles(
  p_actor uuid,p_actor_session uuid,p_actor_aal text,p_fresh_totp boolean,
  p_action text,p_target uuid default null,p_role text default null,p_confirm_id uuid default null)
returns jsonb language plpgsql security definer set search_path=''
as $$
declare
  r text:=coalesce(auth.jwt()->>'role',current_setting('request.jwt.claim.role',true),'');
  prior_role text; owner_count integer;
begin
  if r<>'service_role' or p_actor is null or p_actor_session is null or
     p_actor_aal is distinct from 'aal2' or
     not exists(select 1 from auth.users where id=p_actor and email_confirmed_at is not null) or
     not exists(select 1 from public.lockvyn_admins where user_id=p_actor) or
     public.lockvyn_auth_session_is_active(p_actor,p_actor_session) is distinct from true then
    raise exception 'Active confirmed AAL2 administrator session required' using errcode='42501';
  end if;
  perform pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended('admin-roles',90210));
  if p_action='status' then
    return jsonb_build_object('userId',p_actor,
      'role',(select role from public.lockvyn_admin_roles where user_id=p_actor),
      'initialized',exists(select 1 from public.lockvyn_admin_roles));
  end if;
  if p_action='bootstrap' then
    if p_fresh_totp is distinct from true or p_target is distinct from p_actor or
       p_confirm_id is distinct from p_actor or p_role is distinct from 'owner' or
       not exists(select 1 from public.lockvyn_admins where user_id=p_actor) then
      raise exception 'Fresh verified owner bootstrap required' using errcode='42501';
    end if;
    if exists(select 1 from public.lockvyn_admin_roles) then
      raise exception 'Administrator roles are already initialized' using errcode='55000';
    end if;
    insert into public.lockvyn_admin_roles(user_id,role) values(p_actor,'owner');
    insert into public.lockvyn_admin_role_audit(actor_id,actor_session_id,target_id,action,role_before,role_after)
      values(p_actor,p_actor_session,p_actor,'owner_bootstrap',null,'owner');
    return jsonb_build_object('role','owner','bootstrapped',true);
  end if;
  if not exists(select 1 from public.lockvyn_admin_roles where user_id=p_actor and role='owner') then
    raise exception 'Owner role required' using errcode='42501';
  end if;
  if p_action='list' then
    return coalesce((select jsonb_agg(jsonb_build_object('userId',user_id,'role',role,'assignedAt',assigned_at)
      order by role,user_id) from public.lockvyn_admin_roles),'[]'::jsonb);
  elsif p_action='audit' then
    return coalesce((select jsonb_agg(jsonb_build_object('actorId',actor_id,'targetId',target_id,
      'action',action,'roleBefore',role_before,'roleAfter',role_after,'createdAt',created_at)
      order by event_id desc) from (select * from public.lockvyn_admin_role_audit order by event_id desc limit 100) recent),'[]'::jsonb);
  end if;
  if p_fresh_totp is distinct from true or p_target is null or p_confirm_id is distinct from p_target then
    raise exception 'Fresh TOTP and typed target confirmation required' using errcode='42501';
  end if;
  select role into prior_role from public.lockvyn_admin_roles where user_id=p_target;
  if p_action='assign' then
    if p_role is null or p_role not in ('owner','support','security','release') or
       not exists(select 1 from public.lockvyn_admins where user_id=p_target) or
       not exists(select 1 from auth.users where id=p_target and email_confirmed_at is not null) then
      raise exception 'Target must be a confirmed allowlisted administrator and role must be valid' using errcode='22023';
    end if;
    if prior_role is not distinct from p_role then
      return jsonb_build_object('userId',p_target,'role',p_role,'changed',false);
    end if;
    if prior_role='owner' and p_role<>'owner' then
      select count(*) into owner_count from public.lockvyn_admin_roles where role='owner';
      if owner_count<=1 then raise exception 'The last owner role cannot be changed' using errcode='55000'; end if;
    end if;
    insert into public.lockvyn_admin_roles(user_id,role) values(p_target,p_role)
      on conflict(user_id) do update set role=excluded.role,assigned_at=clock_timestamp();
    insert into public.lockvyn_admin_role_audit(actor_id,actor_session_id,target_id,action,role_before,role_after)
      values(p_actor,p_actor_session,p_target,'role_assigned',prior_role,p_role);
    return jsonb_build_object('userId',p_target,'role',p_role,'changed',true);
  elsif p_action='revoke' then
    if prior_role is null then return jsonb_build_object('userId',p_target,'role',null,'changed',false); end if;
    if prior_role='owner' then
      select count(*) into owner_count from public.lockvyn_admin_roles where role='owner';
      if owner_count<=1 then raise exception 'The last owner role cannot be removed' using errcode='55000'; end if;
    end if;
    delete from public.lockvyn_admin_roles where user_id=p_target;
    insert into public.lockvyn_admin_role_audit(actor_id,actor_session_id,target_id,action,role_before,role_after)
      values(p_actor,p_actor_session,p_target,'role_revoked',prior_role,null);
    return jsonb_build_object('userId',p_target,'role',null,'changed',true);
  end if;
  raise exception 'Invalid role action' using errcode='22023';
end; $$;
revoke all on function public.lockvyn_admin_manage_roles(uuid,uuid,text,boolean,text,uuid,text,uuid)
  from public,anon,authenticated;
grant execute on function public.lockvyn_admin_manage_roles(uuid,uuid,text,boolean,text,uuid,text,uuid) to service_role;
commit;
