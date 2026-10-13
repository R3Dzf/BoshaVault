#!/usr/bin/env node
// Public, unauthenticated, read-only checks against the deployed admin UI.
// No credentials, no account listing, and no mutations.
import assert from "node:assert/strict";
const url="https://lockvyn-admin-ui.r3dzf-studio.workers.dev/";
const response=await fetch(url,{method:"GET",redirect:"manual",cache:"no-store"});
assert.equal(response.status,200,"Dashboard must return HTTP 200");
assert(response.headers.get("content-type")?.includes("text/html"),"Must serve HTML rather than text/plain");
assert.equal(response.headers.get("x-content-type-options"),"nosniff");
assert.equal(response.headers.get("x-frame-options"),"DENY");
const csp=response.headers.get("content-security-policy")||"";
assert(csp.includes("frame-ancestors 'none'"));
assert(csp.includes("script-src 'nonce-"),"CSP scripts require per-request nonce");
const html=await response.text();
assert(html.length<140000);
for(const name of ["id=\"dashboard\"","Account directory","id=\"verified-stat\"",
  "id=\"mfa-stat\"","id=\"active-stat\"","id=\"email-filter\"",
  "id=\"mfa-filter\"","id=\"filtered-count\"","id=\"directory-state\"",
  "id=\"empty\"","id=\"prev\"","id=\"next\""]){
  assert(html.includes(name),"Missing current dashboard element: "+name);
}
for(const marker of ['class="skip-link"','href="#directory-section"','id="directory-heading"', 'id="retry-directory"', 'role="region"', 'aria-busy="false"']) {
  assert(html.includes(marker),"Missing accessible directory feature: "+marker);
}
assert(html.includes("Master Passphrases cannot be reset"),"Vault safety disclosure missing");
assert(!html.includes("SUPABASE_SERVICE_ROLE_KEY"),"Admin secrets must not be in HTML");
console.log("PASS published dashboard HTML, filters, statistics, CSP and safety disclosure");
