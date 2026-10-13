"use strict";
const assert = require("node:assert/strict");

(async () => {
  const { verifiedUserGate, adminMembershipGate } =
    await import("../supabase/functions/lockvyn-admin/access.mjs");
  const user = { id: "u-1", email_confirmed_at: "2026-01-01T00:00:00Z" };
  const claims = { sub: "u-1", role: "authenticated", aal: "aal2" };
  assert.equal(verifiedUserGate(user, claims), null, "Verified AAL2 session is eligible for allowlist check");
  assert.equal(verifiedUserGate({ ...user, email_confirmed_at: null }, claims)?.status, 401);
  assert.equal(verifiedUserGate(null, claims)?.status, 401);
  assert.equal(verifiedUserGate(user, { ...claims, sub: "u-2" })?.status, 401);
  assert.equal(verifiedUserGate(user, { ...claims, role: "anon" })?.status, 401);
  assert.equal(verifiedUserGate(user, { ...claims, aal: "aal1" })?.status, 403);
  assert.equal(verifiedUserGate(user, { ...claims, aal: null })?.status, 403);
  assert.equal(verifiedUserGate(user, {})?.status, 401);
  assert.deepEqual(verifiedUserGate(user, { ...claims, aal: "aal1" })?.body,
    { error: "Admin access requires verified 2FA", mfaRequired: true });
  assert.equal(adminMembershipGate({ user_id: "u-1" }, "u-1"), true);
  assert.equal(adminMembershipGate({ user_id: "u-2" }, "u-1"), false);
  assert.equal(adminMembershipGate(null, "u-1"), false);
  assert.equal(adminMembershipGate({ user_id: "u-1" }, ""), false);
  console.log("PASS admin AAL2, identity, email confirmation and allowlist gates");
})().catch(e => { console.error(e); process.exitCode = 1; });
