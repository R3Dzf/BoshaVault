#!/usr/bin/env node
// Public, unauthenticated, read-only checks against the deployed admin UI.
// No credentials, account listing, or mutations; only GET requests are sent.
import assert from "node:assert/strict";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

const baseUrl = "https://lockvyn-admin-ui.r3dzf-studio.workers.dev/";

function assertCommonPage(response, html, label) {
  assert.equal(response.status, 200, label + " must return HTTP 200");
  assert.match(response.headers.get("content-type") || "", /text\/html/i,
    label + " must serve HTML");
  assert.equal(response.headers.get("cache-control"), "no-store",
    label + " must not be cached");
  assert.equal(response.headers.get("x-content-type-options"), "nosniff");
  assert.equal(response.headers.get("x-frame-options"), "DENY");
  assert.equal(response.headers.get("referrer-policy"), "no-referrer");
  assert(html.length < 130000, label + " exceeded the worker response limit");
  assert(!html.includes("SUPABASE_SERVICE_ROLE_KEY"),
    "Privileged secrets must not be in public HTML");
  assert(!/service_role/i.test(html),
    "Service-role material must not be in public HTML");
  const csp = response.headers.get("content-security-policy") || "";
  assert(csp.includes("default-src 'none'"));
  assert(csp.includes("frame-ancestors 'none'"));
  assert(csp.includes("object-src 'none'"));
  assert(csp.includes("script-src 'nonce-"), "CSP scripts require a per-request nonce");
  const scriptTags = [...html.matchAll(/<script\b([^>]*)>/gi)];
  assert(scriptTags.length > 0, label + " must contain its inline application script");
  for (const match of scriptTags) {
    assert(!/\bsrc\s*=/i.test(match[1]), "Public UI must not load external scripts");
    const nonce = /\bnonce="([^"]+)"/i.exec(match[1])?.[1];
    assert(nonce, "Every inline script must carry a nonce");
    assert(csp.includes("'nonce-" + nonce + "'"),
      "CSP must authorize only the page's nonce-scoped script");
  }
  const styleTags = [...html.matchAll(/<style\b([^>]*)>/gi)];
  assert(styleTags.length > 0, label + " must contain its inline styles");
  for (const match of styleTags) {
    const nonce = /\bnonce="([^"]+)"/i.exec(match[1])?.[1];
    assert(nonce, "Every inline style must carry a nonce");
    assert(csp.includes("'nonce-" + nonce + "'"),
      "CSP must authorize only the page's nonce-scoped styles");
  }
  assert(!/\b(?:localStorage|sessionStorage)\b/i.test(html),
    "Public pages must not persist authentication state in browser storage");
  return csp;
}

export function assertDashboard(response, html) {
  assertCommonPage(response, html, "Dashboard");
  for (const name of ["id=\"dashboard\"", "Account directory", "id=\"verified-stat\"",
    "id=\"mfa-stat\"", "id=\"active-stat\"", "id=\"email-filter\"",
    "id=\"mfa-filter\"", "id=\"filtered-count\"", "id=\"directory-state\"",
    "id=\"empty\"", "id=\"prev\"", "id=\"next\""]) {
    assert(html.includes(name), "Missing current dashboard element: " + name);
  }
  assert(html.includes("Master Passphrases cannot be reset"),
    "Vault safety disclosure missing");
}

export function assertSecurityPage(response, html) {
  const csp = assertCommonPage(response, html, "Security page");
  assert(csp.includes("connect-src 'self' https://"),
    "Security page must restrict network access to its own host and Supabase Auth");
  for (const text of ["Sign out all my cloud sessions", "id=\"sign-in\"",
    "id=\"verify-otp\"", "id=\"confirm-box\"", "id=\"revoke\"",
    "Authenticator verified. Final consent is required.",
    "Existing access tokens may work until expiry",
    "Your offline vaults remain untouched."]) {
    assert(html.includes(text), "Missing security-page safeguard: " + text);
  }
}

export async function runLiveSmoke(fetchImpl = fetch) {
  const pages = [
    { path: "/", check: assertDashboard, label: "dashboard" },
    { path: "/security", check: assertSecurityPage, label: "owner security page" },
  ];
  for (const page of pages) {
    const response = await fetchImpl(new URL(page.path, baseUrl), {
      method: "GET",
      redirect: "manual",
      cache: "no-store",
      headers: { Accept: "text/html" },
      signal: AbortSignal.timeout(15000),
    });
    const html = await response.text();
    page.check(response, html);
    console.log("PASS published " + page.label + " delivery and safety headers");
  }
}

if (process.argv[1] &&
    import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  runLiveSmoke().catch(error => {
    console.error("FAIL published admin page smoke check:", error.message);
    process.exitCode = 1;
  });
}
