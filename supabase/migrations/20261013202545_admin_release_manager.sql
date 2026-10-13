-- Phase 06: release policy and minimal, metadata-only administration.
-- Contains no user, device, vault, password, or encryption-key data.
begin;

create schema if not exists lockvyn_private;
revoke all on schema lockvyn_private from public, anon, authenticated;

create table lockvyn_private.release_policies (
  platform text primary key check (platform in ('windows','android')),
  candidate_tag text not null,
  minimum_version text not null,
  rollout_percent integer not null default 100
    check (rollout_percent between 0 and 100),
  updated_at timestamptz not null default now(),
  updated_by uuid,
  constraint release_candidate_tag_valid check (
    (platform='windows' and candidate_tag ~ '^v(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$')
    or
    (platform='android' and candidate_tag ~ '^android-v(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$')
  ),
  constraint release_minimum_version_valid check (
    (platform='windows' and minimum_version ~ '^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$')
    or
    (platform='android' and minimum_version ~ '^(0|[1-9][0-9]{0,9})$')
  )
);
revoke all on lockvyn_private.release_policies from public, anon, authenticated, service_role;

create table lockvyn_private.release_policy_audit (
  id bigint generated always as identity primary key,
  actor_id uuid not null,
  actor_session_id uuid not null,
  platform text not null check (platform in ('windows','android')),
  previous_state jsonb,
  new_state jsonb not null,
  reason_code text not null check (reason_code in (
    'planned_rollout','rollback','minimum_version','incident_response'
  )),
  created_at timestamptz not null default now()
);
create index release_policy_audit_actor_time_idx
  on lockvyn_private.release_policy_audit(actor_id,created_at desc);
revoke all on lockvyn_private.release_policy_audit from public, anon, authenticated, service_role;

insert into lockvyn_private.release_policies(platform,candidate_tag,minimum_version,rollout_percent)
values ('windows','v1.21.0','0.0.0',100),('android','android-v1.9.0','0',100)
on conflict(platform) do nothing;

create or replace function public.lockvyn_release_policy_public(p_platform text)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare v_policy record;
begin
  if p_platform not in ('windows','android') then
    raise exception 'Invalid platform' using errcode='22023';
  end if;
  select platform,candidate_tag,minimum_version,rollout_percent,updated_at
    into v_policy
    from lockvyn_private.release_policies where platform=p_platform;
  if not found then
    raise exception 'Release policy unavailable' using errcode='P0002';
  end if;
  return pg_catalog.jsonb_build_object(
    'platform',v_policy.platform,
    'candidateTag',v_policy.candidate_tag,
    'minimumVersion',v_policy.minimum_version,
    'rolloutPercent',v_policy.rollout_percent,
    'updatedAt',v_policy.updated_at
  );
end;
$$;
revoke all on function public.lockvyn_release_policy_public(text)
  from public,anon,authenticated;
grant execute on function public.lockvyn_release_policy_public(text) to service_role;

create or replace function public.lockvyn_admin_set_release_policy(
  p_actor uuid,
  p_actor_session uuid,
  p_actor_aal text,
  p_fresh_totp boolean,
  p_platform text,
  p_candidate_tag text,
  p_minimum_version text,
  p_rollout_percent integer,
  p_reason_code text
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_previous jsonb;
  v_new jsonb;
  v_count integer;
begin
  if coalesce(auth.role(),'') <> 'service_role' or
     p_actor is null or p_actor_session is null or
     p_actor_aal <> 'aal2' or p_fresh_totp is distinct from true or
     not exists(select 1 from public.lockvyn_admins where user_id=p_actor) or
     not public.lockvyn_auth_session_is_active(p_actor,p_actor_session)
  then
    raise exception 'A fresh active administrator session is required'
      using errcode='42501';
  end if;
  if p_platform is null or p_platform not in ('windows','android') or
     p_reason_code is null or p_candidate_tag is null or p_minimum_version is null or
     p_rollout_percent is null or p_rollout_percent not between 0 and 100 or
     p_reason_code not in ('planned_rollout','rollback','minimum_version','incident_response')
  then
    raise exception 'Invalid release policy' using errcode='22023';
  end if;
  if p_platform='windows' then
    if p_candidate_tag !~ '^v(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$' or
       p_minimum_version !~ '^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$' or
       string_to_array(p_minimum_version,'.')::integer[] >
         string_to_array(substr(p_candidate_tag,2),'.')::integer[]
    then
      raise exception 'Invalid Windows candidate or minimum version'
        using errcode='22023';
    end if;
  else
    if p_candidate_tag !~ '^android-v(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$' or
       p_minimum_version !~ '^(0|[1-9][0-9]{0,9})$'
    then
      raise exception 'Invalid Android candidate or minimum version'
        using errcode='22023';
    end if;
  end if;

  perform pg_catalog.pg_advisory_xact_lock(
    pg_catalog.hashtextextended(p_actor::text,728061)
  );
  select count(*) into v_count
  from lockvyn_private.release_policy_audit
  where actor_id=p_actor and created_at>now()-interval '60 seconds';
  if v_count >= 5 then
    raise exception 'Release policy rate limit reached' using errcode='54000';
  end if;

  select pg_catalog.jsonb_build_object(
    'platform',platform,'candidateTag',candidate_tag,
    'minimumVersion',minimum_version,'rolloutPercent',rollout_percent,
    'updatedAt',updated_at
  ) into v_previous
  from lockvyn_private.release_policies where platform=p_platform;

  insert into lockvyn_private.release_policies
    (platform,candidate_tag,minimum_version,rollout_percent,updated_at,updated_by)
  values
    (p_platform,p_candidate_tag,p_minimum_version,p_rollout_percent,now(),p_actor)
  on conflict(platform) do update set
    candidate_tag=excluded.candidate_tag,
    minimum_version=excluded.minimum_version,
    rollout_percent=excluded.rollout_percent,
    updated_at=excluded.updated_at,
    updated_by=excluded.updated_by;

  v_new:=public.lockvyn_release_policy_public(p_platform);
  insert into lockvyn_private.release_policy_audit
    (actor_id,actor_session_id,platform,previous_state,new_state,reason_code)
  values (p_actor,p_actor_session,p_platform,v_previous,v_new,p_reason_code);
  return v_new;
end;
$$;
revoke all on function public.lockvyn_admin_set_release_policy(
  uuid,uuid,text,boolean,text,text,text,integer,text
) from public,anon,authenticated;
grant execute on function public.lockvyn_admin_set_release_policy(
  uuid,uuid,text,boolean,text,text,text,integer,text
) to service_role;

comment on table lockvyn_private.release_policies is
  'Sanitized platform release controls only; no user or device identifiers.';
comment on table lockvyn_private.release_policy_audit is
  'Append-only admin release policy changes. Stores actor/session IDs and policy values, never vault contents or credentials.';
commit;
