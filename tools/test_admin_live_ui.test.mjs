import assert from "node:assert/strict";
import test from "node:test";
import { assertDashboard, assertSecurityPage } from "./test_admin_live_ui.mjs";

function headers() {
  return {
    "Content-Type": "text/html; charset=utf-8",
    "Cache-Control": "no-store",
    "X-Content-Type-Options": "nosniff",
    "X-Frame-Options": "DENY",
    "Referrer-Policy": "no-referrer",
    "Content-Security-Policy": "default-src 'none'; base-uri 'none'; frame-ancestors 'none'; object-src 'none'; script-src 'nonce-test_nonce'; style-src 'nonce-test_nonce'; connect-src 'self' https://project.supabase.co; form-action 'self'",
  };
}

function response(body, status = 200, overrides = {}) {
  return new Response(body, { status, headers: { ...headers(), ...overrides } });
}

function securityHtml() {
  return '<!doctype html><html lang="en"><head><style nonce="test_nonce">body{color:#222}</style></head><body>' +
    '<h1>Sign out all my cloud sessions</h1><form id="sign-in"></form>' +
    '<form id="verify-otp"></form><div id="confirm-box"></div><button id="revoke"></button>' +
    '<p>Authenticator verified. Final consent is required.</p>' +
    '<p>Existing access tokens may work until expiry. Your offline vaults remain untouched.</p>' +
    '<script nonce="test_nonce">let accessToken="";</script></body></html>';
}

function dashboardHtml() {
  return '<html lang="en"><head><style nonce="test_nonce"></style></head><body>' +
    '<main id="dashboard">Account directory</main><i id="verified-stat"></i>' +
    '<i id="mfa-stat"></i><i id="active-stat"></i><input id="email-filter">' +
    '<i id="mfa-filter"></i><i id="filtered-count"></i><i id="directory-state"></i>' +
    '<i id="empty"></i><i id="prev"></i><i id="next"></i>' +
    '<p>Master Passphrases cannot be reset</p><script nonce="test_nonce">void 0;</script></body></html>';
}

test("accepts the public dashboard's existing metadata-only contract", () => {
  assert.doesNotThrow(() => assertDashboard(response(dashboardHtml()), dashboardHtml()));
});

test("accepts the owner-only security page with final consent and offline-vault notice", () => {
  assert.doesNotThrow(() => assertSecurityPage(response(securityHtml()), securityHtml()));
});

test("rejects a cacheable owner security page", () => {
  assert.throws(() => assertSecurityPage(
    response(securityHtml(), 200, { "Cache-Control": "public, max-age=60" }),
    securityHtml()
  ), /must not be cached/);
});

test("rejects persistent auth storage and untrusted third-party scripts", () => {
  const stored = securityHtml().replace('let accessToken="";', 'localStorage.token="secret";');
  assert.throws(() => assertSecurityPage(response(stored), stored), /browser storage/);
  const external = securityHtml().replace(
    '<script nonce="test_nonce">',
    '<script src="https://third-party.example/script.js" nonce="test_nonce">'
  );
  assert.throws(() => assertSecurityPage(response(external), external), /external scripts/);
});

test("rejects missing final account-owner confirmation", () => {
  const unsafe = securityHtml().replace('id="revoke"', 'id="delete-account"');
  assert.throws(() => assertSecurityPage(response(unsafe), unsafe), /id="revoke"/);
});
