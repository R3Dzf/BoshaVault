-- Let a user withdraw optional inventory consent without signing out of cloud sync.
begin;

alter table lockvyn_private.cloud_device_audit
  drop constraint if exists cloud_device_audit_action_check;
alter table lockvyn_private.cloud_device_audit
  add constraint cloud_device_audit_action_check
  check (action in ('cloud_session_revoked','cloud_session_unregistered'));

create or replace function public.lockvyn_unregister_cloud_device()
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
    raise exception 'A verified active cloud session is required to withdraw device inventory consent'
      using errcode = '42501';
  end if;

  delete from lockvyn_private.cloud_device_registry
   where owner_id = v_owner
     and auth_session_id = v_session
     and revoked_at is null;
  return true;
end;
$$;
revoke all on function public.lockvyn_unregister_cloud_device() from public, anon, authenticated;
grant execute on function public.lockvyn_unregister_cloud_device() to authenticated;
comment on function public.lockvyn_unregister_cloud_device() is
  'Withdraws optional per-session inventory consent and deletes its metadata; cloud snapshots and sync access remain available.';

commit;
