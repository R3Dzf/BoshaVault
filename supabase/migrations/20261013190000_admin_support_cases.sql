-- Phase 02: carefully scoped, auditable cloud account support.
-- Additive only; DOES NOT touch encrypted vault snapshots or master passphrases.
begin;
create table if not exists public.lockvyn_admin_support_cases (
  id uuid primary key default gen_random_uuid(),
  actor_id uuid not null,
  target_id uuid not null,
  kind text not null check (kind in (
    'suspend_24h', 'restore_login', 'session_review',
    'mfa_review', 'deletion_review'
  )),
  reason text not null check (reason in (
    'user_request', 'lost_device', 'suspected_compromise',
    'access_problem', 'policy_violation'
  )),
  status text not null default 'pending' check (status in (
    'pending','running','completed','failed','review_pending'
  )),
  created_at timestamptz not null default now(),
  expires_at timestamptz not null default (now() + interval '5 minutes'),
  processed_at timestamptz,
  constraint support_no_self_target check (actor_id <> target_id)
);
create index if not exists lockvyn_admin_support_target_time_idx
  on public.lockvyn_admin_support_cases(target_id, created_at desc);
create index if not exists lockvyn_admin_support_actor_time_idx
  on public.lockvyn_admin_support_cases(actor_id, created_at desc);
alter table public.lockvyn_admin_support_cases enable row level security;
alter table public.lockvyn_admin_support_cases force row level security;
revoke all on public.lockvyn_admin_support_cases from public,anon,authenticated;
grant select,insert,update on public.lockvyn_admin_support_cases to service_role;
comment on table public.lockvyn_admin_support_cases is
 'Admin action history. Contains only UUIDs, enumerated reasons, status and dates. Never passwords, access tokens, encrypted vault contents or typed secrets.';

-- Stage a single operation before explicit confirmation. No mutations to Auth
-- users occur at this step. All RPCs are restricted to the server service role.
create or replace function public.lockvyn_admin_support_prepare(
  p_actor uuid, p_target uuid, p_kind text, p_reason text
)
returns uuid language plpgsql security definer set search_path = ''
as $$
declare v_id uuid;
begin
  if p_actor is null or p_target is null or p_actor=p_target or
     not exists(select 1 from public.lockvyn_admins where user_id=p_actor) or
     exists(select 1 from public.lockvyn_admins where user_id=p_target)
  then
    raise exception 'Support action not allowed' using errcode='42501';
  end if;
  if p_kind is null or p_kind not in
     ('suspend_24h','restore_login','session_review','mfa_review','deletion_review')
    or p_reason is null or p_reason not in
     ('user_request','lost_device','suspected_compromise','access_problem','policy_violation')
  then
    raise exception 'Invalid support action' using errcode='22023';
  end if;
  if not exists(select 1 from auth.users where id=p_target)
  then
    raise exception 'Target account not found' using errcode='22023';
  end if;
  perform pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended(p_target::text,14031));
  if exists(select 1 from public.lockvyn_admin_support_cases
    where target_id=p_target and created_at > now()-interval '60 seconds')
  then
    raise exception 'Please wait before creating another support case' using errcode='P0001';
  end if;
  insert into public.lockvyn_admin_support_cases(actor_id,target_id,kind,reason)
  values(p_actor,p_target,p_kind,p_reason) returning id into v_id;
  return v_id;
end;
$$;
revoke all on function public.lockvyn_admin_support_prepare(uuid,uuid,text,text)
  from public,anon,authenticated;
grant execute on function public.lockvyn_admin_support_prepare(uuid,uuid,text,text)
  to service_role;

-- Atomically consume the pending case after checking the target's actual email.
-- A stale or replayed case cannot execute twice.
create or replace function public.lockvyn_admin_support_claim(
  p_actor uuid, p_case uuid, p_target uuid, p_kind text, p_email text
)
returns boolean language plpgsql security definer set search_path = ''
as $$
declare v_count integer;
begin
  if p_actor is null or p_case is null or p_target is null or p_email is null or
     p_kind is null or p_actor=p_target or
     not exists(select 1 from public.lockvyn_admins where user_id=p_actor) or
     exists(select 1 from public.lockvyn_admins where user_id=p_target)
  then
    raise exception 'Support action not allowed' using errcode='42501';
  end if;
  if not exists(select 1 from auth.users u
     where u.id=p_target and u.email is not null
       and pg_catalog.lower(u.email)=pg_catalog.lower(pg_catalog.btrim(p_email)))
  then
    raise exception 'Email confirmation does not match' using errcode='42501';
  end if;
  update public.lockvyn_admin_support_cases
  set status='running'
  where id=p_case and actor_id=p_actor and target_id=p_target
    and kind=p_kind and status='pending' and expires_at>now();
  get diagnostics v_count=row_count;
  return v_count=1;
end;
$$;
revoke all on function public.lockvyn_admin_support_claim(uuid,uuid,uuid,text,text)
  from public,anon,authenticated;
grant execute on function public.lockvyn_admin_support_claim(uuid,uuid,uuid,text,text)
  to service_role;

create or replace function public.lockvyn_admin_support_finish(
  p_actor uuid, p_case uuid, p_status text
)
returns boolean language plpgsql security definer set search_path = ''
as $$
declare v_count integer;
begin
  if p_actor is null or
     not exists(select 1 from public.lockvyn_admins where user_id=p_actor)
     or p_status not in ('completed','failed','review_pending')
  then
    raise exception 'Support action not allowed' using errcode='42501';
  end if;
  update public.lockvyn_admin_support_cases
     set status=p_status,processed_at=now()
   where id=p_case and actor_id=p_actor and status='running';
  get diagnostics v_count=row_count;
  return v_count=1;
end;
$$;
revoke all on function public.lockvyn_admin_support_finish(uuid,uuid,text)
  from public,anon,authenticated;
grant execute on function public.lockvyn_admin_support_finish(uuid,uuid,text)
  to service_role;

create or replace function public.lockvyn_admin_support_recent(p_actor uuid)
returns jsonb language plpgsql security definer set search_path = ''
as $$
declare v_rows jsonb;
begin
  if p_actor is null or
     not exists(select 1 from public.lockvyn_admins where user_id=p_actor)
  then
    raise exception 'Unauthorized' using errcode='42501';
  end if;
  select coalesce(pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
    'id',t.id,'kind',t.kind,'reason',t.reason,
    'state',t.status,'createdAt',t.created_at,'processedAt',t.processed_at,
    'targetId',t.target_id
  ) order by t.created_at desc),'[]'::jsonb)
    into v_rows from (
      select * from public.lockvyn_admin_support_cases
      order by created_at desc limit 25
    ) t;
  return v_rows;
end;
$$;
revoke all on function public.lockvyn_admin_support_recent(uuid)
  from public,anon,authenticated;
grant execute on function public.lockvyn_admin_support_recent(uuid)
  to service_role;
commit;
