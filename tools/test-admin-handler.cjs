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
  let source = readFileSync(file,"utf8");
  // The single TS-only type annotation does not affect runtime semantics.
  source = source.replace(/^import .*;\s*$/gm,"")
    .replace("let body: Record<string, unknown>;","let body;");
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
  const calls = {list:0,adminLookup:0,mail:0,recoveryClaim:0,factors:0};
  function token(user,aal="aal2",role="authenticated") {
    const payload = Buffer.from(JSON.stringify({sub:user.id,aal,role})).toString("base64url");
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
      assert.equal(name,"lockvyn_claim_recovery");
      assert.equal(args.p_actor,ownerId);
      assert.equal(args.p_target,targetId);
      calls.recoveryClaim++;
      return {data:true,error:null};
    }
  };
  let handler;
  const context={
    createClient:()=>service,html:()=>"<html></html>",
    verifiedUserGate:authFile.verifiedUserGate,
    adminMembershipGate:authFile.adminMembershipGate,
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
  async function call({jwt,action="list",id=targetId,page=1,originHeader=origin,contentType="application/json",method="POST"}={}){
    const headers={Origin:originHeader,"Content-Type":contentType};
    if(jwt)headers.Authorization="Bearer "+jwt;
    const request=new Request(endpoint,{method,headers,
      body:method==="POST"?JSON.stringify({action,id,page}):undefined});
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
  const good=await call({jwt:ownerAal2});
  assert.equal(good.status,200,"AAL2 admin is allowed");
  assert.equal(good.data.total,3);
  assert.equal(good.data.users[0].mfa,true);
  assert.equal(good.data.users[1].mfa,false);
  assert.equal(calls.list,1);
  const recovery=await call({jwt:ownerAal2,action:"recover"});
  assert.equal(recovery.status,200);
  assert.equal(recovery.data.sent,true);
  assert.equal(calls.mail,1,"only synthetic mock email sent");
  assert.equal(calls.recoveryClaim,1,"recovery is audited/rate-limited through mock RPC");
  assert.equal(calls.adminLookup,1);
  console.log("PASS actual Edge Function AAL1/nonadmin denial, owner AAL2 list and synthetic recovery");
  console.log("PASS all checks are isolated in memory; zero network/database/email activity");
})().catch(e=>{console.error(e);process.exitCode=1;});
