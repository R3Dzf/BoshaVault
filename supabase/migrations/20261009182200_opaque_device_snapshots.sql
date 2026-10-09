-- BoshaVault optional encrypted cloud sync. No plaintext vault data is stored.
-- New dedicated Supabase project recommended; do NOT apply to an unrelated app.
-- Privacy note: Auth account identity, opaque slot IDs, ciphertext length and timestamps
-- remain observable by Supabase operators. A secret project name is not a security control.
begin;
create table if not exists public.opaque_device_snapshots (
    owner_id uuid not null references auth.users(id) on delete cascade,
    vault_slot uuid not null,
    device_slot uuid not null,
    ciphertext_base64 text not null,
    ciphertext_sha256 char(64) not null,
    updated_at timestamptz not null default now(),
    constraint opaque_device_snapshots_pk primary key(owner_id,vault_slot,device_slot),
    constraint opaque_device_snapshots_body check (
        octet_length(ciphertext_base64) between 136 and 2800000 and
        ciphertext_base64 ~ '^[A-Za-z0-9+/]*={0,2}$'
    ),
    constraint opaque_device_snapshots_sha check (
        ciphertext_sha256 ~ '^[0-9a-f]{64}$'
    )
);
comment on table public.opaque_device_snapshots is
    'Optional E2EE client-produced vault snapshots only; no password, URL, username, or recovery keys.';
create index if not exists opaque_device_snapshots_recent_idx
    on public.opaque_device_snapshots(owner_id,vault_slot,updated_at desc);
alter table public.opaque_device_snapshots enable row level security;
alter table public.opaque_device_snapshots force row level security;
revoke all on public.opaque_device_snapshots from public, anon;
grant select,insert,update,delete on public.opaque_device_snapshots to authenticated;
drop policy if exists "owner_read" on public.opaque_device_snapshots;
drop policy if exists "owner_insert" on public.opaque_device_snapshots;
drop policy if exists "owner_update" on public.opaque_device_snapshots;
drop policy if exists "owner_delete" on public.opaque_device_snapshots;
create policy "owner_read" on public.opaque_device_snapshots
    for select to authenticated using (owner_id=(select auth.uid()));
create policy "owner_insert" on public.opaque_device_snapshots
    for insert to authenticated with check (owner_id=(select auth.uid()));
create policy "owner_update" on public.opaque_device_snapshots
    for update to authenticated using (owner_id=(select auth.uid()))
    with check (owner_id=(select auth.uid()));
create policy "owner_delete" on public.opaque_device_snapshots
    for delete to authenticated using (owner_id=(select auth.uid()));
-- Never trust updated_at supplied by clients.
create or replace function public.opaque_snapshot_set_timestamp()
returns trigger language plpgsql set search_path = '' as $$
begin
    new.updated_at = now();
    return new;
end;
$$;
revoke all on function public.opaque_snapshot_set_timestamp() from public, anon;
drop trigger if exists opaque_snapshot_timestamp on public.opaque_device_snapshots;
create trigger opaque_snapshot_timestamp before insert or update
    on public.opaque_device_snapshots for each row
    execute function public.opaque_snapshot_set_timestamp();
commit;
