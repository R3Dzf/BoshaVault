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
  let source = readFileSync(file,"utf8");
  // The single TS-only type annotation does not affect runtime semantics.
  source = source.replace(/^import .*;\s*$/gm,"")
    .replace("let body: Record<string, unknown>;","let body;")
    .replace("function result(data: unknown, status = 200): Response {",
             "function result(data, status = 200) {")
    .replace("function nonce(): string {", "function nonce() {");
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
  const calls = {list:0,adminLookup:0,mail:0,recoveryClaim:0,factors:0,updated:0,prepared:0,claimed:0,finished:0};
  function token(user,aal="aal2",role="authenticated") {
    const payload = Buffer.from(JSON.stringify({sub:user.id,aal,role,
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
      assert.equal(table,"lockvyn_admins");
      return {
        select:()=>({
          eq:(_,id)=>({
            maybeSingle:async()=>({
              data:id===ownerId?{user_id:ownerId}:null,
              error:null,
            })
          })
        })
      };
    },
    rpc:async(name,args)=>{
      assert.equal(args.p_actor,ownerId);
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
    Deno:{
      env:{get:name=>({
        SUPABASE_URL:"https://example.supabase.co",
        SUPABASE_ANON_KEY:"test-public",
        SUPABASE_SERVICE_ROLE_KEY:"test-service-only",
      })[name]},
      serve:fn=>{handler=fn}
    },
    Request,Response,URL,JSON,TextEncoder,
    crypto:globalThis.crypto, btoa:globalThis.btoa, atob:globalThis.atob
  };
  vm.runInNewContext(source,context,{filename:file,timeout:2000});
  assert.equal(typeof handler,"function");
  async function call({jwt,action="list",id=targetId,page=1,search="",emailState="all",mfaState="all",originHeader=origin,contentType="application/json",method="POST",kind,reason,caseId:requestCaseId,confirmEmail}={}){
    const headers={Origin:originHeader,"Content-Type":contentType};
    if(jwt)headers.Authorization="Bearer "+jwt;
    const request=new Request(endpoint,{method,headers,
      body:method==="POST"?JSON.stringify({action,id,page,search,emailState,mfaState,kind,reason,caseId:requestCaseId,confirmEmail}):undefined});
    const res=await handler(request);
    return {status:res.status,data:await res.json()};
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
  await deny({jwt:nonAdminAal2},403,"real handler rejects AAL2 non-admin");
  await deny({jwt:otherRole},401,"real handler rejects unexpected role");
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
  assert.equal(calls.adminLookup,1);
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
  console.log("PASS actual Edge Function AAL1/nonadmin denial, owner AAL2 list and synthetic recovery");
  console.log("PASS all checks are isolated in memory; zero network/database/email activity");
})().catch(e=>{console.error(e);process.exitCode=1;});
