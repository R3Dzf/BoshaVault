-- Synthetic-only Phase 07 database checks. All rows roll back.
begin;
insert into auth.users(id,aud,role,email,email_confirmed_at,created_at,updated_at) values
 ('a8000000-0000-4000-8000-000000000001','authenticated','authenticated','notice-admin@example.invalid',now(),now(),now()),
 ('a8000000-0000-4000-8000-000000000002','authenticated','authenticated','notice-user@example.invalid',now(),now(),now());
insert into auth.sessions(id,user_id) values
 ('a9000000-0000-4000-8000-000000000001','a8000000-0000-4000-8000-000000000001'),
 ('a9000000-0000-4000-8000-000000000002','a8000000-0000-4000-8000-000000000002');
insert into public.lockvyn_admins(user_id)
values ('a8000000-0000-4000-8000-000000000001');

do $phase07$
begin
  if has_table_privilege('anon','lockvyn_private.announcements','select')
     or has_table_privilege('authenticated','lockvyn_private.announcements','select')
     or has_table_privilege('service_role','lockvyn_private.announcements','select')
     or has_table_privilege('anon','lockvyn_private.announcement_audit','select')
     or has_table_privilege('authenticated','lockvyn_private.announcement_audit','select')
     or has_function_privilege('anon',
       'public.lockvyn_admin_announcement_manage(uuid,uuid,text,boolean,text,uuid,jsonb)','execute')
     or has_function_privilege('authenticated',
       'public.lockvyn_announcements_feed(text,text,text)','execute')
  then raise exception 'Announcement storage and RPCs must be private to service_role'; end if;
end;
$phase07$;

set local role service_role;
select set_config('request.jwt.claims',
 '{"role":"service_role","sub":"a8000000-0000-4000-8000-000000000001","session_id":"a9000000-0000-4000-8000-000000000001","aal":"aal2"}',true);
select set_config('request.jwt.claim.role','service_role',true);

do $phase07$
declare
  v jsonb;
  v_id uuid;
  payload jsonb:=jsonb_build_object(
    'titleEn','Maintenance notice',
    'titleAr','إشعار صيانة',
    'bodyEn','Cloud sync will pause briefly.',
    'bodyAr','سيتوقف التزامن السحابي لفترة قصيرة.',
    'severity','warning','targetPlatform','windows',
    'minVersion','1.20.0','maxVersion','1.22.0',
    'notify',true,'expiresAt',null
  );
  past_payload jsonb;
  i integer;
begin
  v:=public.lockvyn_admin_announcement_manage(
    'a8000000-0000-4000-8000-000000000001',
    'a9000000-0000-4000-8000-000000000001','aal2',true,'list',null,'{}'::jsonb);
  if jsonb_array_length(v->'announcements')<>0 then raise exception 'Synthetic list must start empty'; end if;

  v:=public.lockvyn_admin_announcement_manage(
    'a8000000-0000-4000-8000-000000000001',
    'a9000000-0000-4000-8000-000000000001','aal2',true,'create',null,payload);
  v_id:=(v->'announcement'->>'id')::uuid;
  if v->'announcement'->>'status'<>'draft' then raise exception 'New notices must be drafts'; end if;
  if jsonb_array_length(public.lockvyn_announcements_feed('windows','1.21.0','en'))<>0
  then raise exception 'Draft leaked to public feed'; end if;
  v:=public.lockvyn_admin_announcement_manage(
    'a8000000-0000-4000-8000-000000000001',
    'a9000000-0000-4000-8000-000000000001','aal2',true,'update',v_id,payload);
  if v->'announcement'->>'status'<>'draft' then raise exception 'Draft update changed its status'; end if;

  begin
    perform public.lockvyn_admin_announcement_manage(
      'a8000000-0000-4000-8000-000000000001',
      'a9000000-0000-4000-8000-000000000001','aal1',true,'create',null,payload);
    raise exception 'AAL1 announcement write was accepted';
  exception when insufficient_privilege then null; end;

  begin
    perform public.lockvyn_admin_announcement_manage(
      'a8000000-0000-4000-8000-000000000001',
      'a9000000-0000-4000-8000-000000000099','aal2',true,'create',null,payload);
    raise exception 'Announcement write with an inactive session was accepted';
  exception when insufficient_privilege then null; end;

  begin
    perform public.lockvyn_admin_announcement_manage(
      'a8000000-0000-4000-8000-000000000002',
      'a9000000-0000-4000-8000-000000000002','aal2',true,'create',null,payload);
    raise exception 'Non-admin announcement write was accepted';
  exception when insufficient_privilege then null; end;

  begin
    perform public.lockvyn_admin_announcement_manage(
      'a8000000-0000-4000-8000-000000000001',
      'a9000000-0000-4000-8000-000000000001','aal2',false,'create',null,payload);
    raise exception 'Announcement write without fresh TOTP was accepted';
  exception when insufficient_privilege then null; end;

  begin
    perform public.lockvyn_admin_announcement_manage(
      'a8000000-0000-4000-8000-000000000001',
      'a9000000-0000-4000-8000-000000000001','aal2',true,'create',null,
      payload||jsonb_build_object('bodyEn','owner@example.invalid'));
    raise exception 'Sensitive email content was accepted';
  exception when invalid_parameter_value then null; end;

  begin
    perform public.lockvyn_admin_announcement_manage(
      'a8000000-0000-4000-8000-000000000001',
      'a9000000-0000-4000-8000-000000000001','aal2',true,'create',null,
      payload||jsonb_build_object('minVersion','1.23.0'));
    raise exception 'Invalid version range was accepted';
  exception when invalid_parameter_value then null; end;

  v:=public.lockvyn_admin_announcement_manage(
    'a8000000-0000-4000-8000-000000000001',
    'a9000000-0000-4000-8000-000000000001','aal2',true,'publish',v_id,'{}'::jsonb);
  if v->'announcement'->>'status'<>'published' then raise exception 'Draft did not publish'; end if;

  v:=public.lockvyn_announcements_feed('windows','1.21.0','ar')->0;
  if v->>'title'<>'إشعار صيانة' or v->>'body'<>'سيتوقف التزامن السحابي لفترة قصيرة.'
     or v->>'notify'<>'true' or v ? 'actorId' or v ? 'deviceId' or v ? 'accountId'
  then raise exception 'Public localized payload is invalid: %',v; end if;
  if jsonb_array_length(public.lockvyn_announcements_feed('windows','1.19.0','en'))<>0
     or jsonb_array_length(public.lockvyn_announcements_feed('android','21','en'))<>0
  then raise exception 'Platform/version targeting leaked a notice'; end if;

  past_payload:=jsonb_set(payload,'{expiresAt}',to_jsonb((now()-interval '1 minute')::text));
  v:=public.lockvyn_admin_announcement_manage(
    'a8000000-0000-4000-8000-000000000001',
    'a9000000-0000-4000-8000-000000000001','aal2',true,'create',null,past_payload);
  v_id:=(v->'announcement'->>'id')::uuid;
  begin
    perform public.lockvyn_admin_announcement_manage(
      'a8000000-0000-4000-8000-000000000001',
      'a9000000-0000-4000-8000-000000000001','aal2',true,'publish',v_id,'{}'::jsonb);
    raise exception 'Expired draft was published';
  exception when invalid_parameter_value then null; end;

  v:=public.lockvyn_admin_announcement_manage(
    'a8000000-0000-4000-8000-000000000001',
    'a9000000-0000-4000-8000-000000000001','aal2',true,'archive',
    (public.lockvyn_announcements_feed('windows','1.21.0','en')->0->>'id')::uuid,'{}'::jsonb);
  if v->'announcement'->>'status'<>'archived'
     or jsonb_array_length(public.lockvyn_announcements_feed('windows','1.21.0','en'))<>0
  then raise exception 'Archived notice remained visible'; end if;

  begin
    perform public.lockvyn_admin_announcement_manage(
      'a8000000-0000-4000-8000-000000000001',
      'a9000000-0000-4000-8000-000000000001','aal2',true,'create',null,payload);
    raise exception 'Sixth write within one minute was accepted';
  exception when sqlstate '54000' then null; end;
end;
$phase07$;

rollback;
