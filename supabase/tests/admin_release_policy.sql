-- Synthetic-only Phase 06 release policy security and rollback fixture.
begin;
insert into auth.users(id,aud,role,email,email_confirmed_at,created_at,updated_at) values
 ('a6000000-0000-4000-8000-000000000001','authenticated','authenticated','release-admin@example.invalid',now(),now(),now()),
 ('a6000000-0000-4000-8000-000000000002','authenticated','authenticated','release-user@example.invalid',now(),now(),now());
insert into auth.sessions(id,user_id) values
 ('a7000000-0000-4000-8000-000000000001','a6000000-0000-4000-8000-000000000001'),
 ('a7000000-0000-4000-8000-000000000002','a6000000-0000-4000-8000-000000000002');
insert into public.lockvyn_admins(user_id)
values ('a6000000-0000-4000-8000-000000000001');

do $phase06$
begin
  if has_table_privilege('anon','lockvyn_private.release_policies','select')
     or has_table_privilege('authenticated','lockvyn_private.release_policies','select')
     or has_table_privilege('service_role','lockvyn_private.release_policies','select')
     or has_table_privilege('anon','lockvyn_private.release_policy_audit','select')
     or has_table_privilege('authenticated','lockvyn_private.release_policy_audit','select')
  then raise exception 'Release policy tables must have no direct Data API grants'; end if;
end;
$phase06$;

set local role service_role;
select set_config('request.jwt.claims',
 '{"role":"service_role","sub":"a6000000-0000-4000-8000-000000000001","session_id":"a7000000-0000-4000-8000-000000000001","aal":"aal2"}',true);
select set_config('request.jwt.claim.role','service_role',true);

do $phase06$
declare v jsonb;
begin
  v:=public.lockvyn_release_policy_public('windows');
  if v->>'platform'<>'windows' or v->>'candidateTag' is not null
     or v->>'rolloutPercent'<>'100' or v->>'minimumVersion'<>'0.0.0'
     or v ? 'actorId' or v ? 'audit'
  then raise exception 'Default public policy was not sanitized: %',v; end if;
  v:=public.lockvyn_admin_set_release_policy(
    'a6000000-0000-4000-8000-000000000001',
    'a7000000-0000-4000-8000-000000000001','aal2',true,
    'windows',null,'1.20.0',0,'rollback');
  if v->>'rolloutPercent'<>'0' or v->>'candidateTag' is not null
     or v->>'minimumVersion'<>'1.20.0'
  then raise exception 'Rollback policy did not stop the optional rollout: %',v; end if;

  begin
    perform public.lockvyn_admin_set_release_policy(
      'a6000000-0000-4000-8000-000000000001',
      'a7000000-0000-4000-8000-000000000001','aal1',true,
      'windows',null,'1.20.0',100,'planned_rollout');
    raise exception 'AAL1 mutation was accepted';
  exception when insufficient_privilege then null; end;

  begin
    perform public.lockvyn_admin_set_release_policy(
      'a6000000-0000-4000-8000-000000000002',
      'a7000000-0000-4000-8000-000000000002','aal2',true,
      'windows',null,'1.20.0',100,'planned_rollout');
    raise exception 'Non-admin mutation was accepted';
  exception when insufficient_privilege then null; end;

  begin
    perform public.lockvyn_admin_set_release_policy(
      'a6000000-0000-4000-8000-000000000001',
      'a7000000-0000-4000-8000-000000000001','aal2',false,
      'windows',null,'1.20.0',100,'planned_rollout');
    raise exception 'Mutation without fresh TOTP was accepted';
  exception when insufficient_privilege then null; end;

  begin
    perform public.lockvyn_admin_set_release_policy(
      'a6000000-0000-4000-8000-000000000001',
      'a7000000-0000-4000-8000-000000000001','aal2',true,
      'windows','windows-v1.20.0','1.21.0',100,'planned_rollout');
    raise exception 'Minimum version above the pinned release was accepted';
  exception when invalid_parameter_value then null; end;

  for i in 1..4 loop
    perform public.lockvyn_admin_set_release_policy(
      'a6000000-0000-4000-8000-000000000001',
      'a7000000-0000-4000-8000-000000000001','aal2',true,
      'android',null,'21',100,'planned_rollout');
  end loop;
  begin
    perform public.lockvyn_admin_set_release_policy(
      'a6000000-0000-4000-8000-000000000001',
      'a7000000-0000-4000-8000-000000000001','aal2',true,
      'android',null,'21',100,'planned_rollout');
    raise exception 'Sixth policy change inside one minute was accepted';
  exception when program_limit_exceeded then null; end;
end;
$phase06$;
reset role;

do $phase06$
begin
  if (select count(*) from lockvyn_private.release_policy_audit
      where actor_id='a6000000-0000-4000-8000-000000000001')<>5
  then raise exception 'Successful policy changes were not audited'; end if;
  if (select count(*) from lockvyn_private.release_policy_audit
      where reason_code='rollback')<>1
  then raise exception 'Rollback reason was not recorded'; end if;
  if (select rollout_percent from lockvyn_private.release_policies where platform='windows')<>0
  then raise exception 'Failed policy changes altered the stored rollback'; end if;
end;
$phase06$;
rollback;
