-- Ephemeral signaling only. Absolutely no vault recovery key, Auth session, or
-- plaintext vault content may be stored in this table.
begin;

create table public.device_pairing_sessions (
  session_id uuid primary key,
  owner_id uuid not null references auth.users(id) on delete cascade,
  initiator_public_key text not null check (length(initiator_public_key) between 110 and 180),
  joiner_public_key text check (joiner_public_key is null or length(joiner_public_key) between 110 and 180),
  sealed_pairing text check (sealed_pairing is null or length(sealed_pairing) between 150 and 4096),
  state text not null default 'waiting' check (state in ('waiting','requested','approved','completed','cancelled')),
  created_at timestamptz not null default now(),
  expires_at timestamptz not null default (now() + interval '2 minutes'),
  constraint pairing_consistent_state check (
    (state='waiting' and joiner_public_key is null and sealed_pairing is null) or
    (state='requested' and joiner_public_key is not null and sealed_pairing is null) or
    (state in ('approved','completed') and joiner_public_key is not null and sealed_pairing is not null) or
    (state='cancelled')
  ),
  constraint pairing_max_lifetime check (expires_at <= created_at + interval '2 minutes' and expires_at > created_at)
);
create index on public.device_pairing_sessions(owner_id, expires_at);

-- Immutable identity/ephemeral initiating key; terminal states cannot be reopened,
-- and one recipient cannot substitute its key after requesting approval.
create function public.device_pairing_guard()
returns trigger language plpgsql security invoker set search_path = '' as $$
begin
  if old.session_id is distinct from new.session_id
    or old.owner_id is distinct from new.owner_id
    or old.initiator_public_key is distinct from new.initiator_public_key
    or old.created_at is distinct from new.created_at
    or old.expires_at is distinct from new.expires_at
    or old.expires_at <= now() then
      raise exception 'Immutable or expired pairing session';
  end if;
  if not (
      (old.state='waiting' and new.state='requested'
       and old.joiner_public_key is null and new.joiner_public_key is not null and new.sealed_pairing is null)
   or (old.state='requested' and new.state='approved'
       and new.joiner_public_key=old.joiner_public_key and new.sealed_pairing is not null)
   or (old.state='approved' and new.state='completed'
       and new.joiner_public_key=old.joiner_public_key and new.sealed_pairing=old.sealed_pairing)
   or (old.state in ('waiting','requested','approved') and new.state='cancelled')
  ) then raise exception 'Invalid pairing transition';
  end if;
  return new;
end;
$$;
revoke all on function public.device_pairing_guard() from public, anon, authenticated;
create trigger pairing_guard before update on public.device_pairing_sessions
for each row execute function public.device_pairing_guard();

alter table public.device_pairing_sessions enable row level security;
revoke all on public.device_pairing_sessions from public, anon, authenticated;
grant select, insert, delete on public.device_pairing_sessions to authenticated;
grant update (joiner_public_key, sealed_pairing, state) on public.device_pairing_sessions to authenticated;

create policy "pairing_owner_read" on public.device_pairing_sessions
  for select to authenticated using (owner_id = (select auth.uid()) and expires_at > now());
create policy "pairing_owner_insert" on public.device_pairing_sessions
  for insert to authenticated with check (owner_id = (select auth.uid()) and
    state = 'waiting' and expires_at > now() and created_at >= now() - interval '5 seconds'
    and created_at <= now() + interval '5 seconds');
create policy "pairing_owner_update" on public.device_pairing_sessions
  for update to authenticated
  using (owner_id = (select auth.uid()) and expires_at > now())
  with check (owner_id = (select auth.uid()) and expires_at > now());
create policy "pairing_owner_delete" on public.device_pairing_sessions
  for delete to authenticated using (owner_id = (select auth.uid()));
create policy "pairing_verified_mfa" on public.device_pairing_sessions as restrictive
  for all to authenticated
  using ((select lockvyn_private.cloud_assurance_allowed()))
  with check ((select lockvyn_private.cloud_assurance_allowed()));

comment on table public.device_pairing_sessions is
  'Temporary owner-isolated ECDH signaling for trusted device approval; only public keys and encrypted pairing payload, no vault secrets in plaintext.';
commit;
