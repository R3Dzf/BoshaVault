-- Require a currently active Auth session for all sensitive Lockvyn cloud operations.
-- Global sign-out removes auth.sessions rows; stateless JWTs remain cryptographically
-- valid until exp, so check the session row on RLS and privileged admin operations.
begin;

create or replace function public.lockvyn_auth_session_is_active(
    p_user_id uuid,
    p_session_id uuid
)
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select p_user_id is not null
      and p_session_id is not null
      and exists (
        select 1
        from auth.sessions s
        where s.id = p_session_id
          and s.user_id = p_user_id
          and (s.not_after is null or s.not_after > statement_timestamp())
      );
$$;

revoke all on function public.lockvyn_auth_session_is_active(uuid, uuid)
    from public, anon, authenticated;
grant execute on function public.lockvyn_auth_session_is_active(uuid, uuid)
    to service_role;
comment on function public.lockvyn_auth_session_is_active(uuid, uuid) is
    'Service-role-only check for the signed-in user JWT session_id; prevents revoked access JWTs from using privileged Lockvyn operations.';

create or replace function lockvyn_private.cloud_assurance_allowed()
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select (select auth.uid()) is not null
      and coalesce((select auth.jwt()->>'aal'),'aal1') in ('aal1','aal2')
      and (
        coalesce((select auth.jwt()->>'aal'),'aal1') = 'aal2'
        or not exists (
            select 1 from auth.mfa_factors
            where user_id = (select auth.uid()) and status = 'verified'
        )
      )
      and (select public.lockvyn_auth_session_is_active(
        (select auth.uid()),
        case
          when coalesce((select auth.jwt()->>'session_id'),'') ~*
            '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
          then ((select auth.jwt()->>'session_id')::uuid)
          else null::uuid
        end
      ));
$$;

revoke all on function lockvyn_private.cloud_assurance_allowed()
    from public, anon, authenticated;
grant execute on function lockvyn_private.cloud_assurance_allowed()
    to authenticated;
comment on function lockvyn_private.cloud_assurance_allowed() is
    'Requires verified AAL2 when MFA is enrolled and a live Auth session row. RLS uses this for encrypted snapshots and temporary device-pairing data.';

commit;
