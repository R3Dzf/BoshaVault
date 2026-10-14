-- Synthetic-only Phase 08 ticket fixtures. Every account and ticket rolls back.
begin;
insert into auth.users(id,aud,role,email,email_confirmed_at,created_at,updated_at) values
 ('a8000000-0000-4000-8000-000000000081','authenticated','authenticated','ticket-admin@example.invalid',now(),now(),now()),
 ('a8000000-0000-4000-8000-000000000082','authenticated','authenticated','ticket-user@example.invalid',now(),now(),now());
insert into auth.sessions(id,user_id) values
 ('a9000000-0000-4000-8000-000000000081','a8000000-0000-4000-8000-000000000081'),
 ('a9000000-0000-4000-8000-000000000082','a8000000-0000-4000-8000-000000000082');
insert into public.lockvyn_admins(user_id)
values('a8000000-0000-4000-8000-000000000081');

do $phase08_privileges$
begin
  if has_table_privilege('anon','lockvyn_private.support_tickets','select')
     or has_table_privilege('authenticated','lockvyn_private.support_tickets','select')
     or has_table_privilege('service_role','lockvyn_private.support_tickets','select')
     or has_table_privilege('anon','lockvyn_private.support_ticket_messages','select')
     or has_table_privilege('authenticated','lockvyn_private.support_ticket_attachments','select')
     or has_table_privilege('service_role','lockvyn_private.support_ticket_audit','select')
     or has_function_privilege('anon','public.lockvyn_support_ticket_user_manage(text,uuid,uuid,uuid,jsonb)','execute')
     or has_function_privilege('authenticated','public.lockvyn_support_ticket_admin_manage(text,uuid,uuid,text,boolean,uuid,jsonb)','execute')
     or has_function_privilege('anon','public.lockvyn_support_ticket_purge_expired()','execute')
  then raise exception 'Support ticket tables and RPCs must be private to service_role'; end if;
  if not exists(select 1 from pg_class c join pg_namespace n on n.oid=c.relnamespace
    where n.nspname='lockvyn_private' and c.relname='support_tickets'
      and c.relrowsecurity and c.relforcerowsecurity)
  then raise exception 'Support ticket table must enforce row level security'; end if;
  if (select retention from (select interval '180 days' as retention) x)<>interval '180 days'
  then raise exception 'Ticket retention policy must be 180 days'; end if;
end;
$phase08_privileges$;

select set_config('request.jwt.claim.role','service_role',true);
select set_config('request.jwt.claims',
 '{"role":"service_role","sub":"a8000000-0000-4000-8000-000000000082","session_id":"a9000000-0000-4000-8000-000000000082","aal":"aal1"}',true);

do $phase08_user$
declare
  v jsonb;
  v_ticket uuid;
  v_other uuid;
  v_count integer;
  v_payload jsonb;
  i integer;
begin
  v_payload:=jsonb_build_object(
    'subject','Cannot sync my vault',
    'body','Support request with Bearer [redacted] and [redacted sensitive field].',
    'diagnosticConsent',true,
    'diagnostics',jsonb_build_object('platform','windows','appVersion','3.1.0','osVersion','Windows 11','errorCode','HTTP_503')
  );
  v:=public.lockvyn_support_ticket_user_manage('create',
    'a8000000-0000-4000-8000-000000000082','a9000000-0000-4000-8000-000000000082',null,v_payload);
  v_ticket:=(v->'ticket'->>'id')::uuid;
  if v->'ticket'->>'status'<>'open' then raise exception 'New ticket must be open'; end if;
  if v ? 'requesterId' or v ? 'requester_id' then raise exception 'User response must omit account identity'; end if;
  if not exists(select 1 from lockvyn_private.support_ticket_attachments
    where ticket_id=v_ticket and consented_at is not null and payload->>'platform'='windows')
  then raise exception 'Consented diagnostics were not stored'; end if;
  if exists(select 1 from lockvyn_private.support_ticket_audit where ticket_id=v_ticket
    and (coalesce(previous_state::text,'') like '%Cannot sync%' or coalesce(new_state::text,'') like '%Bearer%'))
  then raise exception 'Audit must not retain message or diagnostic bodies'; end if;
  if not exists(select 1 from lockvyn_private.support_ticket_audit where ticket_id=v_ticket
    and action='diagnostics_upload' and new_state=jsonb_build_object('consented',true,'fieldCount',4))
  then raise exception 'Consented diagnostics upload must be audited without its content'; end if;
  begin
    update lockvyn_private.support_ticket_attachments set filename='vault.json' where ticket_id=v_ticket;
    raise exception 'Arbitrary attachment filename was accepted';
  exception when check_violation then null; end;

  begin
    perform public.lockvyn_support_ticket_user_manage('create',
      'a8000000-0000-4000-8000-000000000082','a9000000-0000-4000-8000-000000000082',null,
      jsonb_build_object('subject','No consent','body','Try a support request.','diagnosticConsent',false,
        'diagnostics',jsonb_build_object('platform','windows')));
    raise exception 'Diagnostics without explicit consent were accepted';
  exception when invalid_parameter_value then null; end;

  begin
    perform public.lockvyn_support_ticket_user_manage('create',
      'a8000000-0000-4000-8000-000000000082','a9000000-0000-4000-8000-000000000082',null,
      jsonb_build_object('subject','Invalid attachment','body','Try a support request.','diagnosticConsent',true,
        'diagnostics',jsonb_build_object('deviceId','hardware-id')));
    raise exception 'Unsupported attachment fields were accepted';
  exception when invalid_parameter_value then null; end;

  begin
    perform public.lockvyn_support_ticket_user_manage('create',
      'a8000000-0000-4000-8000-000000000082','a9000000-0000-4000-8000-000000000082',null,
      jsonb_build_object('subject','Credential leak','body','password=clear-secret','diagnosticConsent',false,'diagnostics',null));
    raise exception 'Unredacted credential text was stored';
  exception when invalid_parameter_value then null; end;

  v:=public.lockvyn_support_ticket_user_manage('get',
    'a8000000-0000-4000-8000-000000000082','a9000000-0000-4000-8000-000000000082',v_ticket,'{}'::jsonb);
  if jsonb_array_length(v->'messages')<>1 or v->'diagnostics'->'payload'->>'errorCode'<>'HTTP_503'
  then raise exception 'User can read own thread and consented attachment'; end if;

  begin
    perform public.lockvyn_support_ticket_user_manage('get',
      'a8000000-0000-4000-8000-000000000081','a9000000-0000-4000-8000-000000000081',v_ticket,'{}'::jsonb);
    raise exception 'Non-owner read a private ticket';
  exception when no_data_found then null; end;

  v:=public.lockvyn_support_ticket_user_manage('reply',
    'a8000000-0000-4000-8000-000000000082','a9000000-0000-4000-8000-000000000082',v_ticket,
    jsonb_build_object('body','Thanks for checking.'));
  if v->'ticket'->>'status'<>'open' then raise exception 'User reply should reopen ticket for agents'; end if;
  perform public.lockvyn_support_ticket_user_manage('close',
    'a8000000-0000-4000-8000-000000000082','a9000000-0000-4000-8000-000000000082',v_ticket,'{}'::jsonb);
  begin
    perform public.lockvyn_support_ticket_user_manage('reply',
      'a8000000-0000-4000-8000-000000000082','a9000000-0000-4000-8000-000000000082',v_ticket,
      jsonb_build_object('body','This reply should fail.'));
    raise exception 'Closed ticket accepted a reply';
  exception when invalid_parameter_value then null; end;
  perform public.lockvyn_support_ticket_user_manage('reopen',
    'a8000000-0000-4000-8000-000000000082','a9000000-0000-4000-8000-000000000082',v_ticket,'{}'::jsonb);

  for i in 1..18 loop
    perform public.lockvyn_support_ticket_user_manage('reply',
      'a8000000-0000-4000-8000-000000000082','a9000000-0000-4000-8000-000000000082',v_ticket,
      jsonb_build_object('body','Synthetic follow-up '||i));
  end loop;
  begin
    perform public.lockvyn_support_ticket_user_manage('reply',
      'a8000000-0000-4000-8000-000000000082','a9000000-0000-4000-8000-000000000082',v_ticket,
      jsonb_build_object('body','Rate limit should reject this.'));
    raise exception 'User reply rate limit was not enforced';
  exception when program_limit_exceeded then null; end;

  for i in 1..4 loop
    perform public.lockvyn_support_ticket_user_manage('create',
      'a8000000-0000-4000-8000-000000000082','a9000000-0000-4000-8000-000000000082',null,
      jsonb_build_object('subject','Ticket number '||i,'body','Synthetic request.','diagnosticConsent',false,'diagnostics',null));
  end loop;
  begin
    perform public.lockvyn_support_ticket_user_manage('create',
      'a8000000-0000-4000-8000-000000000082','a9000000-0000-4000-8000-000000000082',null,
      jsonb_build_object('subject','Sixth ticket','body','Rate limit should reject this.','diagnosticConsent',false,'diagnostics',null));
    raise exception 'Ticket creation rate limit was not enforced';
  exception when program_limit_exceeded then null; end;

  update lockvyn_private.support_tickets set expires_at=now()-interval '1 second' where id=v_ticket;
  v:=public.lockvyn_support_ticket_user_manage('list',
    'a8000000-0000-4000-8000-000000000082','a9000000-0000-4000-8000-000000000082',null,'{}'::jsonb);
  if exists(select 1 from lockvyn_private.support_tickets where id=v_ticket)
     or exists(select 1 from lockvyn_private.support_ticket_messages where ticket_id=v_ticket)
     or exists(select 1 from lockvyn_private.support_ticket_attachments where ticket_id=v_ticket)
  then raise exception 'Expired ticket data and attachment were not purged'; end if;
end;
$phase08_user$;

select set_config('request.jwt.claims',
 '{"role":"service_role","sub":"a8000000-0000-4000-8000-000000000081","session_id":"a9000000-0000-4000-8000-000000000081","aal":"aal2"}',true);

do $phase08_agent$
declare
  v jsonb;
  v_ticket uuid;
  i integer;
begin
  v:=public.lockvyn_support_ticket_user_manage('create',
    'a8000000-0000-4000-8000-000000000082','a9000000-0000-4000-8000-000000000082',null,
    jsonb_build_object('subject','Synthetic agent test','body','Agent workflow fixture.','diagnosticConsent',false,'diagnostics',null));
  v_ticket:=(v->'ticket'->>'id')::uuid;
  v:=public.lockvyn_support_ticket_admin_manage('list',
    'a8000000-0000-4000-8000-000000000081','a9000000-0000-4000-8000-000000000081','aal2',false,null,'{}'::jsonb);
  if not exists(select 1 from jsonb_array_elements(v->'tickets') as t where t->>'id'=v_ticket::text)
  then raise exception 'Agent queue must contain the active synthetic ticket'; end if;
  if (v->'tickets'->0) ? 'requesterId' then raise exception 'Agent queue omits requester identity'; end if;

  begin
    perform public.lockvyn_support_ticket_admin_manage('assign',
      'a8000000-0000-4000-8000-000000000081','a9000000-0000-4000-8000-000000000081','aal1',true,
      v_ticket,jsonb_build_object('assigneeId','a8000000-0000-4000-8000-000000000081'));
    raise exception 'AAL1 administrator action was accepted';
  exception when insufficient_privilege then null; end;
  begin
    perform public.lockvyn_support_ticket_admin_manage('assign',
      'a8000000-0000-4000-8000-000000000081','a9000000-0000-4000-8000-000000000081','aal2',false,
      v_ticket,jsonb_build_object('assigneeId','a8000000-0000-4000-8000-000000000081'));
    raise exception 'Agent mutation without fresh TOTP was accepted';
  exception when insufficient_privilege then null; end;
  begin
    perform public.lockvyn_support_ticket_admin_manage('assign',
      'a8000000-0000-4000-8000-000000000081','a9000000-0000-4000-8000-000000000081','aal2',true,
      v_ticket,jsonb_build_object('assigneeId','a8000000-0000-4000-8000-000000000082'));
    raise exception 'A non-admin was assigned the ticket';
  exception when invalid_parameter_value then null; end;

  v:=public.lockvyn_support_ticket_admin_manage('assign',
    'a8000000-0000-4000-8000-000000000081','a9000000-0000-4000-8000-000000000081','aal2',true,
    v_ticket,jsonb_build_object('assigneeId','a8000000-0000-4000-8000-000000000081'));
  if v->'ticket'->>'status'<>'in_progress' then raise exception 'Assigned tickets should enter progress'; end if;
  v:=public.lockvyn_support_ticket_admin_manage('escalate',
    'a8000000-0000-4000-8000-000000000081','a9000000-0000-4000-8000-000000000081','aal2',true,
    v_ticket,'{}'::jsonb);
  if v->'ticket'->>'status'<>'escalated' or v->'ticket'->>'priority'<>'high'
  then raise exception 'Escalation did not raise priority'; end if;
  perform public.lockvyn_support_ticket_admin_manage('reply',
    'a8000000-0000-4000-8000-000000000081','a9000000-0000-4000-8000-000000000081','aal2',true,
    v_ticket,jsonb_build_object('body','We are reviewing this request.'));
  v:=public.lockvyn_support_ticket_admin_manage('get',
    'a8000000-0000-4000-8000-000000000081','a9000000-0000-4000-8000-000000000081','aal2',false,
    v_ticket,'{}'::jsonb);
  if jsonb_array_length(v->'messages')<>2 or v->'messages'->1->>'senderRole'<>'agent'
  then raise exception 'Agent response was not added to the thread'; end if;
  if (v->'ticket') ? 'requesterId' or (v->'ticket') ? 'requester_id'
  then raise exception 'Admin response should not expose account identifiers'; end if;

  for i in 1..27 loop
    perform public.lockvyn_support_ticket_admin_manage('status',
      'a8000000-0000-4000-8000-000000000081','a9000000-0000-4000-8000-000000000081','aal2',true,
      v_ticket,jsonb_build_object('status','in_progress','priority','normal'));
  end loop;
  begin
    perform public.lockvyn_support_ticket_admin_manage('status',
      'a8000000-0000-4000-8000-000000000081','a9000000-0000-4000-8000-000000000081','aal2',true,
      v_ticket,jsonb_build_object('status','open','priority','normal'));
    raise exception 'Agent mutation rate limit was not enforced';
  exception when program_limit_exceeded then null; end;
end;
$phase08_agent$;

select public.lockvyn_support_ticket_purge_expired();
rollback;
