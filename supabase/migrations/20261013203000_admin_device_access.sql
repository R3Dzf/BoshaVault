-- Phase 03: administrator inventory and step-up revocation for opted-in sessions.
begin;

alter table lockvyn_private.cloud_device_audit
  add column if not exists actor_user_id uuid;
update lockvyn_private.cloud_device_audit
   set actor_user_id = owner_id
 where actor_user_id is null;
alter table lockvyn_private.cloud_device_audit
  alter column actor_user_id set not null;
create index if not exists cloud_device_audit_actor_session_recent_idx
  on lockvyn_private.cloud_device_audit(actor_user_id, actor_session_id, occurred_at desc);

-- Keep the owner-facing RPC compatible with the expanded private audit schema,
-- and count rate limits against the acting user's own Auth session.
create or replace function public.lockvyn_revoke_cloud_device(p_device_id uuid)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_owner uuid := (select auth.uid());
  v_actor_session uuid := (select lockvyn_private.request_session_id());
  v_target_session uuid;
  v_target_id uuid;
  v_revoked_at timestamptz;
begin
  if not (select lockvyn_private.cloud_assurance_allowed())
     or not (select lockvyn_private.cloud_device_session_allowed())
     or coalesce((select auth.jwt()->>'aal'),'') <> 'aal2'
     or v_owner is null or v_actor_session is null then
    raise exception 'Verified AAL2 is required to revoke cloud device access' using errcode = '42501';
  end if;

  perform pg_catalog.pg_advisory_xact_lock(
    pg_catalog.hashtextextended(v_owner::text || ':' || v_actor_session::text, 69331)
  );
  if (
    select count(*) >= 10
    from lockvyn_private.cloud_device_audit a
    where a.actor_user_id = v_owner
      and a.actor_session_id = v_actor_session
      and a.action = 'cloud_session_revoked'
      and a.occurred_at > statement_timestamp() - interval '1 minute'
  ) then
    raise exception 'Cloud device revocation rate limit reached' using errcode = '54000';
  end if;

  select d.id, d.auth_session_id, d.revoked_at
    into v_target_id, v_target_session, v_revoked_at
  from lockvyn_private.cloud_device_registry d
  where d.id = p_device_id and d.owner_id = v_owner
  for update;

  if not found or v_revoked_at is not null then
    return false;
  end if;

  v_revoked_at := clock_timestamp();
  update lockvyn_private.cloud_device_registry
     set revoked_at = v_revoked_at,
         revoked_by_session_id = v_actor_session
   where owner_id = v_owner
     and auth_session_id = v_target_session
     and revoked_at is null;

  if not found then
    return false;
  end if;

  insert into lockvyn_private.cloud_device_audit (
    owner_id, actor_user_id, device_id, actor_session_id, target_session_id, action, occurred_at
  ) values (
    v_owner, v_owner, v_target_id, v_actor_session, v_target_session, 'cloud_session_revoked', v_revoked_at
  );
  return true;
end;
$$;
revoke all on function public.lockvyn_revoke_cloud_device(uuid) from public, anon, authenticated;
grant execute on function public.lockvyn_revoke_cloud_device(uuid) to authenticated;

create or replace function public.lockvyn_admin_list_cloud_devices(
  p_actor uuid,
  p_target uuid
)
returns table (
    device_id uuid,
    display_name text,
    platform text,
    os_version text,
    app_version text,
    schema_version smallint,
    registered_at timestamptz,
    last_sync_at timestamptz,
    revoked_at timestamptz
)
language plpgsql
stable
security definer
set search_path = ''
as $$
begin
  if p_actor is null or p_target is null or p_actor = p_target
     or not exists (select 1 from public.lockvyn_admins a where a.user_id = p_actor)
     or exists (select 1 from public.lockvyn_admins a where a.user_id = p_target) then
    raise exception 'Device inventory is not available for this account' using errcode = '42501';
  end if;
  if not exists (select 1 from auth.users u where u.id = p_target) then
    raise exception 'Target account not found' using errcode = '22023';
  end if;

  return query
  select d.id, d.display_name, d.platform, d.os_version, d.app_version,
         d.schema_version, d.registered_at, d.last_sync_at, d.revoked_at
  from lockvyn_private.cloud_device_registry d
  where d.owner_id = p_target
  order by d.revoked_at nulls first, d.last_sync_at desc nulls last, d.registered_at desc
  limit 100;
end;
$$;
revoke all on function public.lockvyn_admin_list_cloud_devices(uuid,uuid)
  from public, anon, authenticated, service_role;
grant execute on function public.lockvyn_admin_list_cloud_devices(uuid,uuid)
  to service_role;
comment on function public.lockvyn_admin_list_cloud_devices(uuid,uuid) is
  'Service-role-only, allowlist-checked inventory view. Returns self-reported session metadata, never Auth session or vault IDs.';

create or replace function public.lockvyn_admin_revoke_cloud_device(
  p_actor uuid,
  p_actor_session uuid,
  p_actor_aal text,
  p_fresh_totp boolean,
  p_target uuid,
  p_device_id uuid,
  p_confirm_email text
)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_target_email text;
  v_target_session uuid;
  v_revoked_at timestamptz;
begin
  if p_actor is null or p_actor_session is null or p_target is null or p_device_id is null
     or p_actor = p_target
     or p_actor_aal is distinct from 'aal2'
     or p_fresh_totp is distinct from true
     or not exists (select 1 from public.lockvyn_admins a where a.user_id = p_actor)
     or exists (select 1 from public.lockvyn_admins a where a.user_id = p_target)
     or not public.lockvyn_auth_session_is_active(p_actor, p_actor_session) then
    raise exception 'A fresh verified administrator session is required' using errcode = '42501';
  end if;

  select u.email into v_target_email
  from auth.users u
  where u.id = p_target and u.email is not null;

  if v_target_email is null or p_confirm_email is null
     or pg_catalog.lower(v_target_email) <> pg_catalog.lower(pg_catalog.btrim(p_confirm_email)) then
    raise exception 'Target email confirmation does not match' using errcode = '42501';
  end if;

  perform pg_catalog.pg_advisory_xact_lock(
    pg_catalog.hashtextextended(p_actor::text || ':' || p_actor_session::text, 69331)
  );
  if (
    select count(*) >= 10
    from lockvyn_private.cloud_device_audit a
    where a.actor_user_id = p_actor
      and a.actor_session_id = p_actor_session
      and a.action = 'cloud_session_revoked'
      and a.occurred_at > statement_timestamp() - interval '1 minute'
  ) then
    raise exception 'Cloud device revocation rate limit reached' using errcode = '54000';
  end if;

  select d.auth_session_id, d.revoked_at
    into v_target_session, v_revoked_at
  from lockvyn_private.cloud_device_registry d
  where d.id = p_device_id and d.owner_id = p_target
  for update;

  if not found or v_revoked_at is not null then
    return false;
  end if;

  v_revoked_at := clock_timestamp();
  update lockvyn_private.cloud_device_registry
     set revoked_at = v_revoked_at,
         revoked_by_session_id = p_actor_session
   where owner_id = p_target
     and auth_session_id = v_target_session
     and revoked_at is null;

  if not found then
    return false;
  end if;

  insert into lockvyn_private.cloud_device_audit (
    owner_id, actor_user_id, device_id, actor_session_id, target_session_id, action, occurred_at
  ) values (
    p_target, p_actor, p_device_id, p_actor_session, v_target_session, 'cloud_session_revoked', v_revoked_at
  );
  return true;
end;
$$;
revoke all on function public.lockvyn_admin_revoke_cloud_device(uuid,uuid,text,boolean,uuid,uuid,text)
  from public, anon, authenticated, service_role;
grant execute on function public.lockvyn_admin_revoke_cloud_device(uuid,uuid,text,boolean,uuid,uuid,text)
  to service_role;
comment on function public.lockvyn_admin_revoke_cloud_device(uuid,uuid,text,boolean,uuid,uuid,text) is
  'Service-role-only admin action. Rechecks allowlist, protected targets, live signed Auth session, AAL2, fresh TOTP, target email, and rate limit before setting server-side revocation and audit metadata.';

commit;
