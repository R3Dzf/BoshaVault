-- Phase 01: read-only owner account directory.
-- Explicit service-role RPC: never expose auth.users to the client.
begin;
create or replace function public.lockvyn_admin_directory(
  p_actor uuid,
  p_page integer default 1,
  p_search text default '',
  p_email_state text default 'all',
  p_mfa_state text default 'all'
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_result jsonb;
begin
  if p_actor is null or not exists(
    select 1 from public.lockvyn_admins where user_id = p_actor
  ) then
    raise exception 'Unauthorized account listing' using errcode = '42501';
  end if;
  if p_page is null or p_page < 1 or p_page > 10000 or
     p_search is null or length(p_search) > 120 or
     p_email_state is null or p_email_state not in ('all','verified','pending') or
     p_mfa_state is null or p_mfa_state not in ('all','enabled','disabled')
  then
    raise exception 'Invalid directory filters' using errcode = '22023';
  end if;

  with base as (
    select
      u.id, coalesce(u.email,'')::text as email,
      u.created_at, u.last_sign_in_at,
      (u.email_confirmed_at is not null) as confirmed,
      exists(select 1 from auth.mfa_factors f
        where f.user_id = u.id and f.status = 'verified') as mfa
    from auth.users u
  ), matched as (
    select b.*
    from base b
    where (btrim(p_search) = '' or strpos(lower(b.email),lower(btrim(p_search))) > 0)
      and (p_email_state = 'all'
        or (p_email_state = 'verified' and b.confirmed)
        or (p_email_state = 'pending' and not b.confirmed))
      and (p_mfa_state = 'all'
        or (p_mfa_state = 'enabled' and b.mfa)
        or (p_mfa_state = 'disabled' and not b.mfa))
  ), page_rows as (
    select * from matched order by created_at desc, id desc
      limit 30 offset (p_page-1)*30
  )
  select pg_catalog.jsonb_build_object(
    'page',p_page,
    'perPage',30,
    'total',(select count(*) from base),
    'matching',(select count(*) from matched),
    'stats',(select pg_catalog.jsonb_build_object(
       'verified',(select count(*) from base where confirmed),
       'mfaEnabled',(select count(*) from base where mfa),
       'active30Days',(select count(*) from base
          where last_sign_in_at >= now() - interval '30 days')
    )),
    'users',(select coalesce(pg_catalog.jsonb_agg(
      pg_catalog.jsonb_build_object(
         'id',pr.id,
         'email',pr.email,
         'createdAt',pr.created_at,
         'lastSignInAt',pr.last_sign_in_at,
         'confirmed',pr.confirmed,
         'mfa',pr.mfa,
         'isSelf',(pr.id=p_actor)
      ) order by pr.created_at desc, pr.id desc),'[]'::jsonb)
      from page_rows pr)
  )
  into v_result;
  return v_result;
end;
$$;
revoke all on function public.lockvyn_admin_directory(uuid,integer,text,text,text)
  from public,anon,authenticated;
grant execute on function public.lockvyn_admin_directory(uuid,integer,text,text,text)
  to service_role;
comment on function public.lockvyn_admin_directory(uuid,integer,text,text,text) is
 'Read-only, scoped service-role account metadata query. AAL2 and operator identity are independently checked in the Edge Function.';
commit;
