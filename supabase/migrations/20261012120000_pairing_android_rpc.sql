-- Android's platform HttpURLConnection cannot issue PATCH reliably.
-- These narrowly scoped POST RPCs preserve owner/MFA RLS and the immutable
-- transition guard; they do not run as SECURITY DEFINER.
begin;

create function public.lockvyn_pairing_request(p_session_id uuid, p_joiner_public_key text)
returns boolean language plpgsql security invoker set search_path = '' as $$
declare n integer;
begin
  if p_session_id is null or p_joiner_public_key is null or
     length(p_joiner_public_key) not between 110 and 180 then return false; end if;
  update public.device_pairing_sessions
    set state = 'requested', joiner_public_key = p_joiner_public_key
    where session_id = p_session_id and owner_id = (select auth.uid())
      and state = 'waiting' and expires_at > now();
  get diagnostics n = row_count;
  return n = 1;
end; $$;

create function public.lockvyn_pairing_complete(p_session_id uuid, p_joiner_public_key text)
returns boolean language plpgsql security invoker set search_path = '' as $$
declare n integer;
begin
  if p_session_id is null or p_joiner_public_key is null then return false; end if;
  update public.device_pairing_sessions
    set state = 'completed'
    where session_id = p_session_id and owner_id = (select auth.uid())
      and state = 'approved' and joiner_public_key = p_joiner_public_key
      and expires_at > now();
  get diagnostics n = row_count;
  return n = 1;
end; $$;

revoke all on function public.lockvyn_pairing_request(uuid,text) from public,anon;
revoke all on function public.lockvyn_pairing_complete(uuid,text) from public,anon;
grant execute on function public.lockvyn_pairing_request(uuid,text) to authenticated;
grant execute on function public.lockvyn_pairing_complete(uuid,text) to authenticated;
comment on function public.lockvyn_pairing_request(uuid,text) is
 'Owner/MFA-bound temporary pairing request; invoker RLS and state transition trigger remain active.';
comment on function public.lockvyn_pairing_complete(uuid,text) is
 'Acknowledge only the approved recipient; invoker RLS and state transition trigger remain active.';
commit;
