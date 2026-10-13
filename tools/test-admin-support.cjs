"use strict";
const assert=require("node:assert/strict");
(async()=>{
  const policy=await import("../supabase/functions/lockvyn-admin/support.mjs");
  const owner="a0000000-0000-4000-8000-000000000001";
  const target="b0000000-0000-4000-8000-000000000002";
  const request={id:target,kind:"suspend_24h",reason:"lost_device"};
  assert.equal(policy.validSupportRequest(request),true);
  for(const kind of ["delete_now","reset_master_password","disable_totp","revoke_tokens"]){
    assert.equal(policy.validSupportRequest({...request,kind}),false);
  }
  for(const reason of ["free text secrets","",undefined]){
    assert.equal(policy.validSupportRequest({...request,reason}),false);
  }
  assert.equal(policy.validSupportRequest({...request,id:"malformed"}),false);
  assert.equal(policy.safeTarget(target,owner,false),true);
  assert.equal(policy.safeTarget(owner,owner,false),false);
  assert.equal(policy.safeTarget(target,owner,true),false);
  assert.equal(policy.isAutomaticAction("suspend_24h"),true);
  assert.equal(policy.isAutomaticAction("restore_login"),true);
  assert.equal(policy.isAutomaticAction("deletion_review"),false);
  assert.equal(policy.isAutomaticAction("mfa_review"),false);
  const t=Math.floor(Date.now()/1000);
  assert.equal(policy.freshVerifiedTotp({aal:"aal2",amr:[{method:"totp",timestamp:t-10}]},t),true);
  assert.equal(policy.freshVerifiedTotp({aal:"aal2",amr:[{method:"mfa/totp",timestamp:t-20}]},t),true);
  assert.equal(policy.freshVerifiedTotp({aal:"aal2",amr:[{method:"totp",timestamp:t-181}]},t),false);
  assert.equal(policy.freshVerifiedTotp({aal:"aal2",amr:[{method:"totp",timestamp:t+20}]},t),false);
  assert.equal(policy.freshVerifiedTotp({aal:"aal2",amr:[{method:"token_refresh",timestamp:t}]},t),false);
  assert.equal(policy.freshVerifiedTotp({aal:"aal1",amr:[{method:"totp",timestamp:t}]},t),false);
  assert.equal(policy.freshVerifiedTotp({aal:"aal2"},t),false);
  assert.equal(policy.freshVerifiedTotp({aal:"aal2",amr:["totp"]},t),false);
  assert.equal(policy.validCaseConfirmation({...request,caseId:target,confirmEmail:"target@example.invalid"}),true);
  assert.equal(policy.validCaseConfirmation({...request,caseId:"not-valid",confirmEmail:"target@example.invalid"}),false);
  const later="2026-10-14T16:00:00.000Z";
  const earlier="2026-10-12T16:00:00.000Z";
  const now=Date.parse("2026-10-13T16:00:00.000Z");
  assert.deepEqual(policy.cloudLoginStatus(later,now),{state:"suspended",until:later});
  assert.deepEqual(policy.cloudLoginStatus(earlier,now),{state:"active",until:null});
  assert.deepEqual(policy.cloudLoginStatus(null,now),{state:"active",until:null});
  assert.deepEqual(policy.cloudLoginStatus("garbage",now),{state:"unknown",until:null});
  assert.deepEqual(policy.cloudLoginStatus({},now),{state:"unknown",until:null});
  assert.deepEqual(policy.cloudLoginStatus(later,NaN),{state:"unknown",until:null});
  console.log("PASS support input allowlist, fresh TOTP, protected owner, safe review-only controls");
})().catch(e=>{console.error(e);process.exitCode=1;});
