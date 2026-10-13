-- Privacy-limited, explicit-consent registry for signed-in Lockvyn cloud sessions.
-- This identifies an Auth session, not a physical device. Older clients remain
-- compatible until they opt in; revocation denies Lockvyn cloud RLS access only.
begin;

create table lockvyn_private.cloud_device_registry (
    id uuid primary key default gen_random_uuid(),
    owner_id uuid not null references auth.users(id) on delete cascade,
    auth_session_id uuid not null,
    schema_version smallint not null check (schema_version = 1),
    platform text not null check (platform in ('windows','android','macos','ios','linux','other')),
    os_version text not null check (length(os_version) between 1 and 64 and btrim(os_version) <> '' and os_version !~ '[[:cntrl:]]'),
    app_version text not null check (length(app_version) between 1 and 64 and btrim(app_version) <> '' and app_version !~ '[[:cntrl:]]'),
    display_name text check (display_name is null or
      (length(display_name) between 1 and 80 and btrim(display_name) <> '' and display_name !~ '[[:cntrl:]]')),
    registered_at timestamptz not null default clock_timestamp(),
    last_sync_at timestamptz,
    revoked_at timestamptz,
    revoked_by_session_id uuid,
    constraint cloud_device_revocation_pair check (
      (revoked_at is null and revoked_by_session_id is null) or
      (revoked_at is not null and revoked_by_session_id is not null)
    ),
    constraint cloud_device_one_registration_per_session unique (owner_id, auth_session_id)
);
create index cloud_device_registry_owner_recent_idx
    on lockvyn_private.cloud_device_registry(owner_id, registered_at desc);

create table lockvyn_private.cloud_device_audit (
    id uuid primary key default gen_random_uuid(),
    owner_id uuid not null,
    device_id uuid not null,
    actor_session_id uuid not null,
    target_session_id uuid not null,
    action text not null check (action = 'cloud_session_revoked'),
    occurred_at timestamptz not null default clock_timestamp()
);
create index cloud_device_audit_owner_recent_idx
    on lockvyn_private.cloud_device_audit(owner_id, occurred_at desc);

alter table lockvyn_private.cloud_device_registry enable row level security;
alter table lockvyn_private.cloud_device_audit enable row level security;
revoke all on lockvyn_private.cloud_device_registry from public, anon, authenticated;
revoke all on lockvyn_private.cloud_device_audit from public, anon, authenticated;

comment on table lockvyn_private.cloud_device_registry is
  'Explicit-consent registry of signed Lockvyn Auth sessions. Self-reported metadata only; no hardware fingerprint, IP, vault data, or recovery material.';
comment on table lockvyn_private.cloud_device_audit is
  'Append-only-by-privilege audit of owner-authorized Lockvyn cloud-session revocations.';

create or replace function lockvyn_private.request_session_id()
returns uuid
language sql
stable
security definer
set search_path = ''
as $$
  select case
    when coalesce((select auth.jwt()->>'session_id'),'') ~*
      '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
    then ((select auth.jwt()->>'session_id')::uuid)
    else null::uuid
  end;
$$;
revoke all on function lockvyn_private.request_session_id() from public, anon, authenticated;

create or replace function lockvyn_private.cloud_device_session_allowed()
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
  select auth.uid() is not null
    and s.session_id is not null
    and not exists (
      select 1
      from lockvyn_private.cloud_device_registry d
      where d.owner_id = s.user_id
        and d.auth_session_id = s.session_id
        and d.revoked_at is not null
    )
  from (
    select (select auth.uid()) as user_id,
           (select lockvyn_private.request_session_id()) as session_id
  ) s;
$$;
revoke all on function lockvyn_private.cloud_device_session_allowed() from public, anon, authenticated;
grant execute on function lockvyn_private.cloud_device_session_allowed() to authenticated;
comment on function lockvyn_private.cloud_device_session_allowed() is
  'Restrictive RLS check: unregistered legacy sessions remain compatible; an explicitly revoked signed session loses Lockvyn cloud access.';

create or replace function public.lockvyn_register_cloud_device(
    p_consent boolean,
    p_platform text,
    p_os_version text,
    p_app_version text,
    p_display_name text default null,
    p_schema_version smallint default 1
)
returns uuid
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_owner uuid := (select auth.uid());
  v_session uuid := (select lockvyn_private.request_session_id());
  v_device_id uuid;
begin
  if p_consent is distinct from true then
    raise exception 'Explicit device inventory consent is required' using errcode = '42501';
  end if;
  if not (select lockvyn_private.cloud_assurance_allowed())
     or not (select lockvyn_private.cloud_device_session_allowed()) then
    raise exception 'Active cloud session assurance is required' using errcode = '42501';
  end if;
  if v_owner is null or v_session is null then
    raise exception 'A valid signed Auth session is required' using errcode = '42501';
  end if;
  if p_schema_version is distinct from 1 then
    raise exception 'Unsupported device registry schema version' using errcode = '22023';
  end if;
  if p_platform is null or p_platform not in ('windows','android','macos','ios','linux','other')
     or p_os_version is null or length(p_os_version) not between 1 and 64
     or btrim(p_os_version) = '' or p_os_version ~ '[[:cntrl:]]'
     or p_app_version is null or length(p_app_version) not between 1 and 64
     or btrim(p_app_version) = '' or p_app_version ~ '[[:cntrl:]]'
     or (p_display_name is not null and
       (length(p_display_name) not between 1 and 80 or btrim(p_display_name) = ''
        or p_display_name ~ '[[:cntrl:]]')) then
    raise exception 'Invalid device metadata' using errcode = '22023';
  end if;

  insert into lockvyn_private.cloud_device_registry as existing (
    owner_id, auth_session_id, schema_version, platform, os_version, app_version, display_name
  ) values (
    v_owner, v_session, p_schema_version, p_platform, p_os_version, p_app_version, p_display_name
  )
  on conflict (owner_id, auth_session_id) do update
    set schema_version = excluded.schema_version,
        platform = excluded.platform,
        os_version = excluded.os_version,
        app_version = excluded.app_version,
        display_name = excluded.display_name
    where existing.revoked_at is null
  returning existing.id into v_device_id;

  if v_device_id is null then
    raise exception 'A revoked session cannot register again; sign in to create a new Auth session'
      using errcode = '42501';
  end if;
  return v_device_id;
end;
$$;
revoke all on function public.lockvyn_register_cloud_device(boolean,text,text,text,text,smallint)
  from public, anon, authenticated;
grant execute on function public.lockvyn_register_cloud_device(boolean,text,text,text,text,smallint)
  to authenticated;
comment on function public.lockvyn_register_cloud_device(boolean,text,text,text,text,smallint) is
  'Registers only the caller’s signed Auth session after explicit opt-in; metadata is self-reported and versioned.';

create or replace function public.lockvyn_list_cloud_devices()
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
language sql
stable
security definer
set search_path = ''
as $$
  select d.id, d.display_name, d.platform, d.os_version, d.app_version,
         d.schema_version, d.registered_at, d.last_sync_at, d.revoked_at
  from lockvyn_private.cloud_device_registry d
  where d.owner_id = (select auth.uid())
    and (select lockvyn_private.cloud_assurance_allowed())
    and (select lockvyn_private.cloud_device_session_allowed())
  order by d.revoked_at nulls first, d.last_sync_at desc nulls last, d.registered_at desc;
$$;
revoke all on function public.lockvyn_list_cloud_devices() from public, anon, authenticated;
grant execute on function public.lockvyn_list_cloud_devices() to authenticated;
comment on function public.lockvyn_list_cloud_devices() is
  'Lists only the caller’s consented session metadata; never returns Auth session IDs or vault identifiers.';

create or replace function public.lockvyn_record_cloud_device_sync()
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_owner uuid := (select auth.uid());
  v_session uuid := (select lockvyn_private.request_session_id());
begin
  if not (select lockvyn_private.cloud_assurance_allowed())
     or not (select lockvyn_private.cloud_device_session_allowed())
     or v_owner is null or v_session is null then
    raise exception 'Active cloud session assurance is required' using errcode = '42501';
  end if;

  update lockvyn_private.cloud_device_registry
     set last_sync_at = clock_timestamp()
   where owner_id = v_owner
     and auth_session_id = v_session
     and revoked_at is null
     and (last_sync_at is null or last_sync_at < statement_timestamp() - interval '1 minute');

  if not found and not exists (
    select 1 from lockvyn_private.cloud_device_registry
    where owner_id = v_owner and auth_session_id = v_session and revoked_at is null
  ) then
    raise exception 'This Auth session has no active device registration' using errcode = '42501';
  end if;
  return true;
end;
$$;
revoke all on function public.lockvyn_record_cloud_device_sync() from public, anon, authenticated;
grant execute on function public.lockvyn_record_cloud_device_sync() to authenticated;
comment on function public.lockvyn_record_cloud_device_sync() is
  'Records a server timestamp after a successful sync, at most once per minute; accepts no client timestamp.';

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
  v_revoked_at timestamptz := clock_timestamp();
begin
  if not (select lockvyn_private.cloud_assurance_allowed())
     or not (select lockvyn_private.cloud_device_session_allowed())
     or coalesce((select auth.jwt()->>'aal'),'') <> 'aal2'
     or v_owner is null or v_actor_session is null then
    raise exception 'Verified AAL2 is required to revoke cloud device access' using errcode = '42501';
  end if;

  if (
    select count(*) >= 10
    from lockvyn_private.cloud_device_audit a
    where a.owner_id = v_owner
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

  if not found then
    return false;
  end if;
  if v_revoked_at is not null then
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
    owner_id, device_id, actor_session_id, target_session_id, action, occurred_at
  ) values (
    v_owner, v_target_id, v_actor_session, v_target_session, 'cloud_session_revoked', v_revoked_at
  );
  return true;
end;
$$;
revoke all on function public.lockvyn_revoke_cloud_device(uuid) from public, anon, authenticated;
grant execute on function public.lockvyn_revoke_cloud_device(uuid) to authenticated;
comment on function public.lockvyn_revoke_cloud_device(uuid) is
  'Requires the caller’s live AAL2 session; rate-limits to 10 revocations/minute; blocks the selected signed Auth session from Lockvyn cloud RLS paths and records an audit row.';

create policy "device_registry_revoked_session_denied" on public.opaque_device_snapshots as restrictive
  for all to authenticated
  using ((select lockvyn_private.cloud_device_session_allowed()))
  with check ((select lockvyn_private.cloud_device_session_allowed()));

create policy "device_registry_revoked_session_denied" on public.device_pairing_sessions as restrictive
  for all to authenticated
  using ((select lockvyn_private.cloud_device_session_allowed()))
  with check ((select lockvyn_private.cloud_device_session_allowed()));

commit;
