"use strict";
// In-memory integration tests of the actual deployed Edge Function handler.
// No Supabase resources, secrets, account changes, external fetches or mail.
const assert = require("node:assert/strict");
const { readFileSync } = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

(async () => {
  const file = path.join(__dirname,"../supabase/functions/lockvyn-admin/index.ts");
  const authFile = await import("../supabase/functions/lockvyn-admin/access.mjs");
  const supportPolicy=await import("../supabase/functions/lockvyn-admin/support.mjs");
  const releasePolicy=await import("../supabase/functions/lockvyn-admin/release.mjs");
  const announcementPolicy=await import("../supabase/functions/lockvyn-admin/announcements.mjs");
  const ticketPolicy=await import("../supabase/functions/lockvyn-admin/tickets.mjs");
  let source = readFileSync(file,"utf8");
  // The single TS-only type annotation does not affect runtime semantics.
  source = source.replace(/^import .*;\s*$/gm,"")
    .replace("let body: Record<string, unknown>;","let body;")
    .replace("function result(data: unknown, status = 200): Response {",
             "function result(data, status = 200) {")
    .replace("function nonce(): string {", "function nonce() {")
    .replace("async function recordSecuritySignal(actorId: string, signal: string): Promise<void> {",
             "async function recordSecuritySignal(actorId, signal) {");
  assert(!source.includes("let body: Record<string, unknown>;"),
    "Test must evaluate the real Edge Function body");

  const ownerId = "a0000000-0000-4000-8000-000000000001";
  const otherId = "b0000000-0000-4000-8000-000000000002";
  const targetId = "c0000000-0000-4000-8000-000000000003";
  const endpoint = "https://example.supabase.co/functions/v1/lockvyn-admin";
  const origin = "https://lockvyn-admin-ui.r3dzf-studio.workers.dev";
  const resetDestination = origin + "/recover";
  const owner = {id:ownerId,email:"owner@example.invalid",email_confirmed_at:"2026-01-01T00:00:00Z"};
  const nonadmin = {id:otherId,email:"user@example.invalid",email_confirmed_at:"2026-01-01T00:00:00Z"};
  const users = new Map([
    [ownerId,owner],
    [otherId,nonadmin],
    [targetId,{id:targetId,email:"target@example.invalid",email_confirmed_at:"2026-01-01T00:00:00Z"}],
  ]);
  const byToken = new Map();
  const caseId = "d0000000-0000-4000-8000-000000000004";
  let caseState="pending";
  const calls = {list:0,adminLookup:0,mail:0,recoveryClaim:0,factors:0,updated:0,prepared:0,claimed:0,finished:0,sessionCheck:0,securitySignals:[],securityFeed:0,syncOverview:0,syncControl:0,policyReads:0,policySets:0,announcementReads:0,announcementWrites:0,publicAnnouncementReads:0,supportUserCalls:0,supportAdminCalls:0,supportAdminWrites:0};
  const syntheticAnnouncements=[];
  const syntheticTickets=[];
  const syntheticTicketMessages=new Map();
  let lastTicketPayload=null;
  const syntheticPolicies={windows:{platform:"windows",candidateTag:null,minimumVersion:"0.0.0",rolloutPercent:100,updatedAt:"2026-01-01T00:00:00Z"},android:{platform:"android",candidateTag:null,minimumVersion:"0",rolloutPercent:100,updatedAt:"2026-01-01T00:00:00Z"}};
  const syntheticReleases=[
    {tag_name:"windows-v1.22.0",name:"Lockvyn Windows 1.22.0",draft:false,prerelease:false,published_at:"2026-10-01T00:00:00Z",body:"Windows notes <script>not executable</script>",assets:[{id:100,name:"Lockvyn-win-Setup.exe",size:1000,digest:"sha256:"+"a".repeat(64)},{id:101,name:"releases.win.json",size:100,digest:"sha256:"+"b".repeat(64)}]},
    {tag_name:"android-v1.14.1",name:"Lockvyn Android 1.14.1",draft:false,prerelease:false,published_at:"2026-10-02T00:00:00Z",body:"Android notes",assets:[{id:200,name:"android-update.json",size:300,digest:"sha256:"+"c".repeat(64)},{id:201,name:"Lockvyn-Android.apk",size:2000,digest:"sha256:"+"d".repeat(64)},{id:202,name:"SHA256SUMS-Android.txt",size:80,digest:"sha256:"+"e".repeat(64)}]}
  ];
  async function fakeFetch(url){
    const parsed=new URL(url);if(parsed.hostname!=="api.github.com")throw new Error("Unexpected synthetic release host");
    let payload;
    if(parsed.pathname.endsWith("/releases"))payload=syntheticReleases;
    else if(parsed.pathname.includes("/check-runs"))payload={check_runs:[{name:"Build Lockvyn",conclusion:"success",completed_at:"2026-10-02T00:00:00Z"}]};
    else if(parsed.pathname.endsWith("/releases/assets/200"))payload={schema:1,packageName:"com.lockvyn.app",tag:"android-v1.14.1",versionName:"1.14.1",versionCode:21,minSdk:28,signerSha256:"f".repeat(64),sha256:"d".repeat(64),url:"https://github.com/R3Dzf/BoshaVault/releases/download/android-v1.14.1/Lockvyn-Android.apk"};
    else throw new Error("Unexpected synthetic GitHub API path "+parsed.pathname);
    const response=new Response(JSON.stringify(payload),{status:200,headers:{"Content-Type":"application/json"}});
    Object.defineProperty(response,"url",{value:parsed.href});return response;
  }
  let announcementFeedUnavailable=false;
  const revokedSessionIds=new Set();
  let sessionLookupError=false;
  function token(user,aal="aal2",role="authenticated",sessionId=user.id) {
    const payload = Buffer.from(JSON.stringify({sub:user.id,aal,role,session_id:sessionId,
      amr:aal==="aal2"?[{method:"totp",timestamp:Math.floor(Date.now()/1000)}]:[]
    })).toString("base64url");
    const value = "aGVhZGVy."+payload+".c2lnbmF0dXJl";
    byToken.set(value,user);
    return value;
  }
  const ownerAal2 = token(owner);
  const ownerAal1 = token(owner,"aal1");
  const nonAdminAal2 = token(nonadmin);
  const otherRole = token(owner,"aal2","anon");

  const service = {
    auth:{
      getUser: async jwt => ({
        data:{user:byToken.get(jwt)||null},
        error:byToken.has(jwt)?null:{message:"Invalid token"},
      }),
      admin:{
        listUsers:async()=>{
          calls.list++;
          return {data:{total:3,users:[owner,nonadmin]},error:null};
        },
        mfa:{
          listFactors:async({userId})=>{
            calls.factors++;
            return {data:{factors:userId===ownerId?[{status:"verified"}]:[]},error:null};
          }
        },
        updateUserById:async (id,options)=>{
          assert.equal(id,targetId);assert.equal(options.ban_duration,"24h");
          calls.updated++;return {data:{},error:null};
        },
        getUserById:async id=>{
          calls.adminLookup++;
          return {data:{user:users.get(id)||null},error:null};
        }
      },
      resetPasswordForEmail:async (email,options)=>{
        calls.mail++;
        assert.equal(email,"target@example.invalid");
        assert.equal(options.redirectTo,resetDestination);
        return {error:null};
      }
    },
    from:table=>{
      assert(["lockvyn_admins","lockvyn_admin_roles"].includes(table));
      return {
        select:()=>({
          eq:(_,id)=>({
            maybeSingle:async()=>({
              data:table==="lockvyn_admins"
                ?(id===ownerId?{user_id:ownerId}:null)
                :(id===ownerId?{role:"owner"}:null),
              error:null,
            })
          })
        })
      };
    },
    rpc:async(name,args)=>{
      if(name==="lockvyn_auth_session_is_active"){
        calls.sessionCheck++;
        assert.equal(args.p_user_id,args.p_session_id);
        if(sessionLookupError)return {data:null,error:{message:"synthetic lookup failure"}};
        return {data:!revokedSessionIds.has(args.p_session_id),error:null};
      }
      if(name==="lockvyn_support_ticket_user_manage"){
        calls.supportUserCalls++;assert.equal(args.p_actor_session,args.p_actor);
        if(args.p_action==="create"){
          lastTicketPayload=args.p_payload;
          const ticket={id:"f1000000-0000-4000-8000-000000000001",subject:args.p_payload.subject,status:"open",priority:"normal",requester_id:args.p_actor};
          syntheticTickets.push(ticket);syntheticTicketMessages.set(ticket.id,[{senderRole:"user",body:args.p_payload.body}]);
          return {data:{ticket:{id:ticket.id,subject:ticket.subject,status:ticket.status,priority:ticket.priority}},error:null};
        }
        if(args.p_action==="list")return {data:{tickets:syntheticTickets.filter(t=>t.requester_id===args.p_actor).map(({requester_id,...t})=>t)},error:null};
        const ticket=syntheticTickets.find(t=>t.id===args.p_ticket_id&&t.requester_id===args.p_actor);
        if(!ticket)return {data:null,error:{code:"P0002"}};
        if(args.p_action==="get")return {data:{ticket:{id:ticket.id,subject:ticket.subject,status:ticket.status,priority:ticket.priority},messages:syntheticTicketMessages.get(ticket.id),diagnostics:null},error:null};
        if(args.p_action==="reply"){syntheticTicketMessages.get(ticket.id).push({senderRole:"user",body:args.p_payload.body});ticket.status="open";}
        if(args.p_action==="close")ticket.status="closed";if(args.p_action==="reopen")ticket.status="open";
        return {data:{ticket:{id:ticket.id,subject:ticket.subject,status:ticket.status,priority:ticket.priority}},error:null};
      }
      if(name==="lockvyn_support_ticket_admin_manage"){
        calls.supportAdminCalls++;assert.equal(args.p_actor,ownerId);assert.equal(args.p_actor_session,ownerId);assert.equal(args.p_actor_aal,"aal2");
        if(args.p_action==="list")return {data:{tickets:syntheticTickets.map(({requester_id,...t})=>t)},error:null};
        const ticket=syntheticTickets.find(t=>t.id===args.p_ticket_id);if(!ticket)return {data:null,error:{code:"P0002"}};
        assert.equal(args.p_fresh_totp,true);calls.supportAdminWrites++;
        if(args.p_action==="assign")ticket.assigned_to=args.p_payload.assigneeId;
        if(args.p_action==="status"){ticket.status=args.p_payload.status;ticket.priority=args.p_payload.priority;}
        if(args.p_action==="escalate"){ticket.status="escalated";ticket.priority="high";}
        if(args.p_action==="reply")syntheticTicketMessages.get(ticket.id).push({senderRole:"agent",body:args.p_payload.body});
        if(args.p_action==="get")return {data:{ticket:{id:ticket.id,subject:ticket.subject,status:ticket.status,priority:ticket.priority,assignedTo:ticket.assigned_to},messages:syntheticTicketMessages.get(ticket.id),diagnostics:null},error:null};
        return {data:{ticket:{id:ticket.id,subject:ticket.subject,status:ticket.status,priority:ticket.priority,assignedTo:ticket.assigned_to}},error:null};
      }
      if(name==="lockvyn_announcements_feed"){
        calls.publicAnnouncementReads++;
        assert.equal(args.p_platform,"windows");assert.equal(args.p_version,"1.21.0");assert.equal(args.p_locale,"ar");
        assert.equal(Object.keys(args).sort().join(","),"p_locale,p_platform,p_version","feed carries only platform, version and locale");
        if(announcementFeedUnavailable)return {data:null,error:{message:"synthetic offline RPC"}};
        return {data:[{id:"e1000000-0000-4000-8000-000000000001",title:"صيانة",body:"تنبيه صيانة",severity:"warning",notify:false,publishedAt:"2026-01-01T00:00:00Z",expiresAt:null}],error:null};
      }
      if(name==="lockvyn_admin_announcement_manage"){
        assert.equal(args.p_actor,ownerId);assert.equal(args.p_actor_session,ownerId);
        assert.equal(args.p_actor_aal,"aal2");
        calls.announcementReads+=args.p_action==="list"?1:0;
        if(args.p_action==="list")return {data:{announcements:syntheticAnnouncements},error:null};
        assert.equal(args.p_fresh_totp,true);
        calls.announcementWrites++;
        let notice=syntheticAnnouncements.find(x=>x.id===args.p_announcement_id);
        if(args.p_action==="create"){
          notice={...args.p_payload,id:"e1000000-0000-4000-8000-000000000001",status:"draft"};
          syntheticAnnouncements.push(notice);
        }else if(!notice)return {data:null,error:{code:"P0002"}};
        else if(args.p_action==="update")Object.assign(notice,args.p_payload);
        else if(args.p_action==="publish")notice.status="published";
        else if(args.p_action==="archive")notice.status="archived";
        return {data:{announcement:notice},error:null};
      }
      if(name==="lockvyn_release_policy_public"){
        calls.policyReads++;
        return {data:syntheticPolicies[args.p_platform]||null,error:null};
      }
      if(name==="lockvyn_admin_set_release_policy"){
        calls.policySets++;assert.equal(args.p_actor,ownerId);assert.equal(args.p_actor_session,ownerId);
        assert.equal(args.p_actor_aal,"aal2");assert.equal(args.p_fresh_totp,true);
        syntheticPolicies[args.p_platform]={platform:args.p_platform,candidateTag:args.p_candidate_tag,minimumVersion:args.p_minimum_version,rolloutPercent:args.p_rollout_percent,updatedAt:"2026-10-13T00:00:00Z"};
        return {data:syntheticPolicies[args.p_platform],error:null};
      }
      if(name==="lockvyn_admin_record_security_signal"){
        assert(["admin_step_up_denied","admin_allowlist_denied","admin_inactive_session"].includes(args.p_signal));
        calls.securitySignals.push({actor:args.p_actor,signal:args.p_signal});
        return {data:true,error:null};
      }
      assert.equal(args.p_actor,ownerId);
      if(name==="lockvyn_admin_security_feed"){
        calls.securityFeed++;
        assert.equal(args.p_actor_session,ownerId);
        assert.equal(args.p_actor_aal,"aal2");
        return {data:{events:[],alerts:{adminStepUpDenials15m:0,adminAllowlistDenials15m:0,
          repeatedDeniedActors15m:0,newCloudSessions24h:0,failedSupportActions24h:0}},error:null};
      }
      if(name==="lockvyn_admin_cloud_sync_overview"){
        calls.syncOverview++;
        assert.equal(args.p_actor,ownerId);
        assert.equal(args.p_actor_session,ownerId);
        assert.equal(args.p_actor_aal,"aal2");
        assert.equal(args.p_target,targetId);
        assert.equal(args.p_platform,"windows");
        assert.equal(args.p_outcome,"failure");
        assert.equal(args.p_since_days,7);
        assert.equal(args.p_device_query,"laptop");
        return {data:{devices:[{device_id:"d0000000-0000-4000-8000-000000000005",
          last_sync_outcome:"failure"}],suspended:false,suspendedAt:null,
          snapshotCount:2,ciphertextBytes:2048,quotaBytes:67108864,
          remainingBytes:67106816,maxSnapshots:64},error:null};
      }
      if(name==="lockvyn_admin_set_cloud_sync_suspension"){
        calls.syncControl++;
        assert.equal(args.p_actor,ownerId);
        assert.equal(args.p_actor_session,ownerId);
        assert.equal(args.p_actor_aal,"aal2");
        assert.equal(args.p_fresh_totp,true);
        assert.equal(args.p_target,targetId);
        assert.equal(args.p_suspend,true);
        assert.equal(args.p_confirm_email,"target@example.invalid");
        return {data:true,error:null};
      }
      if(name==="lockvyn_admin_directory"){
        calls.list++;
        const full=[
          {id:ownerId,email:"owner@example.invalid",confirmed:true,mfa:true,isSelf:true},
          {id:otherId,email:"user@example.invalid",confirmed:true,mfa:false,isSelf:false},
          {id:targetId,email:"target@example.invalid",confirmed:true,mfa:false,isSelf:false},
        ];
        const q=args.p_search.toLowerCase();
        const rows=full.filter(u=>u.email.includes(q) &&
          (args.p_email_state==="all" || (args.p_email_state==="verified" && u.confirmed) ||
             (args.p_email_state==="pending" && !u.confirmed)) &&
          (args.p_mfa_state==="all" || (args.p_mfa_state==="enabled" && u.mfa) ||
             (args.p_mfa_state==="disabled" && !u.mfa)));
        return {data:{page:args.p_page,perPage:30,total:full.length,matching:rows.length,
          stats:{verified:3,mfaEnabled:1,active30Days:1},users:rows},error:null};
      }
      if(name==="lockvyn_admin_support_prepare"){
        calls.prepared++;return {data:caseId,error:null};
      }
      if(name==="lockvyn_admin_support_claim"){
        calls.claimed++;
        if(caseState!=="pending"||args.p_email!=="target@example.invalid")return {data:false,error:null};
        caseState="running";return {data:true,error:null};
      }
      if(name==="lockvyn_admin_support_finish"){
        calls.finished++;assert.equal(caseState,"running");caseState=args.p_status;
        return {data:true,error:null};
      }
      if(name==="lockvyn_admin_support_recent")return {data:[],error:null};
      assert.equal(name,"lockvyn_claim_recovery");
      assert.equal(args.p_target,targetId);
      calls.recoveryClaim++;
      return {data:true,error:null};
    }
  };
  let handler;
  const context={
    createClient:()=>service,html:()=>"<html></html>",
    securityHtml:()=>"<html><body>user-owned logout?scope=global</body></html>",
    verifiedUserGate:authFile.verifiedUserGate,
    adminMembershipGate:authFile.adminMembershipGate,
    validSupportRequest:supportPolicy.validSupportRequest,
    validCaseConfirmation:supportPolicy.validCaseConfirmation,
    freshVerifiedTotp:supportPolicy.freshVerifiedTotp,
    safeTarget:supportPolicy.safeTarget,
    isAutomaticAction:supportPolicy.isAutomaticAction,
    cloudLoginStatus:supportPolicy.cloudLoginStatus,
    validCandidateTag:releasePolicy.validCandidateTag,
    validateReleasePolicy:releasePolicy.validateReleasePolicy,
    compareReleaseVersions:releasePolicy.compareReleaseVersions,
    announcementInputError:announcementPolicy.announcementInputError,
    announcementQueryError:announcementPolicy.announcementQueryError,
    parseUserTicketRequest:ticketPolicy.parseUserTicketRequest,
    parseAdminTicketRequest:ticketPolicy.parseAdminTicketRequest,
    isAdminTicketAction:ticketPolicy.isAdminTicketAction,
    Deno:{
      env:{get:name=>({
        SUPABASE_URL:"https://example.supabase.co",
        SUPABASE_ANON_KEY:"test-public",
        SUPABASE_SERVICE_ROLE_KEY:"test-service-only",
      })[name]},
      serve:fn=>{handler=fn}
    },
    Request,Response,URL,JSON,TextEncoder,AbortSignal,fetch:fakeFetch,
    crypto:globalThis.crypto, btoa:globalThis.btoa, atob:globalThis.atob
  };
  vm.runInNewContext(source,context,{filename:file,timeout:2000});
  assert.equal(typeof handler,"function");
  async function call({jwt,action="list",id=targetId,page=1,search="",emailState="all",mfaState="all",limit=50,originHeader=origin,contentType="application/json",method="POST",kind,reason,caseId:requestCaseId,confirmEmail,platform,outcome,sinceDays,deviceQuery,suspend,reasonCode,candidateTag,minimumVersion,rolloutPercent,confirmTag,announcement,announcementId,confirmId}={}){
    const headers={Origin:originHeader,"Content-Type":contentType};
    if(jwt)headers.Authorization="Bearer "+jwt;
    const request=new Request(endpoint,{method,headers,
      body:method==="POST"?JSON.stringify({action,id,page,search,emailState,mfaState,limit,kind,reason,caseId:requestCaseId,confirmEmail,platform,outcome,sinceDays,deviceQuery,suspend,reasonCode,candidateTag,minimumVersion,rolloutPercent,confirmTag,announcement,announcementId,confirmId}):undefined});
    const res=await handler(request);
    return {status:res.status,data:await res.json()};
  }
  async function ticketCall({jwt,body,originHeader}={}) {
    const headers={"Content-Type":"application/json"};
    if(jwt)headers.Authorization="Bearer "+jwt;
    if(originHeader!==undefined)headers.Origin=originHeader;
    const request=new Request(endpoint+"/tickets",{method:"POST",headers,body:JSON.stringify(body??{})});
    const res=await handler(request);return {status:res.status,data:await res.json()};
  }
  async function deny(args,code,name) {
    const before={...calls};
    const {status,data}=await call(args);
    assert.equal(status,code,name);
    assert.equal(data.users,undefined,name+" must not leak users");
    assert.equal(calls.list,before.list,name+" must not enumerate users");
    assert.equal(calls.adminLookup,before.adminLookup,name+" must not lookup recovery account");
    assert.equal(calls.recoveryClaim,before.recoveryClaim,name+" must not claim recovery");
    assert.equal(calls.mail,before.mail,name+" must not send email");
  }
  await deny({jwt:ownerAal1},403,"real handler rejects authenticated AAL1 owner");
  assert.equal(calls.securitySignals[0].signal,"admin_step_up_denied",
    "verified AAL1 denials create only a bounded metadata signal");
  await deny({jwt:nonAdminAal2},403,"real handler rejects AAL2 non-admin");
  assert.equal(calls.securitySignals[1].signal,"admin_allowlist_denied");
  const signalsBeforeUntrusted=calls.securitySignals.length;
  await deny({jwt:otherRole},401,"real handler rejects unexpected role");
  assert.equal(calls.securitySignals.length,signalsBeforeUntrusted,
    "invalid identity/role claims are not copied into telemetry");
  await deny({jwt:"not-a-real-token"},401,"real handler rejects invalid token");
  await deny({},401,"real handler rejects anonymous list");
  await deny({jwt:ownerAal2,originHeader:"https://attacker.invalid"},403,
    "real handler rejects cross-origin admin");
  await deny({jwt:ownerAal1,action:"recover"},403,
    "real handler rejects AAL1 recovery");
  await deny({jwt:nonAdminAal2,action:"recover"},403,
    "real handler rejects non-admin recovery");
  const securityPage=await handler(new Request(endpoint+"/security",{method:"GET"}));
  assert.equal(securityPage.status,200,"Public user-owned security page");
  assert.match(await securityPage.text(),/user-owned logout\?scope=global/);
  assert.match(securityPage.headers.get("content-security-policy"),/script-src 'nonce-/);
  const good=await call({jwt:ownerAal2});
  assert.equal(good.status,200,"AAL2 admin is allowed");
  assert.equal(good.data.total,3);
  assert.equal(good.data.users[0].mfa,true);
  assert.equal(good.data.users[1].mfa,false);
  assert.equal(good.data.matching,3);
  assert.equal(good.data.stats.mfaEnabled,1);
  assert.equal(calls.list,1);
  const feed=await call({jwt:ownerAal2,action:"securityEvents"});
  assert.equal(feed.status,200,"AAL2 administrator can read the security feed");
  assert.equal(feed.data.events.length,0);
  assert.equal(calls.securityFeed,1);
  const syncInventory=await call({jwt:ownerAal2,action:"deviceList",id:targetId,
    platform:"windows",outcome:"failure",sinceDays:7,deviceQuery:"laptop"});
  assert.equal(syncInventory.status,200,"AAL2 administrator can load filtered sync activity");
  assert.equal(syncInventory.data.devices[0].last_sync_outcome,"failure");
  assert.equal(syncInventory.data.snapshotCount,2);
  assert.equal(calls.syncOverview,1);
  const badSyncFilter=await call({jwt:ownerAal2,action:"deviceList",id:targetId,
    platform:"bogus",outcome:"failure",sinceDays:7,deviceQuery:"laptop"});
  assert.equal(badSyncFilter.status,400,"Invalid sync inventory filters are rejected");
  assert.equal(calls.syncOverview,1,"Invalid filters never reach the privileged RPC");
  const badSyncEmail=await call({jwt:ownerAal2,action:"syncControl",id:targetId,
    suspend:true,reasonCode:"support_review",confirmEmail:"wrong@example.invalid"});
  assert.equal(badSyncEmail.status,409,"Typed email is checked before changing sync");
  assert.equal(calls.syncControl,0);
  const syncPause=await call({jwt:ownerAal2,action:"syncControl",id:targetId,
    suspend:true,reasonCode:"support_review",confirmEmail:"target@example.invalid"});
  assert.equal(syncPause.status,200,"Fresh TOTP admin can pause cloud sync");
  assert.equal(syncPause.data.suspended,true);
  assert.match(syncPause.data.warning,/Offline vault data and encrypted cloud snapshots are preserved/);
  assert.equal(calls.syncControl,1);
  const invalidFeed=await call({jwt:ownerAal2,action:"securityEvents",limit:101});
  assert.equal(invalidFeed.status,400,"Security feed page size is bounded");
  assert.equal(calls.securityFeed,1,"Invalid feed requests must not query audit data");
  const beforeSessionDenial=calls.list;
  revokedSessionIds.add(ownerId);
  const revoked=await call({jwt:ownerAal2});
  assert.equal(revoked.status,401,"revoked session cannot use privileged admin API");
  assert.match(revoked.data.error,/session/i);
  assert.equal(calls.list,beforeSessionDenial,"revoked JWT must not load admin data");
  assert.equal(calls.securitySignals.at(-1).signal,"admin_inactive_session");
  revokedSessionIds.delete(ownerId);
  sessionLookupError=true;
  const lookupFailure=await call({jwt:ownerAal2});
  assert.equal(lookupFailure.status,503,"session checker failure must fail closed");
  assert.equal(calls.list,beforeSessionDenial,"session lookup errors must not load admin data");
  sessionLookupError=false;
  const noSession=token(owner,"aal2","authenticated",null);
  const missingSession=await call({jwt:noSession});
  assert.equal(missingSession.status,401,"JWT without a valid session_id is rejected");
  const filtered=await call({jwt:ownerAal2,search:"OWNER"});
  assert.equal(filtered.status,200);
  assert.equal(filtered.data.matching,1);
  assert.equal(filtered.data.users[0].id,ownerId);
  const second=await call({jwt:ownerAal2,mfaState:"disabled"});
  assert.equal(second.status,200);
  assert.equal(second.data.matching,2);
  const rejected=await call({jwt:ownerAal2,emailState:"invalid"});
  assert.equal(rejected.status,400);
  assert.equal(rejected.data.users,undefined);
  const recovery=await call({jwt:ownerAal2,action:"recover"});
  assert.equal(recovery.status,200);
  assert.equal(recovery.data.sent,true);
  assert.equal(calls.mail,1,"only synthetic mock email sent");
  assert.equal(calls.recoveryClaim,1,"recovery is audited/rate-limited through mock RPC");
  assert.equal(calls.adminLookup,3,"Recovery plus both cloud sync confirmations look up only synthetic targets");
  // Phase 02: dangerous operations must be rejected before any Auth mutation.
  const beforeSupport={...calls};
  const deniedSupport=await call({jwt:ownerAal1,action:"supportPrepare",kind:"suspend_24h",reason:"lost_device"});
  assert.equal(deniedSupport.status,403);
  assert.equal(calls.prepared,beforeSupport.prepared);
  const denyNonAdmin=await call({jwt:nonAdminAal2,action:"supportPrepare",kind:"suspend_24h",reason:"lost_device"});
  assert.equal(denyNonAdmin.status,403);
  const self=await call({jwt:ownerAal2,action:"supportPrepare",id:ownerId,kind:"suspend_24h",reason:"lost_device"});
  assert.equal(self.status,403,"Owner cannot suspend own account");
  const invalidKind=await call({jwt:ownerAal2,action:"supportPrepare",kind:"delete_now",reason:"lost_device"});
  assert.equal(invalidKind.status,400);
  const statusBefore=await call({jwt:ownerAal2,action:"supportStatus"});
  assert.equal(statusBefore.status,200);
  assert.equal(statusBefore.data.cloudLogin.state,"active");
  assert.equal(statusBefore.data.cloudLogin.until,null);
  const deniedStatus=await call({jwt:nonAdminAal2,action:"supportStatus"});
  assert.equal(deniedStatus.status,403);
  const selfStatus=await call({jwt:ownerAal2,action:"supportStatus",id:ownerId});
  assert.equal(selfStatus.status,403);
  const badStatus=await call({jwt:ownerAal2,action:"supportStatus",id:"invalid"});
  assert.equal(badStatus.status,400);
  const requested=await call({jwt:ownerAal2,action:"supportPrepare",kind:"suspend_24h",reason:"lost_device"});
  assert.equal(requested.status,200);assert.equal(requested.data.caseId,caseId);
  assert.equal(calls.updated,0,"Preparing support must never change Auth");
  const wrong=await call({jwt:ownerAal2,action:"supportExecute",kind:"suspend_24h",reason:"lost_device",caseId:caseId,confirmEmail:"wrong@example.invalid"});
  assert.equal(wrong.status,409);
  assert.equal(calls.updated,0,"Wrong account confirmation cannot update Auth");
  const execute=await call({jwt:ownerAal2,action:"supportExecute",kind:"suspend_24h",reason:"lost_device",caseId:caseId,confirmEmail:"target@example.invalid"});
  assert.equal(execute.status,200);
  assert.equal(execute.data.status,"completed");
  assert.equal(calls.updated,1);
  assert.equal(calls.finished,1);
  const replay=await call({jwt:ownerAal2,action:"supportExecute",kind:"suspend_24h",reason:"lost_device",caseId:caseId,confirmEmail:"target@example.invalid"});
  assert.equal(replay.status,409,"Confirm must be single use");
  assert.equal(calls.updated,1);
  console.log("PASS Phase 02: owner/nonadmin denied, no changes before confirmation, one-time Auth action audited");
  const publicPolicy=await handler(new Request(endpoint+"/release-policy?platform=android",{method:"GET"}));
  const publicData=await publicPolicy.json();
  assert.equal(publicPolicy.status,200,"sanitized platform policy is public for native update checks");
  assert.equal(publicPolicy.headers.get("access-control-allow-origin"),"*");
  assert.equal(publicData.platform,"android");
  assert.equal(publicData.rolloutPercent,100);
  assert.equal(publicData.actorId,undefined,"public policy contains no actor identity");
  const invalidPublic=await handler(new Request(endpoint+"/release-policy?platform=macos",{method:"GET"}));
  assert.equal(invalidPublic.status,400,"public policy rejects unknown platforms");
  const catalog=await call({jwt:ownerAal2,action:"releaseManager"});
  assert.equal(catalog.status,200,"AAL2 admin can load release metadata");
  assert.equal(catalog.data.releases.length,2);
  assert.equal(catalog.data.releases[0].checks.state,"success");
  assert.match(catalog.data.releases[1].androidSignature,/fingerprint published/);
  assert.equal(catalog.data.releases[1].androidManifest.signerSha256,"f".repeat(64));
  assert.match(catalog.data.releases[0].windowsSignature,/Unsigned/);
  assert.equal(catalog.data.releases[0].changelog,"Windows notes <script>not executable</script>");
  assert.equal(calls.policyReads,3,"public endpoint and both admin policies are read");
  const badReleaseConfirmation=await call({jwt:ownerAal2,action:"releasePolicySet",platform:"windows",candidateTag:null,minimumVersion:"0.0.0",rolloutPercent:0,reasonCode:"rollback",confirmTag:"wrong"});
  assert.equal(badReleaseConfirmation.status,400);
  assert.equal(calls.policySets,0,"typed release confirmation is required");
  const releaseRollback=await call({jwt:ownerAal2,action:"releasePolicySet",platform:"windows",candidateTag:null,minimumVersion:"0.0.0",rolloutPercent:0,reasonCode:"rollback",confirmTag:"LATEST"});
  assert.equal(releaseRollback.status,200);
  assert.equal(releaseRollback.data.policy.rolloutPercent,0);
  assert.equal(releaseRollback.data.rollbackSafe,true);
  assert.equal(calls.policySets,1);
  const publicFeed=await handler(new Request(endpoint+"/announcements?platform=windows&version=1.21.0&locale=ar",{method:"GET"}));
  const publicAnnouncements=await publicFeed.json();
  assert.equal(publicFeed.status,200,"native clients can read the sanitized announcement feed");
  assert.equal(publicFeed.headers.get("access-control-allow-origin"),"*");
  assert.match(publicFeed.headers.get("cache-control"),/max-age=60/);
  assert.equal(publicAnnouncements[0].title,"صيانة");
  assert.equal(publicAnnouncements[0].actorId,undefined,"public feed contains no actor identity");
  assert.equal(calls.publicAnnouncementReads,1);
  const badAnnouncementFeed=await handler(new Request(endpoint+"/announcements?platform=windows&version=1.2&locale=en",{method:"GET"}));
  assert.equal(badAnnouncementFeed.status,400,"public feed validates platform version bounds");
  assert.equal(calls.publicAnnouncementReads,1,"invalid feed query never reaches the database");
  announcementFeedUnavailable=true;
  const unavailableFeed=await handler(new Request(endpoint+"/announcements?platform=windows&version=1.21.0&locale=ar",{method:"GET"}));
  assert.equal(unavailableFeed.status,503,"offline feed failure returns a safe retry response");
  assert.equal(unavailableFeed.headers.get("cache-control"),"no-store","failed feed is not cached");
  announcementFeedUnavailable=false;
  const reconnectedFeed=await handler(new Request(endpoint+"/announcements?platform=windows&version=1.21.0&locale=ar",{method:"GET"}));
  const recoveredItems=await reconnectedFeed.json();
  assert.equal(reconnectedFeed.status,200,"the same anonymous feed can be fetched after reconnect");
  assert.equal(recoveredItems[0].id,publicAnnouncements[0].id,"retry returns the same item without server read receipts");
  assert.equal(calls.publicAnnouncementReads,3,"offline retry carries no device or account state");


  const notice={titleEn:"Maintenance",titleAr:"صيانة",bodyEn:"Sync pauses briefly.",
    bodyAr:"سيتوقف التزامن لفترة قصيرة.",severity:"warning",targetPlatform:"windows",
    minVersion:"1.20.0",maxVersion:"1.22.0",notify:true,expiresAt:null};
  const readsBefore=calls.announcementReads;
  const announcementList=await call({jwt:ownerAal2,action:"announcements"});
  assert.equal(announcementList.status,200,"AAL2 admin can load announcement drafts");
  assert.equal(Array.isArray(announcementList.data.announcements),true);
  assert.equal(calls.announcementReads,readsBefore+1);
  const deniedAnnouncement=await call({jwt:ownerAal1,action:"announcementSave",announcement:notice});
  assert.equal(deniedAnnouncement.status,403,"AAL1 cannot save a notice");
  assert.equal(calls.announcementWrites,0,"AAL1 cannot reach the announcement mutation RPC");
  const invalidAnnouncement=await call({jwt:ownerAal2,action:"announcementSave",
    announcement:{...notice,bodyEn:"private@example.invalid"}});
  assert.equal(invalidAnnouncement.status,400,"sensitive content is rejected");
  assert.equal(calls.announcementWrites,0,"invalid content never reaches the database");
  const savedAnnouncement=await call({jwt:ownerAal2,action:"announcementSave",announcement:notice});
  assert.equal(savedAnnouncement.status,200);
  assert.equal(savedAnnouncement.data.announcement.status,"draft");
  const noticeId=savedAnnouncement.data.announcement.id;
  const badNoticeConfirm=await call({jwt:ownerAal2,action:"announcementPublish",id:noticeId,confirmId:"wrong"});
  assert.equal(badNoticeConfirm.status,400,"publishing requires typed notice ID");
  assert.equal(calls.announcementWrites,1,"bad confirmation did not mutate");
  const publishedNotice=await call({jwt:ownerAal2,action:"announcementPublish",id:noticeId,confirmId:noticeId});
  assert.equal(publishedNotice.status,200);
  assert.equal(publishedNotice.data.announcement.status,"published");
  const archivedNotice=await call({jwt:ownerAal2,action:"announcementArchive",id:noticeId,confirmId:noticeId});
  assert.equal(archivedNotice.status,200);
  assert.equal(archivedNotice.data.announcement.status,"archived");
  assert.equal(calls.announcementWrites,3);
  console.log("PASS Phase 07: synthetic bilingual notice feed, version filtering, AAL2 writes and typed publish/archive");

   const ticketPreflight=await handler(new Request(endpoint+"/tickets",{method:"OPTIONS",headers:{Origin:origin}}));
   assert.equal(ticketPreflight.status,204,"Admin browser ticket endpoint allows its same-origin preflight");
   assert.equal(ticketPreflight.headers.get("access-control-allow-origin"),origin);
   const badTicketOrigin=await ticketCall({jwt:ownerAal1,originHeader:"https://attacker.invalid",body:{action:"ticketList"}});
   assert.equal(badTicketOrigin.status,403,"Ticket endpoint rejects cross-origin browser calls");
   const unauthTicket=await ticketCall({body:{action:"ticketList"}});
   assert.equal(unauthTicket.status,401,"Ticket endpoint rejects anonymous requests");
   const badConsent=await ticketCall({jwt:ownerAal1,body:{action:"ticketCreate",ticket:{
     subject:"Sync issue",body:"Please help.",diagnosticConsent:false,
     diagnostics:{platform:"windows",appVersion:"3.1.0"}}}});
   assert.equal(badConsent.status,400,"Diagnostics require explicit consent");
   assert.equal(calls.supportUserCalls,0,"Invalid ticket request never reaches privileged RPC");
   const arbitraryFile=await ticketCall({jwt:ownerAal1,body:{action:"ticketCreate",ticket:{
     subject:"File attempt",body:"No arbitrary file uploads.",diagnosticConsent:false,diagnostics:null,
     attachments:[{filename:"vault.json",content:"synthetic"}]}}});
   assert.equal(arbitraryFile.status,400,"Arbitrary attachments are explicitly rejected");
   const createdTicket=await ticketCall({jwt:ownerAal1,body:{action:"ticketCreate",ticket:{
     subject:"Sync issue",body:"Bearer abcdefghijklmnopqrstuvwxyz password=hunter2",
     diagnosticConsent:true,diagnostics:{platform:"windows",appVersion:"3.1.0",osVersion:"Windows 11",errorCode:"HTTP_503"}}}});
   assert.equal(createdTicket.status,200,"Verified AAL1 users can create tickets");
   const syntheticTicketId=createdTicket.data.ticket.id;
   assert.equal(lastTicketPayload.body.includes("hunter2"),false,"Credentials are redacted before storage");
   assert.equal(lastTicketPayload.body.includes("abcdefghijklmnopqrstuvwxyz"),false,"Bearer tokens are redacted before storage");
   assert.equal(createdTicket.data.ticket.requester_id,undefined,"Public response omits account identity");
   const deniedTicketRead=await ticketCall({jwt:nonAdminAal2,body:{action:"ticketGet",ticketId:syntheticTicketId}});
   assert.equal(deniedTicketRead.status,404,"A different user cannot read the ticket");
   const ownTicket=await ticketCall({jwt:ownerAal1,body:{action:"ticketGet",ticketId:syntheticTicketId}});
   assert.equal(ownTicket.status,200);
   assert.equal(ownTicket.data.messages.length,1);
   const aal1Agent=await ticketCall({jwt:ownerAal1,body:{action:"ticketAdminList"}});
   assert.equal(aal1Agent.status,403,"AAL1 cannot access agent queue");
   const nonAdminAgent=await ticketCall({jwt:nonAdminAal2,body:{action:"ticketAdminList"}});
   assert.equal(nonAdminAgent.status,403,"AAL2 non-admin cannot access agent queue");
   const agentList=await ticketCall({jwt:ownerAal2,body:{action:"ticketAdminList"}});
   assert.equal(agentList.status,200,"Allowlisted AAL2 agent can load queue");
   assert.equal(agentList.data.tickets[0].requester_id,undefined);
   const wrongTicketConfirmation=await ticketCall({jwt:ownerAal2,body:{action:"ticketAdminAssign",ticketId:syntheticTicketId,assigneeId:ownerId,confirmId:"wrong"}});
   assert.equal(wrongTicketConfirmation.status,409,"Agent mutations require typed ticket confirmation");
   assert.equal(calls.supportAdminWrites,0);
   const assignedTicket=await ticketCall({jwt:ownerAal2,body:{action:"ticketAdminAssign",ticketId:syntheticTicketId,assigneeId:ownerId,confirmId:syntheticTicketId}});
   assert.equal(assignedTicket.status,200);
   const escalatedTicket=await ticketCall({jwt:ownerAal2,body:{action:"ticketAdminEscalate",ticketId:syntheticTicketId,confirmId:syntheticTicketId}});
   assert.equal(escalatedTicket.status,200);
   assert.equal(escalatedTicket.data.ticket.status,"escalated");
   const changedTicket=await ticketCall({jwt:ownerAal2,body:{action:"ticketAdminStatus",ticketId:syntheticTicketId,status:"in_progress",priority:"high",confirmId:syntheticTicketId}});
   assert.equal(changedTicket.status,200);
   assert.equal(changedTicket.data.ticket.priority,"high");
   const agentReply=await ticketCall({jwt:ownerAal2,body:{action:"ticketAdminReply",ticketId:syntheticTicketId,body:"We are reviewing this.",confirmId:syntheticTicketId}});
   assert.equal(agentReply.status,200);
   assert.equal(calls.supportAdminWrites,4);
   const updatedThread=await ticketCall({jwt:ownerAal1,body:{action:"ticketReply",ticketId:syntheticTicketId,body:"Thank you."}});
   assert.equal(updatedThread.status,200);
   assert.equal(syntheticTicketMessages.get(syntheticTicketId).length,3);
   console.log("PASS Phase 08: AAL1-owned tickets, redacted consented diagnostics, private threads, AAL2 assignment/escalation/status/replies and typed confirmation");
console.log("PASS actual Edge Function AAL1/nonadmin denial, owner AAL2 list and synthetic recovery");
  console.log("PASS all checks are isolated in memory; zero network/database/email activity");
})().catch(e=>{console.error(e);process.exitCode=1;});
