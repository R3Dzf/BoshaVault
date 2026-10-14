-- Phase 07: bilingual, metadata-only notices with local opt-in alerts.
-- No account, device, vault, or credential records are accepted or returned.
begin;

create schema if not exists lockvyn_private;

create table lockvyn_private.announcements (
  id uuid primary key default gen_random_uuid(),
  title_en text not null default '',
  title_ar text not null default '',
  body_en text not null default '',
  body_ar text not null default '',
  severity text not null default 'info'
    check (severity in ('info','warning','critical')),
  target_platform text not null default 'all'
    check (target_platform in ('all','windows','android')),
  min_version text,
  max_version text,
  notify boolean not null default false,
  status text not null default 'draft'
    check (status in ('draft','published','archived')),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  published_at timestamptz,
  expires_at timestamptz,
  archived_at timestamptz,
  created_by uuid not null,
  updated_by uuid not null,
  constraint announcement_title_lengths check (
    char_length(title_en) <= 120 and char_length(title_ar) <= 120
  ),
  constraint announcement_body_lengths check (
    char_length(body_en) <= 800 and char_length(body_ar) <= 800
  ),
  constraint announcement_target_versions check (
    case
      when target_platform='all' then min_version is null and max_version is null
      when target_platform='windows' then
        (min_version is null or min_version ~ '^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$')
        and (max_version is null or max_version ~ '^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$')
        and case
          when min_version is not null and max_version is not null
            and min_version ~ '^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$'
            and max_version ~ '^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$'
          then string_to_array(min_version,'.')::integer[] <= string_to_array(max_version,'.')::integer[]
          else true
        end
      when target_platform='android' then
        (min_version is null or case when min_version ~ '^(0|[1-9][0-9]{0,9})$' then min_version::numeric <= 2147483647 else false end)
        and (max_version is null or case when max_version ~ '^(0|[1-9][0-9]{0,9})$' then max_version::numeric <= 2147483647 else false end)
        and case
          when min_version is not null and max_version is not null
            and min_version ~ '^(0|[1-9][0-9]{0,9})$'
            and max_version ~ '^(0|[1-9][0-9]{0,9})$'
          then min_version::numeric <= max_version::numeric
          else true
        end
      else false
    end
  )
);
create index announcements_public_feed_idx
  on lockvyn_private.announcements(status,target_platform,published_at desc,expires_at);
alter table lockvyn_private.announcements enable row level security;
alter table lockvyn_private.announcements force row level security;
revoke all on lockvyn_private.announcements from public,anon,authenticated,service_role;

create table lockvyn_private.announcement_audit (
  id bigint generated always as identity primary key,
  announcement_id uuid not null,
  actor_id uuid not null,
  actor_session_id uuid not null,
  action text not null check (action in ('create','update','publish','archive')),
  previous_state jsonb,
  new_state jsonb,
  created_at timestamptz not null default now()
);
create index announcement_audit_actor_time_idx
  on lockvyn_private.announcement_audit(actor_id,created_at desc);
alter table lockvyn_private.announcement_audit enable row level security;
alter table lockvyn_private.announcement_audit force row level security;
revoke all on lockvyn_private.announcement_audit from public,anon,authenticated,service_role;

create or replace function public.lockvyn_admin_announcement_manage(
  p_actor uuid,
  p_actor_session uuid,
  p_actor_aal text,
  p_fresh_totp boolean,
  p_action text,
  p_announcement_id uuid default null,
  p_payload jsonb default '{}'::jsonb
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_row lockvyn_private.announcements%rowtype;
  v_previous jsonb;
  v_new jsonb;
  v_list jsonb;
  v_count integer;
  v_title_en text;
  v_title_ar text;
  v_body_en text;
  v_body_ar text;
  v_severity text;
  v_target text;
  v_min text;
  v_max text;
  v_notify boolean;
  v_expires timestamptz;
begin
  if coalesce(current_setting('request.jwt.claim.role',true),'') <> 'service_role' or
     p_actor is null or p_actor_session is null or
     p_actor_aal is distinct from 'aal2' or
     not exists(select 1 from public.lockvyn_admins where user_id=p_actor) or
     public.lockvyn_auth_session_is_active(p_actor,p_actor_session) is distinct from true
  then
    raise exception 'A fresh active administrator session is required'
      using errcode='42501';
  end if;

  if p_action is null or p_action not in ('list','create','update','publish','archive') then
    raise exception 'Invalid announcement action' using errcode='22023';
  end if;

  if p_action='list' then
    select coalesce(jsonb_agg(items.entry order by items.updated_at desc),'[]'::jsonb)
      into v_list
      from (
        select a.updated_at,
          jsonb_build_object(
            'id',a.id,'titleEn',a.title_en,'titleAr',a.title_ar,
            'bodyEn',a.body_en,'bodyAr',a.body_ar,'severity',a.severity,
            'targetPlatform',a.target_platform,'minVersion',a.min_version,
            'maxVersion',a.max_version,'notify',a.notify,'status',a.status,
            'createdAt',a.created_at,'updatedAt',a.updated_at,
            'publishedAt',a.published_at,'expiresAt',a.expires_at,
            'archivedAt',a.archived_at
          ) as entry
        from lockvyn_private.announcements a
        order by a.updated_at desc
        limit 100
      ) items;
    return jsonb_build_object('announcements',v_list);
  end if;

  if p_fresh_totp is distinct from true then
    raise exception 'Fresh authenticator verification is required' using errcode='42501';
  end if;
  if p_payload is null or jsonb_typeof(p_payload)<>'object' then
    raise exception 'Announcement payload must be an object' using errcode='22023';
  end if;

  perform pg_advisory_xact_lock(hashtextextended(p_actor::text,810717));
  select count(*) into v_count from lockvyn_private.announcement_audit
    where actor_id=p_actor and created_at>now()-interval '60 seconds';
  if v_count>=5 then
    raise exception 'Announcement rate limit reached' using errcode='54000';
  end if;

  if p_action in ('create','update') then
    v_title_en:=btrim(coalesce(p_payload->>'titleEn',''));
    v_title_ar:=btrim(coalesce(p_payload->>'titleAr',''));
    v_body_en:=btrim(coalesce(p_payload->>'bodyEn',''));
    v_body_ar:=btrim(coalesce(p_payload->>'bodyAr',''));
    v_severity:=coalesce(p_payload->>'severity','info');
    v_target:=coalesce(p_payload->>'targetPlatform','all');
    v_min:=nullif(btrim(coalesce(p_payload->>'minVersion','')),'');
    v_max:=nullif(btrim(coalesce(p_payload->>'maxVersion','')),'');
    if coalesce(jsonb_typeof(p_payload->'notify'),'')<>'boolean' then
      raise exception 'Invalid notification preference' using errcode='22023';
    end if;
    v_notify:=(p_payload->>'notify')::boolean;
    if coalesce(p_payload->>'expiresAt','')='' then
      v_expires:=null;
    else
      if length(p_payload->>'expiresAt')>64 then
        raise exception 'Invalid expiry time' using errcode='22023';
      end if;
      v_expires:=(p_payload->>'expiresAt')::timestamptz;
    end if;
    if v_severity not in ('info','warning','critical') or
       v_target not in ('all','windows','android') or
       length(v_title_en)>120 or length(v_title_ar)>120 or
       length(v_body_en)>800 or length(v_body_ar)>800 or
       v_title_en ~* '[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}' or
       v_title_ar ~* '[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}' or
       v_body_en ~* '[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}' or
       v_body_ar ~* '[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}' or
       v_title_en ~* '[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}' or
       v_title_ar ~* '[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}' or
       v_body_en ~* '[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}' or
       v_body_ar ~* '[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}' or
       v_title_en ~ 'eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}' or
       v_title_ar ~ 'eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}' or
       v_body_en ~ 'eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}' or
       v_body_ar ~ 'eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}' or
       v_body_en ~* 'Bearer[[:space:]]+[A-Za-z0-9._~+/-]{20,}' or
       v_body_ar ~* 'Bearer[[:space:]]+[A-Za-z0-9._~+/-]{20,}' or
       v_title_en ~* 'Bearer[[:space:]]+[A-Za-z0-9._~+/-]{20,}' or
       v_title_ar ~* 'Bearer[[:space:]]+[A-Za-z0-9._~+/-]{20,}'
    then
      raise exception 'Invalid or sensitive announcement content' using errcode='22023';
    end if;
    if v_target='all' and (v_min is not null or v_max is not null) then
      raise exception 'Version bounds require a platform target' using errcode='22023';
    end if;
    if v_target='windows' and (
       (v_min is not null and v_min !~ '^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$') or
       (v_max is not null and v_max !~ '^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$')
    ) then raise exception 'Invalid Windows version bound' using errcode='22023'; end if;
     if v_target='android' and (
        (v_min is not null and case when v_min ~ '^(0|[1-9][0-9]{0,9})$' then v_min::numeric>2147483647 else true end) or
        (v_max is not null and case when v_max ~ '^(0|[1-9][0-9]{0,9})$' then v_max::numeric>2147483647 else true end)
     ) then raise exception 'Invalid Android version bound' using errcode='22023'; end if;

    if v_target='windows' and v_min is not null and v_max is not null and
       v_min ~ '^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$' and
       v_max ~ '^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$' and
       string_to_array(v_min,'.')::integer[] > string_to_array(v_max,'.')::integer[] then
      raise exception 'Minimum version exceeds maximum version' using errcode='22023';
    end if;
    if v_target='android' and v_min is not null and v_max is not null then
      if v_min ~ '^(0|[1-9][0-9]{0,9})$' and v_max ~ '^(0|[1-9][0-9]{0,9})$' and
         v_min::numeric > v_max::numeric then
        raise exception 'Minimum version exceeds maximum version' using errcode='22023';
      end if;
    end if;
    if p_action='create' then
      if p_announcement_id is not null then
        raise exception 'New announcement must not include an id' using errcode='22023';
      end if;
      insert into lockvyn_private.announcements(
        title_en,title_ar,body_en,body_ar,severity,target_platform,min_version,max_version,
        notify,status,expires_at,created_by,updated_by
      ) values (
        v_title_en,v_title_ar,v_body_en,v_body_ar,v_severity,v_target,v_min,v_max,
        v_notify,'draft',v_expires,p_actor,p_actor
      ) returning * into v_row;
      v_previous:=null;
    else
      if p_announcement_id is null then
        raise exception 'Announcement id is required' using errcode='22023';
      end if;
      select * into v_row from lockvyn_private.announcements
        where id=p_announcement_id for update;
      if not found then raise exception 'Announcement not found' using errcode='P0002'; end if;
      if v_row.status<>'draft' then
        raise exception 'Only drafts can be edited; archive and create a new notice' using errcode='55000';
      end if;
      v_previous:=jsonb_build_object('status',v_row.status,'targetPlatform',v_row.target_platform,
        'titleEn',v_row.title_en,'titleAr',v_row.title_ar,'notify',v_row.notify,'expiresAt',v_row.expires_at);
      update lockvyn_private.announcements set
        title_en=v_title_en,title_ar=v_title_ar,body_en=v_body_en,body_ar=v_body_ar,
        severity=v_severity,target_platform=v_target,min_version=v_min,max_version=v_max,
        notify=v_notify,expires_at=v_expires,updated_at=now(),updated_by=p_actor
        where id=p_announcement_id returning * into v_row;
    end if;
  elsif p_action in ('publish','archive') then
    if p_announcement_id is null then
      raise exception 'Announcement id is required' using errcode='22023';
    end if;
    select * into v_row from lockvyn_private.announcements
      where id=p_announcement_id for update;
    if not found then raise exception 'Announcement not found' using errcode='P0002'; end if;
    v_previous:=jsonb_build_object('status',v_row.status,'targetPlatform',v_row.target_platform,
      'titleEn',v_row.title_en,'titleAr',v_row.title_ar,'notify',v_row.notify,'expiresAt',v_row.expires_at);
    if p_action='publish' then
      if v_row.status<>'draft' or v_row.title_en='' or v_row.title_ar='' or
         v_row.body_en='' or v_row.body_ar='' or
         (v_row.expires_at is not null and v_row.expires_at<=now()) then
        raise exception 'Complete both languages and use a future expiry before publishing' using errcode='22023';
      end if;
      update lockvyn_private.announcements set status='published',published_at=now(),
        updated_at=now(),updated_by=p_actor where id=p_announcement_id returning * into v_row;
    else
      if v_row.status='archived' then
        raise exception 'Announcement is already archived' using errcode='55000';
      end if;
      update lockvyn_private.announcements set status='archived',archived_at=now(),
        updated_at=now(),updated_by=p_actor where id=p_announcement_id returning * into v_row;
    end if;
  end if;

  v_new:=jsonb_build_object('status',v_row.status,'targetPlatform',v_row.target_platform,
    'titleEn',v_row.title_en,'titleAr',v_row.title_ar,'notify',v_row.notify,'expiresAt',v_row.expires_at);
  insert into lockvyn_private.announcement_audit(
    announcement_id,actor_id,actor_session_id,action,previous_state,new_state
  ) values (v_row.id,p_actor,p_actor_session,p_action,v_previous,v_new);

  return jsonb_build_object(
    'announcement',jsonb_build_object(
      'id',v_row.id,'titleEn',v_row.title_en,'titleAr',v_row.title_ar,
      'bodyEn',v_row.body_en,'bodyAr',v_row.body_ar,'severity',v_row.severity,
      'targetPlatform',v_row.target_platform,'minVersion',v_row.min_version,
      'maxVersion',v_row.max_version,'notify',v_row.notify,'status',v_row.status,
      'createdAt',v_row.created_at,'updatedAt',v_row.updated_at,
      'publishedAt',v_row.published_at,'expiresAt',v_row.expires_at,'archivedAt',v_row.archived_at
    )
  );
end;
$$;
revoke all on function public.lockvyn_admin_announcement_manage(uuid,uuid,text,boolean,text,uuid,jsonb)
  from public,anon,authenticated;
grant execute on function public.lockvyn_admin_announcement_manage(uuid,uuid,text,boolean,text,uuid,jsonb)
  to service_role;

create or replace function public.lockvyn_announcements_feed(
  p_platform text,p_version text,p_locale text
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_result jsonb;
begin
  if p_platform is null or p_version is null or p_locale is null or
     p_platform not in ('windows','android') or p_locale not in ('en','ar') then
    raise exception 'Invalid announcement feed query' using errcode='22023';
  end if;
  if p_platform='windows' and p_version !~ '^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$' then
    raise exception 'Invalid Windows version' using errcode='22023';
  end if;
  if p_platform='android' and
     (p_version !~ '^(0|[1-9][0-9]{0,9})$' or p_version::numeric>2147483647) then
    raise exception 'Invalid Android version' using errcode='22023';
  end if;

  select coalesce(jsonb_agg(items.entry order by items.published_at desc),'[]'::jsonb)
    into v_result
    from (
      select a.published_at,
        jsonb_build_object(
          'id',a.id,
          'title',case when p_locale='ar' then a.title_ar else a.title_en end,
          'body',case when p_locale='ar' then a.body_ar else a.body_en end,
          'severity',a.severity,'notify',a.notify,
          'publishedAt',a.published_at,'expiresAt',a.expires_at
        ) as entry
      from lockvyn_private.announcements a
      where a.status='published' and a.published_at<=now()
        and (a.expires_at is null or a.expires_at>now())
        and a.target_platform in ('all',p_platform)
        and (
          a.min_version is null or a.target_platform='all' or
          (p_platform='windows' and
            string_to_array(p_version,'.')::integer[] >= string_to_array(a.min_version,'.')::integer[]) or
          (p_platform='android' and p_version::numeric >= a.min_version::numeric)
        )
        and (
          a.max_version is null or a.target_platform='all' or
          (p_platform='windows' and
            string_to_array(p_version,'.')::integer[] <= string_to_array(a.max_version,'.')::integer[]) or
          (p_platform='android' and p_version::numeric <= a.max_version::numeric)
        )
      order by a.published_at desc
      limit 10
    ) items;
  return v_result;
end;
$$;
revoke all on function public.lockvyn_announcements_feed(text,text,text)
  from public,anon,authenticated;
grant execute on function public.lockvyn_announcements_feed(text,text,text) to service_role;

comment on table lockvyn_private.announcements is
  'Bilingual metadata-only app notices. No user, device, vault, or credential identifiers.';
comment on table lockvyn_private.announcement_audit is
  'Append-only admin actions for notice drafts and publication; body text is omitted from audit snapshots.';
commit;
