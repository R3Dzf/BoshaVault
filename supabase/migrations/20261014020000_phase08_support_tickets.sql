-- Phase 08: private, consent-based in-app support tickets.
-- Additive only. Ticket content never includes vault snapshots or credentials.
begin;

create schema if not exists lockvyn_private;

create table lockvyn_private.support_tickets (
  id uuid primary key default gen_random_uuid(),
  requester_id uuid not null references auth.users(id) on delete cascade,
  subject text not null,
  status text not null default 'open'
    check (status in ('open','in_progress','escalated','closed')),
  priority text not null default 'normal'
    check (priority in ('low','normal','high')),
  assigned_to uuid references auth.users(id) on delete set null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  closed_at timestamptz,
  escalated_at timestamptz,
  expires_at timestamptz not null default (now() + interval '180 days'),
  constraint support_ticket_subject_length check (char_length(subject) between 3 and 120)
);
create index support_tickets_user_recent_idx
  on lockvyn_private.support_tickets(requester_id,updated_at desc);
create index support_tickets_queue_idx
  on lockvyn_private.support_tickets(status,priority,updated_at desc);

create table lockvyn_private.support_ticket_messages (
  id uuid primary key default gen_random_uuid(),
  ticket_id uuid not null references lockvyn_private.support_tickets(id) on delete cascade,
  sender_id uuid not null references auth.users(id) on delete cascade,
  sender_role text not null check (sender_role in ('user','agent')),
  body text not null check (char_length(body) between 1 and 4000),
  created_at timestamptz not null default now()
);
create index support_ticket_messages_thread_idx
  on lockvyn_private.support_ticket_messages(ticket_id,created_at,id);

create table lockvyn_private.support_ticket_attachments (
  ticket_id uuid primary key references lockvyn_private.support_tickets(id) on delete cascade,
  filename text not null default 'diagnostics.json' check (filename='diagnostics.json'),
  content_type text not null default 'application/json' check (content_type='application/json'),
  payload jsonb not null check (jsonb_typeof(payload)='object'),
  consented_at timestamptz not null,
  created_at timestamptz not null default now()
);

create table lockvyn_private.support_ticket_audit (
  id bigint generated always as identity primary key,
  ticket_id uuid not null references lockvyn_private.support_tickets(id) on delete cascade,
  actor_id uuid not null references auth.users(id) on delete cascade,
  actor_session_id uuid not null,
  actor_role text not null check (actor_role in ('user','agent')),
  action text not null check (action in (
    'create','reply','close','reopen','diagnostics_upload',
    'assign','status','escalate','agent_reply'
  )),
  previous_state jsonb,
  new_state jsonb,
  created_at timestamptz not null default now()
);
create index support_ticket_audit_ticket_idx
  on lockvyn_private.support_ticket_audit(ticket_id,created_at desc);
create index support_ticket_audit_actor_idx
  on lockvyn_private.support_ticket_audit(actor_id,created_at desc);

alter table lockvyn_private.support_tickets enable row level security;
alter table lockvyn_private.support_tickets force row level security;
alter table lockvyn_private.support_ticket_messages enable row level security;
alter table lockvyn_private.support_ticket_messages force row level security;
alter table lockvyn_private.support_ticket_attachments enable row level security;
alter table lockvyn_private.support_ticket_attachments force row level security;
alter table lockvyn_private.support_ticket_audit enable row level security;
alter table lockvyn_private.support_ticket_audit force row level security;
revoke all on lockvyn_private.support_tickets from public,anon,authenticated,service_role;
revoke all on lockvyn_private.support_ticket_messages from public,anon,authenticated,service_role;
revoke all on lockvyn_private.support_ticket_attachments from public,anon,authenticated,service_role;
revoke all on lockvyn_private.support_ticket_audit from public,anon,authenticated,service_role;

create or replace function lockvyn_private.support_text_is_safe(p_text text)
returns boolean
language sql immutable
set search_path = ''
as $$
  select coalesce(
    p_text !~* 'bearer[[:space:]]+[a-z0-9._~+/-]{12,}' and
    p_text !~ 'eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}' and
    p_text !~* '(password|passphrase|master[ _-]?key|recovery[ _-]?key|api[ _-]?key|authorization|secret)[[:space:]]*[:=][[:space:]]*[^[:space:],;]{4,}',
    false
  );
$$;
revoke all on function lockvyn_private.support_text_is_safe(text) from public,anon,authenticated,service_role;

create or replace function public.lockvyn_support_ticket_purge_expired()
returns integer
language plpgsql security definer set search_path = ''
as $$
declare v_deleted integer;
begin
  if coalesce(current_setting('request.jwt.claim.role',true),'') <> 'service_role' then
    raise exception 'Service role required' using errcode='42501';
  end if;
  delete from lockvyn_private.support_tickets where expires_at<=now();
  get diagnostics v_deleted = row_count;
  return v_deleted;
end;
$$;
revoke all on function public.lockvyn_support_ticket_purge_expired() from public,anon,authenticated;
grant execute on function public.lockvyn_support_ticket_purge_expired() to service_role;

create or replace function public.lockvyn_support_ticket_user_manage(
  p_action text,p_actor uuid,p_actor_session uuid,
  p_ticket_id uuid default null,p_payload jsonb default '{}'::jsonb
)
returns jsonb
language plpgsql security definer set search_path = ''
as $$
declare
  v_ticket lockvyn_private.support_tickets%rowtype;
  v_subject text;
  v_body text;
  v_diagnostics jsonb;
  v_consent boolean;
  v_count integer;
  v_messages jsonb;
  v_attachment jsonb;
  v_previous jsonb;
  v_new jsonb;
begin
  if coalesce(current_setting('request.jwt.claim.role',true),'') <> 'service_role' or
     p_actor is null or p_actor_session is null or
     public.lockvyn_auth_session_is_active(p_actor,p_actor_session) is distinct from true then
    raise exception 'A verified active user session is required' using errcode='42501';
  end if;
  if p_action not in ('create','list','get','reply','close','reopen') or
     p_payload is null or jsonb_typeof(p_payload)<>'object' then
    raise exception 'Invalid support ticket request' using errcode='22023';
  end if;
  perform public.lockvyn_support_ticket_purge_expired();

  if p_action='list' then
    select coalesce(jsonb_agg(jsonb_build_object(
      'id',q.id,'subject',q.subject,'status',q.status,'priority',q.priority,
      'createdAt',q.created_at,'updatedAt',q.updated_at,'closedAt',q.closed_at,
      'messageCount',q.message_count
    ) order by q.updated_at desc),'[]'::jsonb)
    into v_messages
    from (
      select t.id,t.subject,t.status,t.priority,t.created_at,t.updated_at,t.closed_at,
        (select count(*) from lockvyn_private.support_ticket_messages m where m.ticket_id=t.id) as message_count
      from lockvyn_private.support_tickets t
      where t.requester_id=p_actor and t.expires_at>now()
      order by t.updated_at desc limit 50
    ) q;
    return jsonb_build_object('tickets',v_messages);
  end if;

  if p_action='create' then
    if p_ticket_id is not null then raise exception 'New ticket must not include an id' using errcode='22023'; end if;
    v_subject:=btrim(coalesce(p_payload->>'subject',''));
    v_body:=btrim(coalesce(p_payload->>'body',''));
    if char_length(v_subject) not between 3 and 120 or char_length(v_body) not between 1 and 4000 or
       not lockvyn_private.support_text_is_safe(v_subject) or
       not lockvyn_private.support_text_is_safe(v_body) then
      raise exception 'Ticket text is invalid or contains an unredacted credential' using errcode='22023';
    end if;
    if jsonb_typeof(p_payload->'diagnosticConsent') is distinct from 'boolean' then
      raise exception 'Diagnostic consent must be explicitly answered' using errcode='22023';
    end if;
    v_consent:=(p_payload->>'diagnosticConsent')::boolean;
    v_diagnostics:=p_payload->'diagnostics';
    if v_diagnostics is not null and jsonb_typeof(v_diagnostics)<>'null' then
      if not v_consent or jsonb_typeof(v_diagnostics)<>'object' or
         length(v_diagnostics::text)>8192 or jsonb_object_length(v_diagnostics)>4 or
         exists(select 1 from jsonb_object_keys(v_diagnostics) as k where k not in ('platform','appVersion','osVersion','errorCode')) or
         exists(select 1 from jsonb_each(v_diagnostics) as e(key,value)
           where jsonb_typeof(e.value)<>'string' or length(e.value #>> '{}')>120 or
             not lockvyn_private.support_text_is_safe(e.value #>> '{}')) then
        raise exception 'Diagnostics are not consented or contain unsupported fields' using errcode='22023';
      end if;
    elsif v_consent then
      raise exception 'Consent was given without a diagnostics attachment' using errcode='22023';
    end if;
    perform pg_advisory_xact_lock(hashtextextended(p_actor::text,808081));
    select count(*) into v_count from lockvyn_private.support_tickets
      where requester_id=p_actor and created_at>now()-interval '24 hours';
    if v_count>=5 then raise exception 'Support ticket creation limit reached' using errcode='54000'; end if;
    insert into lockvyn_private.support_tickets(requester_id,subject)
      values(p_actor,v_subject) returning * into v_ticket;
    insert into lockvyn_private.support_ticket_messages(ticket_id,sender_id,sender_role,body)
      values(v_ticket.id,p_actor,'user',v_body);
    if v_diagnostics is not null and jsonb_typeof(v_diagnostics)='object' then
      insert into lockvyn_private.support_ticket_attachments(ticket_id,payload,consented_at)
        values(v_ticket.id,v_diagnostics,now());
      insert into lockvyn_private.support_ticket_audit(ticket_id,actor_id,actor_session_id,actor_role,action,new_state)
        values(v_ticket.id,p_actor,p_actor_session,'user','diagnostics_upload',
          jsonb_build_object('consented',true,'fieldCount',jsonb_object_length(v_diagnostics)));
    end if;
    insert into lockvyn_private.support_ticket_audit(ticket_id,actor_id,actor_session_id,actor_role,action,new_state)
      values(v_ticket.id,p_actor,p_actor_session,'user','create',jsonb_build_object('status','open','priority','normal'));
    return jsonb_build_object('ticket',jsonb_build_object(
      'id',v_ticket.id,'subject',v_ticket.subject,'status',v_ticket.status,
      'priority',v_ticket.priority,'createdAt',v_ticket.created_at,'updatedAt',v_ticket.updated_at
    ));
  end if;

  if p_ticket_id is null then raise exception 'Ticket id is required' using errcode='22023'; end if;
  select * into v_ticket from lockvyn_private.support_tickets
    where id=p_ticket_id and requester_id=p_actor and expires_at>now() for update;
  if not found then raise exception 'Ticket not found' using errcode='P0002'; end if;

  if p_action='get' then
    select coalesce(jsonb_agg(jsonb_build_object(
      'id',m.id,'senderRole',m.sender_role,'body',m.body,'createdAt',m.created_at
    ) order by m.created_at,m.id),'[]'::jsonb) into v_messages
    from lockvyn_private.support_ticket_messages m where m.ticket_id=v_ticket.id;
    select jsonb_build_object('filename',a.filename,'contentType',a.content_type,
      'payload',a.payload,'consentedAt',a.consented_at) into v_attachment
    from lockvyn_private.support_ticket_attachments a where a.ticket_id=v_ticket.id;
    return jsonb_build_object('ticket',jsonb_build_object(
      'id',v_ticket.id,'subject',v_ticket.subject,'status',v_ticket.status,'priority',v_ticket.priority,
      'createdAt',v_ticket.created_at,'updatedAt',v_ticket.updated_at,'closedAt',v_ticket.closed_at
    ),'messages',v_messages,'diagnostics',coalesce(v_attachment,'null'::jsonb));
  elsif p_action='reply' then
    v_body:=btrim(coalesce(p_payload->>'body',''));
    if v_ticket.status='closed' or char_length(v_body) not between 1 and 4000 or
       not lockvyn_private.support_text_is_safe(v_body) then
      raise exception 'Ticket is closed or reply text is invalid' using errcode='22023';
    end if;
    perform pg_advisory_xact_lock(hashtextextended(p_actor::text,808082));
    select count(*) into v_count from lockvyn_private.support_ticket_messages
      where sender_id=p_actor and sender_role='user' and created_at>now()-interval '1 hour';
    if v_count>=20 then raise exception 'Support reply limit reached' using errcode='54000'; end if;
    insert into lockvyn_private.support_ticket_messages(ticket_id,sender_id,sender_role,body)
      values(v_ticket.id,p_actor,'user',v_body);
    update lockvyn_private.support_tickets set status='open',updated_at=now()
      where id=v_ticket.id returning * into v_ticket;
    insert into lockvyn_private.support_ticket_audit(ticket_id,actor_id,actor_session_id,actor_role,action,previous_state,new_state)
      values(v_ticket.id,p_actor,p_actor_session,'user','reply',
        jsonb_build_object('status','in_progress'),jsonb_build_object('status',v_ticket.status));
  elsif p_action in ('close','reopen') then
    if (p_action='close' and v_ticket.status='closed') or
       (p_action='reopen' and v_ticket.status<>'closed') then
      raise exception 'Ticket state does not allow this action' using errcode='55000';
    end if;
    v_previous:=jsonb_build_object('status',v_ticket.status,'priority',v_ticket.priority);
    update lockvyn_private.support_tickets set
      status=case when p_action='close' then 'closed' else 'open' end,
      closed_at=case when p_action='close' then now() else null end,
      updated_at=now()
      where id=v_ticket.id returning * into v_ticket;
    v_new:=jsonb_build_object('status',v_ticket.status,'priority',v_ticket.priority);
    insert into lockvyn_private.support_ticket_audit(ticket_id,actor_id,actor_session_id,actor_role,action,previous_state,new_state)
      values(v_ticket.id,p_actor,p_actor_session,'user',p_action,v_previous,v_new);
  end if;
  return jsonb_build_object('ticket',jsonb_build_object(
    'id',v_ticket.id,'subject',v_ticket.subject,'status',v_ticket.status,
    'priority',v_ticket.priority,'createdAt',v_ticket.created_at,'updatedAt',v_ticket.updated_at,
    'closedAt',v_ticket.closed_at
  ));
end;
$$;
revoke all on function public.lockvyn_support_ticket_user_manage(text,uuid,uuid,uuid,jsonb) from public,anon,authenticated;
grant execute on function public.lockvyn_support_ticket_user_manage(text,uuid,uuid,uuid,jsonb) to service_role;

create or replace function public.lockvyn_support_ticket_admin_manage(
  p_action text,p_actor uuid,p_actor_session uuid,p_actor_aal text,p_fresh_totp boolean,
  p_ticket_id uuid default null,p_payload jsonb default '{}'::jsonb
)
returns jsonb
language plpgsql security definer set search_path = ''
as $$
declare
  v_ticket lockvyn_private.support_tickets%rowtype;
  v_assignee uuid;
  v_status text;
  v_priority text;
  v_body text;
  v_count integer;
  v_tickets jsonb;
  v_messages jsonb;
  v_attachment jsonb;
  v_previous jsonb;
  v_new jsonb;
begin
  if coalesce(current_setting('request.jwt.claim.role',true),'') <> 'service_role' or
     p_actor is null or p_actor_session is null or p_actor_aal is distinct from 'aal2' or
     not exists(select 1 from public.lockvyn_admins where user_id=p_actor) or
     public.lockvyn_auth_session_is_active(p_actor,p_actor_session) is distinct from true then
    raise exception 'A fresh active administrator session is required' using errcode='42501';
  end if;
  if p_action not in ('list','get','assign','status','escalate','reply') or
     p_payload is null or jsonb_typeof(p_payload)<>'object' then
    raise exception 'Invalid agent ticket request' using errcode='22023';
  end if;
  perform public.lockvyn_support_ticket_purge_expired();

  if p_action='list' then
    select coalesce(jsonb_agg(jsonb_build_object(
      'id',q.id,'subject',q.subject,'status',q.status,'priority',q.priority,
      'assignedTo',q.assigned_to,'createdAt',q.created_at,'updatedAt',q.updated_at,
      'messageCount',q.message_count
    ) order by case q.status when 'escalated' then 0 when 'open' then 1 when 'in_progress' then 2 else 3 end,
      case q.priority when 'high' then 0 when 'normal' then 1 else 2 end,q.updated_at),'[]'::jsonb)
    into v_tickets
    from (
      select t.id,t.subject,t.status,t.priority,t.assigned_to,t.created_at,t.updated_at,
        (select count(*) from lockvyn_private.support_ticket_messages m where m.ticket_id=t.id) as message_count
      from lockvyn_private.support_tickets t
      where t.expires_at>now()
      order by t.updated_at desc limit 100
    ) q;
    return jsonb_build_object('tickets',v_tickets);
  end if;

  if p_action='get' then
    if p_ticket_id is null then raise exception 'Ticket id is required' using errcode='22023'; end if;
    select * into v_ticket from lockvyn_private.support_tickets where id=p_ticket_id and expires_at>now();
    if not found then raise exception 'Ticket not found' using errcode='P0002'; end if;
    select coalesce(jsonb_agg(jsonb_build_object(
      'id',m.id,'senderRole',m.sender_role,'body',m.body,'createdAt',m.created_at
    ) order by m.created_at,m.id),'[]'::jsonb) into v_messages
    from lockvyn_private.support_ticket_messages m where m.ticket_id=v_ticket.id;
    select jsonb_build_object('filename',a.filename,'contentType',a.content_type,
      'payload',a.payload,'consentedAt',a.consented_at) into v_attachment
    from lockvyn_private.support_ticket_attachments a where a.ticket_id=v_ticket.id;
    return jsonb_build_object('ticket',jsonb_build_object(
      'id',v_ticket.id,'subject',v_ticket.subject,'status',v_ticket.status,'priority',v_ticket.priority,
      'assignedTo',v_ticket.assigned_to,'createdAt',v_ticket.created_at,'updatedAt',v_ticket.updated_at,
      'closedAt',v_ticket.closed_at,'escalatedAt',v_ticket.escalated_at
    ),'messages',v_messages,'diagnostics',coalesce(v_attachment,'null'::jsonb));
  end if;

  if p_fresh_totp is distinct from true then raise exception 'Fresh authenticator verification is required' using errcode='42501'; end if;
  if p_ticket_id is null then raise exception 'Ticket id is required' using errcode='22023'; end if;
  perform pg_advisory_xact_lock(hashtextextended(p_actor::text,808083));
  select count(*) into v_count from lockvyn_private.support_ticket_audit
    where actor_id=p_actor and actor_role='agent' and created_at>now()-interval '1 minute';
  if v_count>=30 then raise exception 'Agent ticket change limit reached' using errcode='54000'; end if;
  select * into v_ticket from lockvyn_private.support_tickets
    where id=p_ticket_id and expires_at>now() for update;
  if not found then raise exception 'Ticket not found' using errcode='P0002'; end if;
  v_previous:=jsonb_build_object('status',v_ticket.status,'priority',v_ticket.priority,'assignedTo',v_ticket.assigned_to);

  if p_action='assign' then
    if jsonb_typeof(p_payload->'assigneeId')='null' then v_assignee:=null;
    elsif jsonb_typeof(p_payload->'assigneeId')='string' then
      v_assignee:=(p_payload->>'assigneeId')::uuid;
      if not exists(select 1 from public.lockvyn_admins where user_id=v_assignee) then
        raise exception 'Assignee is not an administrator' using errcode='22023';
      end if;
    else raise exception 'Assignee is invalid' using errcode='22023'; end if;
    if v_ticket.status='closed' then raise exception 'Closed tickets cannot be assigned' using errcode='55000'; end if;
    update lockvyn_private.support_tickets set assigned_to=v_assignee,
      status=case when v_assignee is not null and status='open' then 'in_progress' else status end,
      updated_at=now() where id=v_ticket.id returning * into v_ticket;
  elsif p_action='status' then
    v_status:=p_payload->>'status';v_priority:=p_payload->>'priority';
    if v_status not in ('open','in_progress','escalated','closed') or
       v_priority not in ('low','normal','high') then
      raise exception 'Ticket status or priority is invalid' using errcode='22023';
    end if;
    update lockvyn_private.support_tickets set status=v_status,priority=v_priority,
      closed_at=case when v_status='closed' then coalesce(closed_at,now()) else null end,
      updated_at=now() where id=v_ticket.id returning * into v_ticket;
  elsif p_action='escalate' then
    if v_ticket.status='closed' then raise exception 'Closed tickets cannot be escalated' using errcode='55000'; end if;
    update lockvyn_private.support_tickets set status='escalated',priority='high',
      escalated_at=now(),updated_at=now() where id=v_ticket.id returning * into v_ticket;
  elsif p_action='reply' then
    v_body:=btrim(coalesce(p_payload->>'body',''));
    if v_ticket.status='closed' or char_length(v_body) not between 1 and 4000 or
       not lockvyn_private.support_text_is_safe(v_body) then
      raise exception 'Ticket is closed or reply text is invalid' using errcode='22023';
    end if;
    insert into lockvyn_private.support_ticket_messages(ticket_id,sender_id,sender_role,body)
      values(v_ticket.id,p_actor,'agent',v_body);
    update lockvyn_private.support_tickets set status=case when status='open' then 'in_progress' else status end,
      updated_at=now() where id=v_ticket.id returning * into v_ticket;
  end if;

  v_new:=jsonb_build_object('status',v_ticket.status,'priority',v_ticket.priority,'assignedTo',v_ticket.assigned_to);
  insert into lockvyn_private.support_ticket_audit(ticket_id,actor_id,actor_session_id,actor_role,action,previous_state,new_state)
    values(v_ticket.id,p_actor,p_actor_session,'agent',case when p_action='reply' then 'agent_reply' else p_action end,v_previous,v_new);
  return jsonb_build_object('ticket',jsonb_build_object(
    'id',v_ticket.id,'subject',v_ticket.subject,'status',v_ticket.status,'priority',v_ticket.priority,
    'assignedTo',v_ticket.assigned_to,'createdAt',v_ticket.created_at,'updatedAt',v_ticket.updated_at,
    'closedAt',v_ticket.closed_at,'escalatedAt',v_ticket.escalated_at
  ));
end;
$$;
revoke all on function public.lockvyn_support_ticket_admin_manage(text,uuid,uuid,text,boolean,uuid,jsonb) from public,anon,authenticated;
grant execute on function public.lockvyn_support_ticket_admin_manage(text,uuid,uuid,text,boolean,uuid,jsonb) to service_role;

comment on table lockvyn_private.support_tickets is
  'Private, in-app support requests. Retained for 180 days; no vault contents or credentials.';
comment on table lockvyn_private.support_ticket_attachments is
  'Explicitly consented, allowlisted diagnostics JSON only; no arbitrary files or raw logs.';
comment on table lockvyn_private.support_ticket_audit is
  'Metadata-only audit; ticket bodies and diagnostic payloads are never copied here.';

commit;
