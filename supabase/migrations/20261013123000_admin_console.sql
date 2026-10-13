-- Private account-support authority. Never grant access to vault encryption keys.
begin;
create table if not exists public.lockvyn_admins (
  user_id uuid primary key references auth.users(id) on delete cascade,
  added_at timestamptz not null default now()
);
alter table public.lockvyn_admins enable row level security;
alter table public.lockvyn_admins force row level security;
revoke all on public.lockvyn_admins from public, anon, authenticated;
grant select on public.lockvyn_admins to service_role;
comment on table public.lockvyn_admins is
  'Explicit owner-managed allowlist for server-side admin support. Empty by default; no client access.';

create table if not exists public.lockvyn_admin_audit (
  id bigint generated always as identity primary key,
  actor_id uuid not null references auth.users(id) on delete cascade,
  target_id uuid not null references auth.users(id) on delete cascade,
  action text not null check (action in ('recovery_requested')),
  created_at timestamptz not null default now()
);
create index if not exists lockvyn_admin_audit_target_idx
  on public.lockvyn_admin_audit(target_id, action, created_at desc);
alter table public.lockvyn_admin_audit enable row level security;
alter table public.lockvyn_admin_audit force row level security;
revoke all on public.lockvyn_admin_audit from public, anon, authenticated;
grant select, insert on public.lockvyn_admin_audit to service_role;
grant usage, select on sequence public.lockvyn_admin_audit_id_seq to service_role;
comment on table public.lockvyn_admin_audit is
  'Metadata-only account recovery audit. No secrets or recovery URLs.';

create or replace function public.lockvyn_claim_recovery(p_actor uuid, p_target uuid)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
begin
  -- This RPC is callable only by a service-role Edge Function after verified JWT/MFA.
  if p_actor is null or p_target is null or not exists (
    select 1 from public.lockvyn_admins where user_id = p_actor
  ) then
    raise exception 'Unauthorized support action';
  end if;
  perform pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended(p_target::text, 31415));
  if exists (
    select 1 from public.lockvyn_admin_audit
    where target_id = p_target and action = 'recovery_requested'
      and created_at > now() - interval '60 minutes'
  ) then
    return false;
  end if;
  insert into public.lockvyn_admin_audit (actor_id, target_id, action)
    values (p_actor, p_target, 'recovery_requested');
  return true;
end;
$$;
revoke all on function public.lockvyn_claim_recovery(uuid,uuid) from public, anon, authenticated;
grant execute on function public.lockvyn_claim_recovery(uuid,uuid) to service_role;
commit;

-- Administrator onboarding (run separately after verifying the owner's account):
-- insert into public.lockvyn_admins(user_id)
-- select id from auth.users where email = 'YOUR_VERIFIED_OWNER_EMAIL'
--   and email_confirmed_at is not null;
