-- Optional 2FA must be enforced by the Data API, including for older clients.
-- The helper reads Auth's live, server-managed factors, never user_metadata.
begin;
create schema if not exists lockvyn_private;
revoke all on schema lockvyn_private from public, anon;
grant usage on schema lockvyn_private to authenticated;
create or replace function lockvyn_private.cloud_assurance_allowed()
returns boolean language sql stable security definer set search_path = '' as $$
    select (select auth.uid()) is not null
      and coalesce((select auth.jwt()->>'aal'),'aal1') in ('aal1','aal2')
      and (
        coalesce((select auth.jwt()->>'aal'),'aal1') = 'aal2'
        or not exists (
            select 1 from auth.mfa_factors
            where user_id = (select auth.uid()) and status = 'verified'
        )
      );
$$;
comment on function lockvyn_private.cloud_assurance_allowed() is
    'Current caller only: require aal2 whenever Auth has a verified factor. Private helper needs definer rights because clients cannot SELECT Auth factors.';
revoke all on function lockvyn_private.cloud_assurance_allowed() from public, anon, authenticated;
grant execute on function lockvyn_private.cloud_assurance_allowed() to authenticated;
drop policy if exists "cloud_mfa_required" on public.opaque_device_snapshots;
create policy "cloud_mfa_required" on public.opaque_device_snapshots
    as restrictive for all to authenticated
    using ((select lockvyn_private.cloud_assurance_allowed()))
    with check ((select lockvyn_private.cloud_assurance_allowed()));
commit;
