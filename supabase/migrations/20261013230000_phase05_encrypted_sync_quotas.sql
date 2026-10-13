-- Phase 05: transactional encrypted-snapshot quotas, opt-in sync health, and
-- reversible cloud-only sync suspension. No ciphertext is decrypted or deleted.
begin;

alter table lockvyn_private.cloud_device_registry
  add column last_sync_attempt_at timestamptz,
  add column last_sync_outcome text not null default 'unknown'
    check (last_sync_outcome in ('unknown','success','failure')),
  add column last_sync_bytes bigint
    check (last_sync_bytes is null or last_sync_bytes between 0 and 67108864),
  add column last_sync_error_code text
    check (last_sync_error_code is null or last_sync_error_code in (
      'quota_exceeded','sync_suspended','network','auth','transport','unknown'
    ));
update lockvyn_private.cloud_device_registry
set last_sync_attempt_at=last_sync_at,
    last_sync_outcome='success'
where last_sync_at is not null;

alter table public.opaque_device_snapshots
  add column ciphertext_bytes bigint;
update public.opaque_device_snapshots
set ciphertext_bytes=octet_length(pg_catalog.decode(ciphertext_base64,'base64'));
alter table public.opaque_device_snapshots
  alter column ciphertext_bytes set not null;
alter table public.opaque_device_snapshots
  add constraint opaque_device_snapshots_ciphertext_bytes_check
  check (ciphertext_bytes between 1 and 2000029);

create table lockvyn_private.cloud_sync_quota_overrides (
  owner_id uuid primary key references auth.users(id) on delete cascade,
  quota_bytes bigint not null check (quota_bytes between 1 and 67108864),
  changed_at timestamptz not null default clock_timestamp()
);
alter table lockvyn_private.cloud_sync_quota_overrides enable row level security;
alter table lockvyn_private.cloud_sync_quota_overrides force row level security;
revoke all on lockvyn_private.cloud_sync_quota_overrides
  from public, anon, authenticated, service_role;
comment on table lockvyn_private.cloud_sync_quota_overrides is
  'Optional operator-set per-account ciphertext quota, capped at the 64 MiB default. No client/API role can read or change it.';

create table lockvyn_private.cloud_sync_suspensions (
  owner_id uuid primary key references auth.users(id) on delete cascade,
  suspended_at timestamptz not null default clock_timestamp(),
  suspended_by uuid not null,
  reason_code text not null check (reason_code in ('support_review','suspected_compromise'))
);
alter table lockvyn_private.cloud_sync_suspensions enable row level security;
alter table lockvyn_private.cloud_sync_suspensions force row level security;
revoke all on lockvyn_private.cloud_sync_suspensions
  from public, anon, authenticated, service_role;
comment on table lockvyn_private.cloud_sync_suspensions is
  'Cloud snapshot sync suspension only. It never deletes local vaults or encrypted cloud rows.';

create table lockvyn_private.cloud_sync_admin_audit (
  event_id uuid primary key default gen_random_uuid(),
  actor_id uuid not null,
  target_id uuid not null,
  actor_session_id uuid not null,
  event_type text not null check (event_type in ('sync_suspended','sync_resumed')),
  reason_code text not null check (reason_code in ('support_review','suspected_compromise')),
  occurred_at timestamptz not null default clock_timestamp()
);
create index lockvyn_cloud_sync_admin_audit_recent_idx
  on lockvyn_private.cloud_sync_admin_audit(occurred_at desc);
alter table lockvyn_private.cloud_sync_admin_audit enable row level security;
alter table lockvyn_private.cloud_sync_admin_audit force row level security;
revoke all on lockvyn_private.cloud_sync_admin_audit
  from public, anon, authenticated, service_role;
comment on table lockvyn_private.cloud_sync_admin_audit is
  'Append-only-by-privilege audit of cloud-only sync suspensions and resumptions.';

create or replace function lockvyn_private.cloud_sync_allowed()
returns boolean
language sql
stable
security definer
set search_path = ''
as $phase05$
  select (select auth.uid()) is not null
    and not exists (
      select 1 from lockvyn_private.cloud_sync_suspensions s
      where s.owner_id=(select auth.uid())
    );
$phase05$;
revoke all on function lockvyn_private.cloud_sync_allowed()
  from public, anon;
grant execute on function lockvyn_private.cloud_sync_allowed() to authenticated, service_role;
comment on function lockvyn_private.cloud_sync_allowed() is
  'Restrictive snapshot RLS gate. No suspension row means legacy clients retain cloud sync access.';

create policy lockvyn_cloud_sync_suspension_gate
  on public.opaque_device_snapshots as restrictive
  for all to authenticated
  using ((select lockvyn_private.cloud_sync_allowed()))
  with check ((select lockvyn_private.cloud_sync_allowed()));

create or replace function lockvyn_private.guard_cloud_snapshot_quota()
returns trigger
language plpgsql
security definer
set search_path = ''
as $phase05$
declare
  v_bytes bigint;
  v_used_bytes bigint;
  v_rows bigint;
  v_quota bigint;
begin
  if tg_op='UPDATE' and new.owner_id is distinct from old.owner_id then
    raise exception 'Snapshot ownership cannot change' using errcode='42501';
  end if;
  v_bytes := octet_length(pg_catalog.decode(new.ciphertext_base64,'base64'));
  if v_bytes < 1 or v_bytes > 2000029 then
    raise exception 'Encrypted snapshot size is invalid' using errcode='22023';
  end if;

  perform pg_catalog.pg_advisory_xact_lock(
    pg_catalog.hashtextextended(new.owner_id::text, 50205)
  );
  select coalesce(sum(s.ciphertext_bytes),0), count(*)
    into v_used_bytes, v_rows
  from public.opaque_device_snapshots s
  where s.owner_id=new.owner_id
    and not (s.vault_slot=new.vault_slot and s.device_slot=new.device_slot);

  select q.quota_bytes into v_quota
  from lockvyn_private.cloud_sync_quota_overrides q
  where q.owner_id=new.owner_id;
  if not found then v_quota := 67108864; end if;

  if v_rows >= 64 or v_used_bytes + v_bytes > v_quota then
    raise exception 'Cloud storage quota reached. Nothing was deleted; local vault data is unchanged.'
      using errcode='BV001';
  end if;
  new.ciphertext_bytes := v_bytes;
  return new;
end;
$phase05$;
revoke all on function lockvyn_private.guard_cloud_snapshot_quota()
  from public, anon, authenticated, service_role;
create trigger lockvyn_guard_cloud_snapshot_quota
  before insert or update of owner_id, vault_slot, device_slot, ciphertext_base64
  on public.opaque_device_snapshots
  for each row execute function lockvyn_private.guard_cloud_snapshot_quota();

create or replace function lockvyn_private.cloud_sync_quota_bytes(p_owner uuid)
returns bigint
language sql
stable
security definer
set search_path = ''
as $phase05$
  select coalesce(
    (select q.quota_bytes from lockvyn_private.cloud_sync_quota_overrides q
      where q.owner_id=p_owner),
    67108864::bigint
  );
$phase05$;
revoke all on function lockvyn_private.cloud_sync_quota_bytes(uuid)
  from public, anon, authenticated, service_role;

create or replace function public.lockvyn_cloud_sync_status()
returns jsonb
language plpgsql
stable
security definer
set search_path = ''
as $phase05$
declare
  v_owner uuid := (select auth.uid());
  v_session uuid := (select lockvyn_private.request_session_id());
  v_bytes bigint;
  v_count bigint;
  v_quota bigint;
  v_suspended boolean;
  v_suspended_at timestamptz;
begin
  if v_owner is null or v_session is null
     or not (select lockvyn_private.cloud_assurance_allowed())
     or not (select lockvyn_private.cloud_device_session_allowed()) then
    raise exception 'A verified active cloud session is required' using errcode='42501';
  end if;
  select exists(select 1 from lockvyn_private.cloud_sync_suspensions s
      where s.owner_id=v_owner),
    (select s.suspended_at from lockvyn_private.cloud_sync_suspensions s
      where s.owner_id=v_owner)
    into v_suspended, v_suspended_at;
  select count(*),coalesce(sum(s.ciphertext_bytes),0)
    into v_count,v_bytes
  from public.opaque_device_snapshots s
  where s.owner_id=v_owner;
  v_quota := lockvyn_private.cloud_sync_quota_bytes(v_owner);
  return jsonb_build_object(
    'suspended',coalesce(v_suspended,false),
    'suspendedAt',v_suspended_at,
    'snapshotCount',v_count,
    'ciphertextBytes',v_bytes,
    'quotaBytes',v_quota,
    'remainingBytes',greatest(0,v_quota-v_bytes),
    'maxSnapshots',64
  );
end;
$phase05$;
revoke all on function public.lockvyn_cloud_sync_status() from public, anon;
grant execute on function public.lockvyn_cloud_sync_status() to authenticated;
comment on function public.lockvyn_cloud_sync_status() is
  'Authenticated owner-only sync preflight and ciphertext usage; returns metadata and suspension state, never content.';

create or replace function public.lockvyn_record_cloud_sync_outcome(
  p_outcome text,
  p_ciphertext_bytes bigint default null,
  p_error_code text default null
)
returns boolean
language plpgsql
security definer
set search_path = ''
as $phase05$
declare
  v_owner uuid := (select auth.uid());
  v_session uuid := (select lockvyn_private.request_session_id());
begin
  if v_owner is null or v_session is null
     or not (select lockvyn_private.cloud_assurance_allowed())
     or not (select lockvyn_private.cloud_device_session_allowed()) then
    raise exception 'A verified active cloud session is required' using errcode='42501';
  end if;
  if p_outcome not in ('success','failure')
     or p_ciphertext_bytes is not null and p_ciphertext_bytes not between 0 and 67108864
     or p_error_code is not null and p_error_code not in (
       'quota_exceeded','sync_suspended','network','auth','transport','unknown'
     )
     or p_outcome='success' and p_error_code is not null
     or p_outcome='failure' and p_error_code is null then
    raise exception 'Invalid bounded sync result' using errcode='22023';
  end if;
  if p_outcome='success'
     and not (select lockvyn_private.cloud_sync_allowed()) then
    raise exception 'Cloud sync is paused by Lockvyn support; local vault and encrypted cloud snapshots are preserved.'
      using errcode='BV002';
  end if;

  perform pg_catalog.pg_advisory_xact_lock(
    pg_catalog.hashtextextended(v_owner::text||':'||v_session::text,50207)
  );
  update lockvyn_private.cloud_device_registry
     set last_sync_attempt_at=clock_timestamp(),
         last_sync_outcome=p_outcome,
         last_sync_bytes=p_ciphertext_bytes,
         last_sync_error_code=p_error_code,
         last_sync_at=case when p_outcome='success' then clock_timestamp() else last_sync_at end
   where owner_id=v_owner
     and auth_session_id=v_session
     and revoked_at is null
     and (last_sync_attempt_at is null
          or last_sync_attempt_at<statement_timestamp()-interval '1 minute');
  if not found and not exists(
    select 1 from lockvyn_private.cloud_device_registry
    where owner_id=v_owner and auth_session_id=v_session and revoked_at is null
  ) then
    raise exception 'This Auth session has no active opted-in sync record' using errcode='42501';
  end if;
  return true;
end;
$phase05$;
revoke all on function public.lockvyn_record_cloud_sync_outcome(text,bigint,text)
  from public, anon;
grant execute on function public.lockvyn_record_cloud_sync_outcome(text,bigint,text)
  to authenticated;

create or replace function public.lockvyn_record_cloud_device_sync()
returns boolean
language plpgsql
security definer
set search_path = ''
as $phase05$
begin
  return public.lockvyn_record_cloud_sync_outcome('success',null,null);
end;
$phase05$;
revoke all on function public.lockvyn_record_cloud_device_sync()
  from public, anon, authenticated;
grant execute on function public.lockvyn_record_cloud_device_sync() to authenticated;

create or replace function public.lockvyn_admin_cloud_sync_overview(
  p_actor uuid,
  p_actor_session uuid,
  p_actor_aal text,
  p_target uuid,
  p_platform text default null,
  p_outcome text default null,
  p_since_days integer default 30,
  p_device_query text default null
)
returns jsonb
language plpgsql
stable
security definer
set search_path = ''
as $phase05$
declare
  v_devices jsonb;
  v_suspended boolean;
  v_suspended_at timestamptz;
  v_count bigint;
  v_bytes bigint;
  v_quota bigint;
begin
  if p_actor is null or p_actor_session is null or p_target is null
     or p_actor=p_target or p_actor_aal is distinct from 'aal2'
     or not exists(select 1 from public.lockvyn_admins a where a.user_id=p_actor)
     or exists(select 1 from public.lockvyn_admins a where a.user_id=p_target)
     or not public.lockvyn_auth_session_is_active(p_actor,p_actor_session) then
    raise exception 'A live AAL2 administrator session is required' using errcode='42501';
  end if;
  if p_platform is not null and p_platform not in ('windows','android','macos','ios','linux','other') then
    raise exception 'Invalid platform filter' using errcode='22023';
  end if;
  if p_outcome is not null and p_outcome not in ('all','success','failure','unknown') then
    raise exception 'Invalid sync result filter' using errcode='22023';
  end if;
  if p_since_days is not null and p_since_days not between 1 and 365 then
    raise exception 'Invalid sync time filter' using errcode='22023';
  end if;
  if p_device_query is not null and length(p_device_query)>80 then
    raise exception 'Invalid device filter' using errcode='22023';
  end if;
  if not exists(select 1 from auth.users u where u.id=p_target) then
    raise exception 'Target account not found' using errcode='22023';
  end if;

  select coalesce(jsonb_agg(jsonb_build_object(
      'device_id',d.id,
      'display_name',d.display_name,
      'platform',d.platform,
      'os_version',d.os_version,
      'app_version',d.app_version,
      'schema_version',d.schema_version,
      'registered_at',d.registered_at,
      'last_sync_at',d.last_sync_at,
      'last_sync_attempt_at',d.last_sync_attempt_at,
      'last_sync_outcome',d.last_sync_outcome,
      'last_sync_bytes',d.last_sync_bytes,
      'last_sync_error_code',d.last_sync_error_code,
      'revoked_at',d.revoked_at
    ) order by d.last_sync_attempt_at desc nulls last,d.registered_at desc),'[]'::jsonb)
    into v_devices
  from (
    select r.* from lockvyn_private.cloud_device_registry r
    where r.owner_id=p_target
      and (p_platform is null or r.platform=p_platform)
      and (p_outcome is null or p_outcome='all' or r.last_sync_outcome=p_outcome)
      and (p_since_days is null or r.last_sync_attempt_at is null or
           r.last_sync_attempt_at>statement_timestamp()-make_interval(days=>p_since_days))
      and (p_device_query is null or coalesce(r.display_name,'') ilike '%'||p_device_query||'%'
           or r.platform ilike '%'||p_device_query||'%'
           or r.app_version ilike '%'||p_device_query||'%')
    order by r.last_sync_attempt_at desc nulls last,r.registered_at desc
    limit 100
  ) d;

  select exists(select 1 from lockvyn_private.cloud_sync_suspensions s
      where s.owner_id=p_target),
    (select s.suspended_at from lockvyn_private.cloud_sync_suspensions s
      where s.owner_id=p_target)
    into v_suspended,v_suspended_at;
  select count(*),coalesce(sum(s.ciphertext_bytes),0)
    into v_count,v_bytes
  from public.opaque_device_snapshots s where s.owner_id=p_target;
  v_quota:=lockvyn_private.cloud_sync_quota_bytes(p_target);

  return jsonb_build_object(
    'devices',v_devices,
    'suspended',coalesce(v_suspended,false),
    'suspendedAt',v_suspended_at,
    'snapshotCount',v_count,
    'ciphertextBytes',v_bytes,
    'quotaBytes',v_quota,
    'remainingBytes',greatest(0,v_quota-v_bytes),
    'maxSnapshots',64
  );
end;
$phase05$;
revoke all on function public.lockvyn_admin_cloud_sync_overview(uuid,uuid,text,uuid,text,text,integer,text)
  from public, anon, authenticated;
grant execute on function public.lockvyn_admin_cloud_sync_overview(uuid,uuid,text,uuid,text,text,integer,text)
  to service_role;

create or replace function public.lockvyn_admin_set_cloud_sync_suspension(
  p_actor uuid,
  p_actor_session uuid,
  p_actor_aal text,
  p_fresh_totp boolean,
  p_target uuid,
  p_suspend boolean,
  p_reason_code text,
  p_confirm_email text
)
returns boolean
language plpgsql
security definer
set search_path = ''
as $phase05$
declare
  v_email text;
  v_changed integer;
begin
  if p_actor is null or p_actor_session is null or p_target is null
     or p_actor=p_target or p_suspend is null
     or p_actor_aal is distinct from 'aal2' or p_fresh_totp is distinct from true
     or not exists(select 1 from public.lockvyn_admins a where a.user_id=p_actor)
     or exists(select 1 from public.lockvyn_admins a where a.user_id=p_target)
     or not public.lockvyn_auth_session_is_active(p_actor,p_actor_session) then
    raise exception 'A fresh verified administrator session is required' using errcode='42501';
  end if;
  if p_reason_code is null or p_reason_code not in ('support_review','suspected_compromise') then
    raise exception 'Invalid cloud sync suspension reason' using errcode='22023';
  end if;
  select u.email into v_email from auth.users u where u.id=p_target;
  if v_email is null then
    raise exception 'Target account not found' using errcode='22023';
  end if;
  if p_confirm_email is null
     or pg_catalog.lower(v_email)<>pg_catalog.lower(pg_catalog.btrim(p_confirm_email)) then
    raise exception 'Target email confirmation does not match' using errcode='42501';
  end if;

  perform pg_catalog.pg_advisory_xact_lock(
    pg_catalog.hashtextextended(p_actor::text||':'||p_actor_session::text,50206)
  );
  if (select count(*) from lockvyn_private.cloud_sync_admin_audit a
      where a.actor_id=p_actor and a.actor_session_id=p_actor_session
        and a.occurred_at>statement_timestamp()-interval '1 minute')>=5 then
    raise exception 'Cloud sync control rate limit reached' using errcode='54000';
  end if;

  if p_suspend then
    insert into lockvyn_private.cloud_sync_suspensions(owner_id,suspended_by,reason_code)
    values(p_target,p_actor,p_reason_code)
    on conflict(owner_id) do nothing;
    get diagnostics v_changed=row_count;
  else
    delete from lockvyn_private.cloud_sync_suspensions where owner_id=p_target;
    get diagnostics v_changed=row_count;
  end if;
  if v_changed=0 then return false; end if;

  insert into lockvyn_private.cloud_sync_admin_audit(
    actor_id,target_id,actor_session_id,event_type,reason_code
  ) values (
    p_actor,p_target,p_actor_session,
    case when p_suspend then 'sync_suspended' else 'sync_resumed' end,
    p_reason_code
  );
  return true;
end;
$phase05$;
revoke all on function public.lockvyn_admin_set_cloud_sync_suspension(uuid,uuid,text,boolean,uuid,boolean,text,text)
  from public, anon, authenticated;
grant execute on function public.lockvyn_admin_set_cloud_sync_suspension(uuid,uuid,text,boolean,uuid,boolean,text,text)
  to service_role;

-- Mirror cloud-only sync controls into the existing 90-day Security Center feed.
alter table lockvyn_private.lockvyn_admin_security_events
  drop constraint lockvyn_admin_security_events_event_type_check,
  drop constraint lockvyn_admin_security_events_detail_code_check;
alter table lockvyn_private.lockvyn_admin_security_events
  add constraint lockvyn_admin_security_events_event_type_check check (event_type in (
    'account_recovery_requested','cloud_device_unregistered','cloud_session_registered',
    'cloud_session_revoked','support_case_opened','support_action_completed',
    'support_action_failed','support_review_pending','admin_step_up_denied',
    'admin_allowlist_denied','admin_inactive_session','cloud_sync_suspended',
    'cloud_sync_resumed'
  )),
  add constraint lockvyn_admin_security_events_detail_code_check
    check (detail_code is null or detail_code in (
      'recovery_requested','cloud_device_unregistered','suspend_24h','restore_login',
      'session_review','mfa_review','deletion_review','support_review',
      'suspected_compromise'
    ));

create or replace function lockvyn_private.capture_cloud_sync_admin_event()
returns trigger
language plpgsql
security definer
set search_path = ''
as $phase05_event$
begin
  perform lockvyn_private.write_admin_security_event(
    new.actor_id,new.target_id,
    case when new.event_type='sync_suspended'
      then 'cloud_sync_suspended' else 'cloud_sync_resumed' end,
    new.reason_code
  );
  return new;
end;
$phase05_event$;
revoke all on function lockvyn_private.capture_cloud_sync_admin_event()
  from public, anon, authenticated, service_role;
create trigger lockvyn_capture_cloud_sync_security_event
  after insert on lockvyn_private.cloud_sync_admin_audit
  for each row execute function lockvyn_private.capture_cloud_sync_admin_event();

commit;
