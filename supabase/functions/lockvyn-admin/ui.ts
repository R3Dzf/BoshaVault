// Same-origin, dependency-free interface. Access tokens are kept in memory only.
export function html(n: string, key: string, recovering: boolean): string {
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Lockvyn · Account administration</title>
<style nonce="${n}">
:root{color-scheme:light;font-family:Inter,system-ui,-apple-system,"Segoe UI",sans-serif;background:#f7f7fb;color:#211b38}
*{box-sizing:border-box}body{margin:0;min-height:100vh}main{max-width:1140px;margin:auto;padding:32px 22px 80px}
header{display:flex;gap:16px;align-items:center;justify-content:space-between;padding:0 0 28px}
.brand{display:flex;align-items:center;gap:12px;font-weight:750;font-size:21px}.logo{width:38px;height:38px;border-radius:12px;background:#6a5bd6;color:white;display:grid;place-items:center;font-size:22px}
section,.panel{background:white;border:1px solid #e5e3ef;border-radius:17px;box-shadow:0 4px 20px #1910330c;padding:24px;margin-bottom:20px}
h1{margin:0 0 8px;font-size:25px}h2{margin:0 0 6px;font-size:18px}p,.note{color:#716b84;line-height:1.6}
form{display:grid;gap:13px;max-width:460px}label{font-size:13px;font-weight:650;display:grid;gap:6px}
input{font:inherit;border:1px solid #d9d6e4;border-radius:10px;padding:11px 12px;width:100%;min-height:44px}
input:focus{outline:2px solid #9c91f7;outline-offset:0}
button{font:inherit;font-weight:650;cursor:pointer;border:1px solid #d8d4ed;background:white;color:#4b3fac;padding:10px 14px;border-radius:10px;min-height:42px}
button.primary{background:#6a5bd6;color:white;border-color:#6a5bd6}button:disabled{opacity:.5;cursor:not-allowed}
button:focus-visible{outline:3px solid #c2b9ff;outline-offset:2px}
.toolbar{display:flex;gap:12px;align-items:center;justify-content:space-between;flex-wrap:wrap}.toolbar input{max-width:340px}
table{border-collapse:collapse;width:100%;min-width:690px;text-align:left}
th,td{border-bottom:1px solid #efecf5;padding:13px 10px;font-size:13px}
th{font-size:12px;color:#777086;text-transform:uppercase;letter-spacing:.035em}
td small{color:#777086;display:block;margin-top:3px}
td:last-child,th:last-child{text-align:right}
.scroll{overflow-x:auto}.muted{color:#756d86}.ok{color:#176e4b}.warn{color:#b05617}
.stat{display:flex;gap:18px;flex-wrap:wrap}.stat strong{font-size:23px}
#alert{min-height:24px;font-weight:600}#alert.error{color:#ae3044}#alert.success{color:#126848}
footer{font-size:12px;color:#787284;margin-top:20px}
[hidden]{display:none!important}@media(max-width:640px){main{padding:22px 12px}section{padding:16px}h1{font-size:22px}}

/* Phase 01: responsive account overview and explicit global-vs-filtered totals */
.overview{background:linear-gradient(135deg,#fff 0%,#f1efff 100%);border-color:#e3dafa}
.eyebrow{font-size:11px;letter-spacing:.085em;text-transform:uppercase;color:#7467aa;font-weight:750}
.cards{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:12px;margin:18px 0 24px}
.metric{border:1px solid #e4e0f1;background:white;padding:16px;border-radius:14px}
.metric strong{font-size:28px;line-height:1.4;display:block;font-variant-numeric:tabular-nums}
.metric small{color:#78718a;line-height:1.5}
.filters{display:flex;gap:10px;align-items:end;flex-wrap:wrap;margin:18px 0 12px}
.filters label{flex:1 1 170px;max-width:370px}.filters select{min-height:44px;padding:10px 12px;border:1px solid #d9d6e4;border-radius:10px;background:white;color:#211b38;font:inherit;width:100%}
.filters .search-label{flex:2 1 230px;max-width:none}
.table-head{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap}
#directory-state{font-size:13px;margin:12px 0;min-height:20px;color:#716b84}
#empty{padding:32px 12px;text-align:center;color:#716b84}
#directory-table[aria-busy="true"]{opacity:.6}
.status{font-weight:650;font-size:12px;padding:5px 9px;border-radius:7px;display:inline-block;background:#f0eef8}
.status.yes{background:#e4f6eb;color:#13613d}
.status.no{background:#fff0ea;color:#94482c}
.status.unknown{background:#f0eef5;color:#615c70}
@media(max-width:800px){.cards{grid-template-columns:repeat(2,minmax(0,1fr))}.metric{padding:12px}}
@media(max-width:520px){.filters label{max-width:none;flex:1 1 100%}.cards{gap:8px}.metric strong{font-size:23px}main{padding:18px 12px 70px}.table-head{align-items:start}}
.skip-link{position:absolute;left:12px;top:-70px;background:#fff;border:2px solid #6a5bd6;padding:12px;z-index:20;border-radius:8px}
.skip-link:focus{top:8px}
#directory-table:focus-visible{outline:3px solid #6a5bd6;outline-offset:3px}
@media(prefers-reduced-motion:reduce){*{scroll-behavior:auto!important;transition:none!important}}
/* Phase 04: concise metadata-only security signals. */
.security-alerts{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px;margin:16px 0}
.security-alert{border:1px solid #e4e0f1;border-radius:12px;padding:13px;background:#fbfaff}
.security-alert strong{display:block;font-size:22px;font-variant-numeric:tabular-nums}
.security-alert small{color:#716b84;line-height:1.5}
.security-alert.warning{border-color:#e6b08a;background:#fff8f2}
.security-events{list-style:none;padding:0;display:grid;gap:9px}
.security-events li{border:1px solid #eeeaf7;border-radius:10px;padding:12px}
.security-events li strong{display:block}
.security-events li p{margin:5px 0 0;font-size:12px;overflow-wrap:anywhere}
@media(max-width:700px){.security-alerts{grid-template-columns:repeat(2,minmax(0,1fr))}}
@media(max-width:420px){.security-alerts{grid-template-columns:1fr}}
/* Phase 02: explicit, accessible, cloud-only support workflow. */
.support-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px}
.support-grid label{display:grid;gap:7px;font-size:13px;font-weight:650}
.support-grid select{width:100%;padding:10px;border:1px solid #d9d6e4;border-radius:10px;min-height:44px;background:white;color:#211b38}
.support-actions{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin-top:16px}
.support-note{font-size:13px;line-height:1.6;padding:12px;border:1px solid #e4e0f1;border-radius:9px}
.case-history{list-style:none;padding:0;display:grid;gap:8px}
.case-history li{padding:12px;border:1px solid #eeeaf7;border-radius:9px;font-size:13px;overflow-wrap:anywhere}
.device-inventory{border-top:1px solid #eeeaf7;margin-top:22px;padding-top:20px}
.device-list{list-style:none;padding:0;display:grid;gap:10px}
.device-list li{padding:13px;border:1px solid #eeeaf7;border-radius:10px}
.device-list h4{margin:0 0 5px;font-size:14px}
.device-list p{margin:4px 0;font-size:13px}
.device-list button{margin-top:8px}

@media(max-width:620px){.support-grid{grid-template-columns:1fr}}

.ticket-thread{white-space:pre-wrap;overflow-wrap:anywhere;border:1px solid #e8e4f1;background:#fbfaff;border-radius:10px;padding:14px;min-height:100px;max-height:360px;overflow:auto}
.ticket-diagnostics{display:grid;grid-template-columns:minmax(90px,.5fr) minmax(0,1.5fr);gap:6px 12px;padding:12px;border:1px solid #e8e4f1;border-radius:10px}
.ticket-diagnostics dt{font-weight:700}.ticket-diagnostics dd{margin:0;overflow-wrap:anywhere}
.ticket-agent-actions{display:grid;gap:12px;margin-top:14px}.ticket-agent-actions textarea{font:inherit;border:1px solid #d9d6e4;border-radius:10px;padding:10px 12px;resize:vertical;min-height:90px}
.ticket-buttons,.ticket-status-grid{display:flex;gap:10px;flex-wrap:wrap;align-items:end}
.ticket-status-grid label{flex:1 1 130px}.ticket-status-grid select{font:inherit;border:1px solid #d9d6e4;border-radius:10px;padding:10px 12px;background:white;color:#211b38;width:100%;min-height:44px}

.release-table{min-width:850px}
.release-policy-block{border-top:1px solid #efecf5;margin-top:18px;padding-top:18px}
.release-notes{white-space:pre-wrap;max-width:340px}
.announcement-grid{display:grid;grid-template-columns:minmax(0,1.15fr) minmax(290px,.85fr);gap:18px;margin-top:16px}
.announcement-editor{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}
.announcement-editor label{min-width:0}.announcement-editor .wide{grid-column:1/-1}
.announcement-editor textarea,.announcement-editor select{font:inherit;border:1px solid #d9d6e4;border-radius:10px;padding:10px 12px;background:white;color:#211b38;width:100%;min-height:44px}
.announcement-editor textarea{resize:vertical;min-height:86px;line-height:1.5}
.announcement-editor input[type=checkbox]{width:auto;min-height:0;padding:0}
.announcement-list{list-style:none;padding:0;margin:12px 0;display:grid;gap:9px}
.announcement-list li{border:1px solid #e8e4f1;border-radius:11px;padding:12px;display:grid;gap:6px}
.analytics-filters{display:flex;gap:12px;align-items:end;flex-wrap:wrap;margin:16px 0}
.analytics-filters label{flex:1 1 180px;max-width:320px}
.analytics-chart{list-style:none;padding:0;display:grid;gap:10px}
.analytics-chart li{display:grid;grid-template-columns:110px 1fr;gap:12px;align-items:center}
.analytics-bars{display:grid;grid-template-columns:repeat(5,minmax(0,1fr));gap:8px}
.analytics-series{display:grid;gap:4px;font-size:11px;color:#716b84}
.analytics-bar{display:block;height:8px;min-width:2px;max-width:100%;border-radius:8px;background:#6a5bd6}
.analytics-table{min-width:850px}
.analytics-export{margin-top:10px}
@media(max-width:640px){.analytics-chart li{grid-template-columns:1fr}.analytics-bars{grid-template-columns:repeat(2,minmax(0,1fr))}}
.announcement-list li button{text-align:left}
.announcement-preview{white-space:pre-wrap;overflow-wrap:anywhere;border:1px solid #e8e4f1;background:#fbfaff;border-radius:10px;padding:14px;min-height:110px}
.announcement-actions{display:flex;gap:8px;flex-wrap:wrap;align-items:end;margin-top:12px}
@media(max-width:760px){.announcement-grid{grid-template-columns:1fr}}
@media(max-width:520px){.announcement-editor{grid-template-columns:1fr}.announcement-editor .wide{grid-column:auto}}
 .role-hidden{display:none!important}.role-actions{display:flex;gap:10px;flex-wrap:wrap}.role-actions button{min-width:140px}
</style>
</head>
<body>
<a id="skip-link" class="skip-link" href="#directory-section">Skip to account directory</a>
<main>
<header><div class="brand"><span class="logo">◇</span>Lockvyn <span class="muted">/ Admin</span></div>
<button id="logout" hidden>Sign out</button></header>
<div id="alert" role="status" aria-live="polite"></div>
<section id="login" ${recovering?"hidden":""}>
<h1>Account administration</h1>
<p>Sign in with your approved administrator account. A verified authenticator code is required.</p>
<form id="login-form"><label>Email<input id="email" type="email" autocomplete="username" required></label>
<label>Cloud account password<input id="password" type="password" autocomplete="current-password" minlength="16" required></label>
<button class="primary" type="submit">Continue</button></form>
</section>
<section id="mfa" hidden>
<h2>Verify your authenticator</h2><p>Enter the current six-digit code from your registered TOTP app.</p>
<form id="mfa-form"><label>Authenticator code<input id="code" inputmode="numeric" autocomplete="one-time-code" pattern="[0-9]{6}" maxlength="6" required></label>
<button class="primary" type="submit">Verify and open dashboard</button></form>
</section>
<div id="dashboard" hidden>
<section id="role-status" aria-labelledby="role-status-heading">
  <h2 id="role-status-heading">Administrator access</h2>
  <p id="role-status-text" role="status" aria-live="polite">Checking your assigned role…</p>
  <p id="role-user-id" class="note"></p>
  <button id="refresh-role-status" type="button">Refresh role status</button>
</section>
<section id="role-bootstrap" hidden aria-labelledby="role-bootstrap-heading">
  <h2 id="role-bootstrap-heading">Claim the initial owner role</h2>
  <p>No administrator role exists yet. The first allowlisted administrator may become the initial owner after a fresh authenticator check. Verify the account UUID below and type it to confirm. This is a one-time initialization.</p>
  <p><strong>Your account UUID:</strong> <code id="role-bootstrap-user"></code></p>
  <label for="role-bootstrap-confirm">Type your account UUID<input id="role-bootstrap-confirm" autocomplete="off" maxlength="36"></label>
  <label for="role-bootstrap-code">Fresh authenticator code<input id="role-bootstrap-code" inputmode="numeric" autocomplete="one-time-code" pattern="[0-9]{6}" maxlength="6"></label>
  <button id="role-bootstrap-button" class="primary" type="button">Initialize owner role</button>
</section>
<section id="role-manager" hidden aria-labelledby="role-manager-heading">
  <h2 id="role-manager-heading">Administrator roles</h2>
  <p>Only confirmed, allowlisted administrators can receive a scoped role. Every change needs a fresh authenticator code and the target account UUID typed twice. Role history is append-only.</p>
  <form id="role-form">
    <label for="role-target-id">Target administrator UUID<input id="role-target-id" autocomplete="off" maxlength="36" required></label>
    <label for="role-name">Role<select id="role-name"><option value="support">Support</option><option value="security">Security</option><option value="release">Release</option><option value="owner">Owner</option></select></label>
    <label for="role-confirm-id">Type the same UUID to confirm<input id="role-confirm-id" autocomplete="off" maxlength="36" required></label>
    <label for="role-code">Fresh authenticator code<input id="role-code" inputmode="numeric" autocomplete="one-time-code" pattern="[0-9]{6}" maxlength="6" required></label>
    <div class="role-actions"><button class="primary" type="submit">Assign role</button><button id="role-revoke" type="button">Revoke role</button></div>
  </form>
  <p id="role-manager-state" role="status" aria-live="polite">Role roster has not been loaded.</p>
  <ul id="role-list" class="announcement-list" aria-label="Current administrator role assignments"></ul>
  <h3>Role change history</h3><ol id="role-audit" class="announcement-list" aria-label="Administrator role change history"></ol>
</section>
<section class="support-ticket-manager" data-roles="owner support" aria-labelledby="ticket-heading">
  <div class="toolbar">
    <div><div class="eyebrow">Private requests · 180-day retention</div>
      <h2 id="ticket-heading">Support tickets</h2>
      <p class="note">Ticket text and optional sanitized diagnostics are visible to the requester and approved agents. Every change requires a fresh authenticator check and typed ticket ID. No vault contents are available here.</p></div>
    <button id="refresh-tickets" type="button">Refresh ticket queue</button>
  </div>
  <p id="ticket-queue-state" role="status" aria-live="polite">Ticket queue has not been loaded.</p>
  <div class="announcement-grid">
    <div>
      <h3>Queue</h3>
      <ol id="ticket-list" class="announcement-list" aria-label="Support ticket queue"></ol>
    </div>
    <div>
      <h3 id="ticket-selected-heading">Selected request</h3>
      <p id="ticket-selected-state" role="status" aria-live="polite">Choose a request to review its replies.</p>
      <p id="ticket-selected-id" class="note">Ticket IDs appear after selection.</p>
      <div id="ticket-thread" class="ticket-thread" role="region" aria-label="Support ticket conversation" tabindex="0">No request selected.</div>
      <h4>Consented diagnostics</h4>
      <dl id="ticket-diagnostics" class="ticket-diagnostics"><dt>State</dt><dd>No diagnostics loaded.</dd></dl>
      <div class="ticket-agent-actions" role="group" aria-label="Selected ticket actions">
        <label>Type the selected ticket ID to enable changes
          <input id="ticket-confirm-id" autocomplete="off" maxlength="36" aria-describedby="ticket-selected-id"></label>
        <div class="ticket-buttons">
          <button id="ticket-assign-me" type="button" disabled>Assign to me</button>
          <button id="ticket-unassign" type="button" disabled>Unassign</button>
          <button id="ticket-escalate" type="button" disabled>Escalate</button>
        </div>
        <div class="ticket-status-grid">
          <label>Status<select id="ticket-status"><option value="open">Open</option><option value="in_progress">In progress</option><option value="escalated">Escalated</option><option value="closed">Closed</option></select></label>
          <label>Priority<select id="ticket-priority"><option value="low">Low</option><option value="normal">Normal</option><option value="high">High</option></select></label>
        </div>
        <button id="ticket-save-status" type="button" disabled>Save status and priority</button>
        <label>Agent reply<textarea id="ticket-reply" rows="4" maxlength="4000" placeholder="Write a reply (up to 4,000 characters)"></textarea></label>
        <button id="ticket-send-reply" type="button" disabled>Send reply</button>
      </div>
    </div>
  </div>
</section>
<section data-roles="owner release" aria-labelledby="announcement-heading">
  <div class="toolbar">
    <div><div class="eyebrow">Maintenance · security · updates</div>
      <h2 id="announcement-heading">Announcements & notices</h2>
      <p class="note">Write bilingual notices, preview plain text, target an app version and publish with fresh authenticator verification. Client alerts stay opt-in and local.</p></div>
    <button id="refresh-announcements" type="button">Refresh notices</button>
  </div>
  <p id="announcement-state" role="status" aria-live="polite">Notices have not been loaded.</p>
  <div class="announcement-grid">
    <div>
      <h3>Drafts and published notices</h3>
      <ol id="announcement-list" class="announcement-list" aria-label="Announcement list"></ol>
      <div class="announcement-actions">
        <label>Type the selected notice ID to publish or archive
          <input id="announcement-confirm-id" autocomplete="off" maxlength="36" aria-describedby="announcement-selected-id"></label>
        <button id="announcement-publish" type="button" disabled>Publish selected draft</button>
        <button id="announcement-archive" type="button" disabled>Archive selected notice</button>
      </div>
      <p id="announcement-selected-id" class="note">Select a notice to see its ID and actions.</p>
    </div>
    <div>
      <form id="announcement-form" class="announcement-editor" style="max-width:none">
        <label>English title<input id="announcement-title-en" maxlength="120" required></label>
        <label>Arabic title<input id="announcement-title-ar" maxlength="120" required dir="auto"></label>
        <label class="wide">English message<textarea id="announcement-body-en" maxlength="800" rows="4" required></textarea></label>
        <label class="wide">Arabic message<textarea id="announcement-body-ar" maxlength="800" rows="4" required dir="auto"></textarea></label>
        <label>Severity<select id="announcement-severity"><option value="info">Information</option><option value="warning">Warning</option><option value="critical">Critical</option></select></label>
        <label>Platform<select id="announcement-platform"><option value="all">All platforms</option><option value="windows">Windows</option><option value="android">Android</option></select></label>
        <label>Minimum version<input id="announcement-min" maxlength="16" placeholder="Optional · e.g. 1.21.0"></label>
        <label>Maximum version<input id="announcement-max" maxlength="16" placeholder="Optional · Android uses version code"></label>
        <label>Expires at<input id="announcement-expires" type="datetime-local"></label>
        <label><span>Show an in-app alert when enabled locally</span><input id="announcement-notify" type="checkbox"></label>
        <div class="wide announcement-actions">
          <button class="primary" id="announcement-save" type="submit">Save as draft</button>
          <button id="announcement-new" type="button">New draft</button>
          <button id="announcement-preview-button" type="button">Preview English & Arabic</button>
        </div>
      </form>
      <h3>Preview</h3>
      <div id="announcement-preview" class="announcement-preview" role="status" aria-live="polite">The preview appears here as plain text.</div>
    </div>
  </div>
</section>
<section class="release-manager" data-roles="owner release" aria-labelledby="release-manager-heading">
  <div class="toolbar">
    <div><div class="eyebrow">Official packages · Windows & Android</div>
      <h2 id="release-manager-heading">Release Manager</h2>
      <p class="note">Published stable releases, CI state, changelog, asset SHA256 and signing metadata. No vault or device data is used.</p></div>
    <button id="refresh-releases" type="button">Refresh release status</button>
  </div>
  <p id="release-manager-state" role="status" aria-live="polite">Release status has not been loaded.</p>
  <div class="scroll"><table class="release-table">
    <thead><tr><th>Platform / tag</th><th>Published</th><th>CI</th><th>Signature status</th><th>Assets and SHA256</th><th>Changelog</th></tr></thead>
    <tbody id="release-rows"></tbody>
  </table></div>
  <div class="release-policy-block">
    <h3>Client update policy</h3>
    <p id="release-policy-state" role="status" aria-live="polite">Policy has not been loaded.</p>
    <p class="note">The latest stable feed is the default. Rollback sets rollout to 0 and stops optional installs; clients below the minimum still receive the required newer release. Updates never downgrade or change encrypted vaults. A fresh TOTP login is required to save.</p>
    <form id="release-policy-form" style="max-width:620px">
      <label>Platform<select id="release-platform"><option value="windows">Windows</option><option value="android">Android</option></select></label>
      <label>Stable release candidate<select id="release-candidate"><option value="">Latest stable feed</option></select></label>
      <label id="release-minimum-label">Minimum Windows version<input id="release-minimum" inputmode="decimal" required value="0.0.0"></label>
      <label>Phased rollout percent<input id="release-rollout" type="number" min="0" max="100" step="1" required value="100"></label>
      <label>Reason<select id="release-reason" required>
        <option value="planned_rollout">Planned rollout</option><option value="rollback">Rollback / stop rollout</option>
        <option value="minimum_version">Minimum version change</option><option value="incident_response">Incident response</option>
      </select></label>
      <label>Type the selected tag (or LATEST)<input id="release-confirm-tag" autocomplete="off" required maxlength="80"></label>
      <button id="save-release-policy" class="primary" type="submit" disabled>Save release policy</button>
    </form>
  </div>
</section>
<section class="overview" data-roles="owner support">
  <div class="eyebrow">Overview · Account administration</div>
  <div class="toolbar"><div><h1>Users & access</h1>
    <p class="note">Live cloud account metadata. No passwords, vault records or recovery keys are available to administrators.</p></div>
  </div>
  <div class="cards" aria-label="All account statistics">
    <article class="metric"><small>Total registered</small><strong id="total">—</strong><small>All cloud accounts</small></article>
    <article class="metric"><small>Verified emails</small><strong id="verified-stat">—</strong><small>All cloud accounts</small></article>
    <article class="metric"><small>2FA enabled</small><strong id="mfa-stat">—</strong><small>Verified MFA enrollment</small></article>
    <article class="metric"><small>Active last 30 days</small><strong id="active-stat">—</strong><small>By last sign-in timestamp</small></article>
  </div>
</section>
<section data-roles="owner security" aria-labelledby="analytics-heading">
  <div class="toolbar">
    <div><div class="eyebrow">Opt-in · aggregate-only · 90-day retention</div>
      <h2 id="analytics-heading">Privacy-conscious analytics</h2>
      <p class="note">Only accounts that currently opted in contribute. The report shows aggregate signup day, active app versions, update uptake, sync success/failure, and coarse transfer-size buckets. Groups under five are hidden. No emails, account IDs, device IDs, vault data, IP addresses, or raw errors are returned.</p></div>
    <button id="refresh-analytics" type="button">Refresh report</button>
  </div>
  <p class="note">Analytics consent is off by default in the Windows and Android Sync privacy settings. Opting out deletes that account’s events and consent row. Signup dates appear only for accounts that currently opted in.</p>
  <div class="analytics-filters" role="group" aria-label="Analytics date filters">
    <label>From date<input id="analytics-from" type="date"></label>
    <label>To date<input id="analytics-to" type="date"></label>
    <button id="apply-analytics-filters" type="button">Apply dates</button>
    <button id="analytics-export" class="analytics-export" type="button" disabled>Export aggregate CSV</button>
  </div>
  <p id="analytics-state" role="status" aria-live="polite">Choose a date range to load privacy-safe aggregates.</p>
  <h3>Daily activity chart</h3>
  <ol id="analytics-chart" class="analytics-chart" aria-label="Daily aggregate metric totals"></ol>
  <h3>Aggregate report</h3>
  <div class="scroll"><table class="analytics-table">
    <caption class="muted">Rows represent groups of at least five currently opted-in accounts.</caption>
    <thead><tr><th>Date</th><th>Metric</th><th>Platform</th><th>App version</th><th>Outcome</th><th>Transfer size</th><th>Accounts</th><th>Events</th></tr></thead>
    <tbody id="analytics-table-body"></tbody>
  </table></div>
</section>
<section id="directory-section" data-roles="owner support" aria-labelledby="directory-heading">
  <div class="table-head"><div><h2 id="directory-heading" tabindex="-1">Account directory</h2>
    <p class="note">Search and filters cover all registered accounts, not only the current page.</p></div>
    <div class="muted" id="filtered-count" aria-live="polite">Loading account count…</div>
  </div>
  <div class="filters" role="group" aria-label="Account directory filters">
    <label class="search-label">Search email across all users
      <input id="search" type="search" placeholder="Search by email" autocomplete="off" maxlength="120"></label>
    <label>Email status
      <select id="email-filter"><option value="all">All email statuses</option>
      <option value="verified">Verified</option><option value="pending">Pending</option></select></label>
    <label>2FA status
      <select id="mfa-filter"><option value="all">All 2FA statuses</option>
      <option value="enabled">Enabled</option><option value="disabled">Off</option></select></label>
    <button id="clear-filters" type="button">Clear filters</button>
  </div>
  <div id="directory-state" role="status" aria-live="polite"></div>
  <div class="scroll" id="directory-table" role="region" aria-label="Accounts table (scroll horizontally on narrow screens)" tabindex="0" aria-busy="false">
    <table><thead><tr><th scope="col">Email</th><th scope="col">Registered</th>
    <th scope="col">Last sign-in</th><th scope="col">Email status</th>
    <th scope="col">2FA</th><th scope="col">Support</th></tr></thead>
    <tbody id="users"></tbody></table>
  </div>
  <div id="empty" hidden role="status">No accounts match these filters. Try clearing the search.</div>
  <button id="retry-directory" type="button" hidden>Retry loading accounts</button>
  <div class="toolbar" style="margin-top:20px">
    <button id="prev" type="button">Previous</button>
    <span class="muted" id="page-label">Page 1</span>
    <button id="next" type="button">Next</button>
  </div>
</section>
<section id="account-support" data-roles="owner support" hidden aria-labelledby="support-heading">
  <h2 id="support-heading" tabindex="-1">Cloud account support</h2>
  <p id="support-target" class="note"></p>
  <p id="support-cloud-state" class="support-note" role="status" aria-live="polite">Cloud login status has not been checked.</p>
  <button id="refresh-support-status" type="button">Refresh cloud login status</button>
  <p class="support-note">All support actions affect the cloud account only, never the encrypted vault on an offline device.
    A suspended user's existing access token may remain valid until its expiry.
    2FA recovery and account deletion create review cases only; they never silently change the account. A user can <a href="https://lockvyn-admin-ui.r3dzf-studio.workers.dev/security">revoke their own cloud sessions</a> with their credentials; administrators cannot impersonate them.</p>
  <section class="device-inventory" data-roles="owner security" aria-labelledby="cloud-device-heading">
    <div class="toolbar"><div><h3 id="cloud-device-heading" tabindex="-1">Registered cloud sessions</h3>
      <p class="note">Only sessions the user explicitly opted in appear here. Names and software versions are self-reported; they do not prove a physical device's identity.</p></div>
      <button id="refresh-cloud-devices" type="button">Refresh sessions</button></div>
    <p class="support-note">Revoking blocks this signed-in app session from cloud snapshots and pairing. It does not erase offline vault data or invalidate the Auth refresh token. A new sign-in creates a different session.</p>
    <section class="sync-overview" data-roles="owner security" aria-labelledby="cloud-sync-heading">
      <h4 id="cloud-sync-heading">Encrypted cloud sync</h4>
      <p id="cloud-sync-usage" role="status" aria-live="polite">Sync usage has not been loaded.</p>
      <p id="cloud-sync-state" class="support-note" role="status" aria-live="polite" tabindex="-1">Cloud sync state has not been loaded.</p>
      <div class="filters" role="group" aria-label="Cloud sync activity filters">
        <label>Platform
          <select id="cloud-device-platform"><option value="">All platforms</option>
            <option value="windows">Windows</option><option value="android">Android</option>
            <option value="macos">macOS</option><option value="ios">iOS</option>
            <option value="linux">Linux</option><option value="other">Other</option></select></label>
        <label>Last result
          <select id="cloud-device-outcome"><option value="all">All results</option>
            <option value="success">Success</option><option value="failure">Failure</option>
            <option value="unknown">Not reported</option></select></label>
        <label>Activity window
          <select id="cloud-device-since"><option value="7">7 days</option><option value="30" selected>30 days</option>
            <option value="90">90 days</option><option value="365">365 days</option>
            <option value="all">Any time</option></select></label>
        <label>Device or app
          <input id="cloud-device-query" type="search" maxlength="80" autocomplete="off"
            placeholder="Filter session metadata"></label>
        <button id="apply-cloud-device-filters" type="button">Apply filters</button>
      </div>
      <div class="support-actions" aria-label="Cloud sync control">
        <button class="primary" id="pause-cloud-sync" type="button" hidden>Pause cloud sync</button>
        <button class="primary" id="resume-cloud-sync" type="button" hidden>Resume cloud sync</button>
      </div>
      <div id="cloud-sync-confirm" role="group" aria-labelledby="cloud-sync-confirm-heading" hidden>
        <h4 id="cloud-sync-confirm-heading">Confirm cloud sync change</h4>
        <p id="cloud-sync-confirm-summary" class="note"></p>
        <form id="cloud-sync-form">
          <label>Type the affected account's full email
            <input id="cloud-sync-email" type="email" autocomplete="off" required></label>
          <label>Reason
            <select id="cloud-sync-reason"><option value="support_review">Support review</option>
              <option value="suspected_compromise">Suspected compromise</option></select></label>
          <label>Fresh code from your own authenticator app
            <input id="cloud-sync-otp" inputmode="numeric" autocomplete="one-time-code"
              pattern="[0-9]{6}" maxlength="6" required></label>
          <div class="support-actions"><button class="primary" type="submit">Confirm cloud sync change</button>
            <button id="cancel-cloud-sync" type="button">Cancel</button></div>
        </form>
      </div>
      <div id="cloud-device-state" role="status" aria-live="polite" tabindex="-1">Registered sessions have not been loaded.</div>
      <ul id="cloud-device-list" class="device-list"></ul>
    </section>
    <div id="device-revoke-panel" role="group" aria-labelledby="device-revoke-heading" hidden>
      <h4 id="device-revoke-heading">Confirm cloud session revocation</h4>
      <p id="device-revoke-summary" class="note"></p>
      <form id="device-revoke-form">
        <label>Type the affected account's full email
          <input id="device-revoke-email" type="email" autocomplete="off" required></label>
        <label>Fresh code from your own authenticator app
          <input id="device-revoke-otp" inputmode="numeric" autocomplete="one-time-code" pattern="[0-9]{6}" maxlength="6" required></label>
        <div class="support-actions"><button class="primary" type="submit">Revoke this cloud session</button>
          <button id="cancel-device-revoke" type="button">Cancel</button></div>
      </form>
    </div>
  </section>
  <div class="support-grid">
    <label>Support action
      <select id="support-kind">
        <option value="suspend_24h">Suspend cloud login for 24 hours</option>
        <option value="restore_login">Restore cloud login</option>
        <option value="session_review">Request session revocation review</option>
        <option value="mfa_review">Request 2FA recovery review</option>
        <option value="deletion_review">Request account deletion review</option>
      </select>
    </label>
    <label>Reason (no passwords or private data)
      <select id="support-reason">
        <option value="user_request">Verified user request</option>
        <option value="lost_device">Lost device</option>
        <option value="suspected_compromise">Suspected account compromise</option>
        <option value="access_problem">Access problem</option>
        <option value="policy_violation">Policy violation</option>
      </select>
    </label>
  </div>
  <div class="support-actions">
    <button class="primary" id="prepare-support" type="button">Prepare support action</button>
    <button id="close-support" type="button">Cancel</button>
  </div>
  <div id="support-confirm" hidden>
    <h2>Final confirmation</h2>
    <p id="support-summary" class="note"></p>
    <form id="support-form">
      <label>Type the affected account's full email to confirm
        <input id="support-email" type="email" autocomplete="off" required></label>
      <label>Fresh code from your own authenticator app
        <input id="support-otp" inputmode="numeric" autocomplete="one-time-code" pattern="[0-9]{6}" maxlength="6" required></label>
      <button class="primary" type="submit">Confirm cloud support action</button>
    </form>
  </div>
</section>
<section id="support-history-section" data-roles="owner support" aria-labelledby="support-history-heading">
  <div class="toolbar"><h2 id="support-history-heading">Support activity</h2>
    <button id="refresh-history" type="button">Refresh history</button></div>
  <p class="note">Metadata-only audit history. No vault content, recovery codes or session tokens.</p>
  <div id="support-history-state" role="status" aria-live="polite"></div>
  <ul class="case-history" id="support-history"></ul>
</section>
<section id="security-center" data-roles="owner security" aria-labelledby="security-center-heading">
  <div class="toolbar"><h2 id="security-center-heading">Security Center</h2>
    <button id="refresh-security-events" type="button">Refresh security activity</button></div>
  <p class="note">Last 90 days of privacy-limited first-party events: opt-in cloud session enrollment/revocation, support actions, recovery requests, and verified-user admin access denials. Password login failures, emails, IP addresses, tokens, and vault data are not collected here.</p>
  <div id="security-alerts" class="security-alerts" role="group" aria-label="Recent security signal counts"></div>
  <div id="security-events-state" role="status" aria-live="polite">Security activity has not been loaded.</div>
  <ul id="security-events" class="security-events"></ul>
</section>
<section><h2>About account recovery</h2><p>Send a password recovery email for a user's <strong>cloud login account</strong>.
The user chooses a new cloud password using their own verified email.
<strong>Master Passphrases cannot be reset by the administrator</strong>: private vault encryption remains independent.</p></section>
</div>
<section id="recovery" ${recovering?"":"hidden"}>
<h1>Reset cloud account password</h1><p>This only changes your Lockvyn cloud sign-in password. It does not change the passphrase that encrypts your vault.</p>
<form id="recovery-form"><label>New cloud account password<input id="new-password" type="password" minlength="16" autocomplete="new-password" required></label>
<label>Confirm new password<input id="confirm-password" type="password" minlength="16" autocomplete="new-password" required></label>
<button class="primary" type="submit">Change cloud account password</button></form>
</section>
<footer>Support dashboard · No access to private vault entries · Account actions are logged</footer>
</main>
<script nonce="${n}">
"use strict";
const PUBLIC_KEY=${JSON.stringify(key)}, RECOVERY=${recovering};
const base="https://ghmbcfqiomggoxgwghjs.supabase.co";
let accessToken="", pendingFactor=null, challengeId="", page=1, accounts=[], currentRole=null, roleStatusData=null;
const get=id=>document.getElementById(id);
function alertText(message,bad=false){const el=get("alert");el.textContent=message;el.className=bad?"error":"success";}
function visible(id,yes){get(id).hidden=!yes;}
function date(s){return s?new Date(s).toLocaleDateString():"—";}
async function authFetch(path,body,verb="POST",jwt=""){
  const res=await fetch(base+"/auth/v1/"+path,{
    method:verb,cache:"no-store",
    headers:{"apikey":PUBLIC_KEY,"Content-Type":"application/json",...(jwt?{"Authorization":"Bearer "+jwt}:{})},
    body:body===null?undefined:JSON.stringify(body),
  });
  const data=await res.json().catch(()=>({}));
  if(!res.ok)throw Error(data.msg||data.error_description||data.message||"Authentication failed");
  return data;
}
async function api(action,params){
  const res=await fetch(base+"/functions/v1/lockvyn-admin",{method:"POST",cache:"no-store",
    headers:{"Authorization":"Bearer "+accessToken,"Content-Type":"application/json"},
    body:JSON.stringify({action,...params})});
  const data=await res.json();
  if(!res.ok)throw Error(data.error||"Request failed");
  return data;
}
const ANALYTICS_METRICS=["signups","active_app_versions","update_uptake","sync_reliability","resource_usage"];
const ANALYTICS_LABELS={signups:"Signups",active_app_versions:"Active versions",update_uptake:"Update uptake",
  sync_reliability:"Sync reliability",resource_usage:"Resource use"};
let analyticsRows=[];
function analyticsToday(){return new Date().toISOString().slice(0,10);}
function analyticsDay(value){if(!/^\d{4}-\d{2}-\d{2}$/.test(value||""))return null;
  const n=Date.parse(value+"T00:00:00Z");return Number.isFinite(n)&&new Date(n).toISOString().slice(0,10)===value?n:null;}
function analyticsCsv(value){return '"'+String(value??'').replaceAll('"','""')+'"';}
function clearAnalytics(){
  analyticsRows=[];get("analytics-chart").replaceChildren();get("analytics-table-body").replaceChildren();
  get("analytics-export").disabled=true;
}
function renderAnalytics(data){
  analyticsRows=data.rows;
  const body=get("analytics-table-body");body.replaceChildren();
  for(const row of data.rows){
    const tr=document.createElement("tr");
    for(const value of [row.day,ANALYTICS_LABELS[row.metric],row.platform,row.appVersion,
      row.outcome||"—",row.resourceBucket||"—",row.users,row.events]){
      const td=document.createElement("td");td.textContent=String(value);tr.append(td);
    }
    body.append(tr);
  }
  const chart=get("analytics-chart");chart.replaceChildren();
  const byDay=new Map();
  for(const row of data.rows){
    if(!byDay.has(row.day))byDay.set(row.day,Object.fromEntries(ANALYTICS_METRICS.map(metric=>[metric,0])));
    byDay.get(row.day)[row.metric]+=row.events;
  }
  const maxima=Object.fromEntries(ANALYTICS_METRICS.map(metric=>[metric,
    Math.max(1,...Array.from(byDay.values(),totals=>totals[metric]))]));
  for(const [day,totals] of [...byDay.entries()].sort((a,b)=>a[0].localeCompare(b[0]))){
    const li=document.createElement("li"),dateLabel=document.createElement("strong"),bars=document.createElement("div");
    dateLabel.textContent=day;bars.className="analytics-bars";
    for(const metric of ANALYTICS_METRICS){
      const item=document.createElement("span"),label=document.createElement("span"),bar=document.createElement("span");
      item.className="analytics-series";label.textContent=ANALYTICS_LABELS[metric]+": "+totals[metric];
      bar.className="analytics-bar";bar.setAttribute("aria-hidden","true");
      bar.style.width=(totals[metric]===0?0:Math.max(4,Math.round(totals[metric]/maxima[metric]*100)))+"%";
      item.append(label,bar);bars.append(item);
    }
    li.append(dateLabel,bars);chart.append(li);
  }
  if(!data.rows.length){
    const empty=document.createElement("li");empty.textContent="No cohorts met the minimum of five opted-in accounts.";chart.append(empty);
  }
  get("analytics-export").disabled=!data.rows.length;
}
async function loadPrivacyAnalytics(){
  if(!accessToken)return;
  const fromDate=get("analytics-from").value,toDate=get("analytics-to").value;
  const from=analyticsDay(fromDate),to=analyticsDay(toDate),today=analyticsDay(analyticsToday());
  if(from===null||to===null||today===null||from>to||to>today||from<today-89*86400000||to-from>89*86400000){
    get("analytics-state").textContent="Choose dates within the last 90 days.";clearAnalytics();return;
  }
  get("analytics-state").textContent="Loading privacy-safe aggregates…";
  try{
    const data=await api("privacyAnalytics",{fromDate,toDate});
    if(data.minimumCohort!==5||!Array.isArray(data.rows))throw Error("Invalid aggregate analytics response");
    renderAnalytics(data);
    get("analytics-state").textContent=data.rows.length
      ?"Loaded "+data.rows.length+" aggregate rows. Groups under five accounts are hidden."
      :"No reportable groups met the five-account minimum for these dates.";
  }catch(error){
    clearAnalytics();get("analytics-state").textContent="Could not load analytics. Retry when connected.";
    alertText(error.message,true);
  }
}
get("refresh-analytics").addEventListener("click",loadPrivacyAnalytics);
get("apply-analytics-filters").addEventListener("click",loadPrivacyAnalytics);
get("analytics-export").addEventListener("click",()=>{
  if(!analyticsRows.length)return;
  const columns=["day","metric","platform","appVersion","outcome","resourceBucket","users","events"];
  const csv=[columns.join(","),...analyticsRows.map(row=>columns.map(key=>analyticsCsv(row[key])).join(","))].join(String.fromCharCode(13,10));
  const url=URL.createObjectURL(new Blob(["\ufeff",csv],{type:"text/csv;charset=utf-8"}));
  const link=document.createElement("a");link.href=url;
  link.download="lockvyn-analytics-"+get("analytics-from").value+"-"+get("analytics-to").value+".csv";
  document.body.append(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(url),0);
});
const analyticsEnd=analyticsToday(),analyticsStart=new Date(Date.now()-29*86400000).toISOString().slice(0,10);
get("analytics-from").value=analyticsStart;get("analytics-to").value=analyticsEnd;
get("analytics-from").max=analyticsEnd;get("analytics-to").max=analyticsEnd;
get("analytics-from").min=new Date(Date.now()-89*86400000).toISOString().slice(0,10);
get("analytics-to").min=get("analytics-from").min;

function ticketApi(action,params={}){
  const resPromise=fetch(base+"/functions/v1/lockvyn-admin/tickets",{method:"POST",cache:"no-store",
    headers:{"Authorization":"Bearer "+accessToken,"Content-Type":"application/json"},
    body:JSON.stringify({action,...params})});
  return resPromise.then(async res=>{
    const data=await res.json().catch(()=>({}));
    if(!res.ok)throw Error(data.error||"Ticket request failed");
    return data;
  });
}
function ticketActorId(){
  try{
    const bits=accessToken.split(".")[1].replace(/-/g,"+").replace(/_/g,"/");
    return JSON.parse(atob(bits)).sub||"";
  }catch{return "";}
}
let supportTicketRows=[],selectedSupportTicket=null;
function updateTicketWriteControls(){
  const id=selectedSupportTicket?.id||"";
  const enabled=Boolean(id&&get("ticket-confirm-id").value.trim()===id);
  for(const control of ["ticket-assign-me","ticket-unassign","ticket-escalate","ticket-save-status","ticket-send-reply"])
    get(control).disabled=!enabled;
}
function renderTicketQueue(items){
  supportTicketRows=items;
  const list=get("ticket-list");list.replaceChildren();
  for(const item of items){
    if(!item||typeof item.id!=="string"||typeof item.subject!=="string")continue;
    const row=document.createElement("li");
    const button=document.createElement("button");button.type="button";
    button.textContent=item.subject+" · "+item.status+" · "+item.priority;
    button.setAttribute("aria-label","Open "+item.subject+" · "+item.status+" · "+item.priority);
    button.addEventListener("click",()=>loadSupportTicket(item.id));
    const detail=document.createElement("small");
    detail.textContent=dateTime(item.updatedAt)+" · "+item.messageCount+" messages · "+
      (item.assignedTo?(item.assignedTo===ticketActorId()?"assigned to you":"assigned"):"unassigned");
    row.append(button,detail);list.append(row);
  }
  if(!list.children.length){
    const empty=document.createElement("li");empty.textContent="No support requests are waiting in the queue.";list.append(empty);
  }
}
async function loadSupportTickets(preferredId=selectedSupportTicket?.id||""){
  if(!accessToken)return;
  const state=get("ticket-queue-state");state.textContent="Loading ticket queue…";
  try{
    const data=await ticketApi("ticketAdminList",{});
    if(!Array.isArray(data.tickets))throw Error("Invalid ticket queue response");
    renderTicketQueue(data.tickets);
    state.textContent="Loaded "+data.tickets.length+" private support requests. User and administrator email addresses are not included.";
    const selected=data.tickets.find(item=>item.id===preferredId)||data.tickets[0];
    if(selected)await loadSupportTicket(selected.id);
    else clearSelectedSupportTicket();
  }catch(error){
    get("ticket-list").replaceChildren();
    state.textContent="Could not load support tickets. Retry when connected.";
    alertText(error.message,true);
  }
}
function clearSelectedSupportTicket(){
  selectedSupportTicket=null;
  get("ticket-selected-state").textContent="Choose a request to review its replies.";
  get("ticket-selected-id").textContent="Ticket IDs appear after selection.";
  get("ticket-thread").textContent="No request selected.";
  get("ticket-diagnostics").replaceChildren();
  const dt=document.createElement("dt"),dd=document.createElement("dd");
  dt.textContent="State";dd.textContent="No diagnostics loaded.";
  get("ticket-diagnostics").append(dt,dd);
  get("ticket-confirm-id").value="";
  get("ticket-reply").value="";
  updateTicketWriteControls();
}
async function loadSupportTicket(id){
  if(!accessToken)return;
  try{
    const data=await ticketApi("ticketAdminGet",{ticketId:id});
    const ticket=data.ticket;
    if(!ticket||ticket.id!==id||!Array.isArray(data.messages))throw Error("Invalid support ticket response");
    selectedSupportTicket=ticket;
    get("ticket-selected-state").textContent=ticket.subject+" · "+ticket.status+" · "+ticket.priority;
    get("ticket-selected-id").textContent="Selected ticket ID: "+ticket.id+" · Type this exact ID to enable agent changes.";
    get("ticket-confirm-id").value="";
    get("ticket-status").value=ticket.status;
    get("ticket-priority").value=ticket.priority;
    get("ticket-reply").value="";
    const thread=get("ticket-thread");thread.replaceChildren();
    for(const message of data.messages){
      const entry=document.createElement("p");
      const sender=document.createElement("strong");
      sender.textContent=message.senderRole==="agent"?"Support agent · ":"Requester · ";
      const body=document.createElement("span");body.textContent=String(message.body||"");
      const when=document.createElement("small");when.textContent=" · "+dateTime(message.createdAt);
      entry.append(sender,body,when);thread.append(entry);
    }
    if(!data.messages.length)thread.textContent="No messages yet.";
    const diagnostics=data.diagnostics?.payload||null,host=get("ticket-diagnostics");
    host.replaceChildren();
    if(diagnostics&&typeof diagnostics==="object"&&!Array.isArray(diagnostics)){
      for(const key of ["platform","appVersion","osVersion","errorCode"]){
        if(typeof diagnostics[key]!=="string")continue;
        const term=document.createElement("dt"),value=document.createElement("dd");
        term.textContent=key;value.textContent=diagnostics[key];host.append(term,value);
      }
    }
    if(!host.children.length){
      const term=document.createElement("dt"),value=document.createElement("dd");
      term.textContent="State";value.textContent="No consented diagnostics attached.";
      host.append(term,value);
    }
    updateTicketWriteControls();
  }catch(error){get("ticket-selected-state").textContent="Could not load this support request.";alertText(error.message,true);}
}
async function changeSupportTicket(action,extra={}){
  if(!selectedSupportTicket)return;
  const id=selectedSupportTicket.id;
  if(get("ticket-confirm-id").value.trim()!==id){alertText("Type the selected ticket ID to confirm.",true);return;}
  try{
    await ticketApi(action,{ticketId:id,confirmId:id,...extra});
    alertText("Support ticket updated.");
    await loadSupportTickets(id);
  }catch(error){alertText(error.message,true);}
}
get("refresh-tickets").addEventListener("click",()=>{if(accessToken)loadSupportTickets()});
get("ticket-confirm-id").addEventListener("input",updateTicketWriteControls);
get("ticket-assign-me").addEventListener("click",()=>changeSupportTicket("ticketAdminAssign",{assigneeId:ticketActorId()}));
get("ticket-unassign").addEventListener("click",()=>changeSupportTicket("ticketAdminAssign",{assigneeId:null}));
get("ticket-escalate").addEventListener("click",()=>changeSupportTicket("ticketAdminEscalate",{}));
get("ticket-save-status").addEventListener("click",()=>changeSupportTicket("ticketAdminStatus",{
  status:get("ticket-status").value,priority:get("ticket-priority").value
}));
get("ticket-send-reply").addEventListener("click",()=>{
  const body=get("ticket-reply").value.trim();
  if(!body){alertText("Enter a reply before sending.",true);return;}
  changeSupportTicket("ticketAdminReply",{body});
});

function jwtAal(jwt){try{const bits=jwt.split(".")[1].replace(/-/g,"+").replace(/_/g,"/");return JSON.parse(atob(bits)).aal||"";}catch{return "";}}
function applyRoleVisibility(){
  for(const section of document.querySelectorAll("[data-roles]")){
    const allowed=(section.dataset.roles||"").split(/\s+/).includes(currentRole||"");
    section.classList.toggle("role-hidden",!allowed);
  }
  visible("role-bootstrap",!currentRole&&roleStatusData?.initialized===false);
  visible("role-manager",currentRole==="owner");
  get("skip-link").href=currentRole==="owner"||currentRole==="support"?"#directory-section":"#role-status";
}
function renderRoleAssignments(rows){
  const list=get("role-list");list.replaceChildren();
  for(const row of rows||[]){
    const item=document.createElement("li");
    item.textContent=row.role+" · "+row.userId+" · assigned "+date(row.assignedAt);
    list.append(item);
  }
  if(!rows?.length){const item=document.createElement("li");item.textContent="No administrator roles assigned.";list.append(item);}
}
function renderRoleAudit(rows){
  const list=get("role-audit");list.replaceChildren();
  for(const row of rows||[]){
    const item=document.createElement("li");
    item.textContent=row.action+" · "+(row.roleBefore||"none")+" → "+(row.roleAfter||"none")+
      " · target "+row.targetId+" · actor "+row.actorId+" · "+date(row.createdAt);
    list.append(item);
  }
  if(!rows?.length){const item=document.createElement("li");item.textContent="No role changes recorded.";list.append(item);}
}
async function loadRoleStatus(){
  const info=get("role-status-text");
  info.textContent="Checking your assigned role…";
  try{
    roleStatusData=await api("roleStatus",{});
    currentRole=typeof roleStatusData.role==="string"?roleStatusData.role:null;
    get("role-user-id").textContent="Account UUID: "+roleStatusData.userId;
    get("role-bootstrap-user").textContent=roleStatusData.userId;
    info.textContent=currentRole
      ?"Your administrator role is "+currentRole+". Permissions are enforced on every server request."
      :roleStatusData.initialized
        ?"No role is assigned to this account. Ask an owner to grant the minimum role needed."
        :"No roles exist yet. An approved administrator can initialize the first owner role.";
    applyRoleVisibility();
    if(currentRole==="owner"){
      const [roles,audit]=await Promise.all([api("roleList",{}),api("roleAudit",{})]);
      renderRoleAssignments(roles);renderRoleAudit(audit);
      get("role-manager-state").textContent=roles.length+" assigned roles · latest "+audit.length+" audit events";
    }
  }catch(error){
    roleStatusData=null;currentRole=null;applyRoleVisibility();
    info.textContent="Could not verify administrator role. Dashboard actions remain unavailable.";
    alertText(error.message,true);
  }
}
async function openDashboard(){
  visible("login",false);visible("mfa",false);visible("dashboard",true);visible("logout",true);
  await loadRoleStatus();
  if(!currentRole)return;
  const tasks=[];
  if(["owner","support"].includes(currentRole))tasks.push(loadSupportTickets(),loadPage(1),loadHistory());
  if(["owner","security"].includes(currentRole))tasks.push(loadSecurityEvents(),loadPrivacyAnalytics());
  if(["owner","release"].includes(currentRole))tasks.push(loadReleaseManager(),loadAnnouncementManager());
  await Promise.all(tasks);
}
async function setupMfa(){
  const user=await authFetch("user",null,"GET",accessToken);
  const factors=Array.isArray(user.factors)?user.factors:[];
  pendingFactor=factors.find(f=>f.factor_type==="totp" && f.status==="verified");
  if(!pendingFactor)throw Error("Enable 2FA on this account in Lockvyn before opening the admin dashboard.");
  const response=await authFetch("factors/"+encodeURIComponent(pendingFactor.id)+"/challenge",{}, "POST",accessToken);
  challengeId=response.id;
  visible("login",false);visible("mfa",true);
}
get("login-form").addEventListener("submit",async e=>{
  e.preventDefault();alertText("");
  try{
    const out=await authFetch("token?grant_type=password",{
      email:get("email").value.trim(),password:get("password").value,
    });
    get("password").value="";
    accessToken=out.access_token;
    if(jwtAal(accessToken)==="aal2")await openDashboard();else await setupMfa();
  }catch(error){alertText(error.message,true);}
});
get("mfa-form").addEventListener("submit",async e=>{
  e.preventDefault();alertText("");
  try{
    const out=await authFetch("factors/"+encodeURIComponent(pendingFactor.id)+"/verify",
      {challenge_id:challengeId,code:get("code").value.trim()},"POST",accessToken);
    accessToken=out.access_token;
    get("code").value="";
    if(jwtAal(accessToken)!=="aal2")throw Error("Verification did not establish AAL2.");
    await openDashboard();
  }catch(error){alertText(error.message,true);}
});
function td(row,value){const cell=document.createElement("td");cell.textContent=value;row.append(cell);return cell;}
function statusCell(row,value,state){
  const cell=document.createElement("td"),badge=document.createElement("span");
  badge.className="status "+state;badge.textContent=value;cell.append(badge);row.append(cell);
}
let matching=0,loadSequence=0,searchDelay=null;
function display(){
  const body=get("users");body.replaceChildren();
  for(const user of accounts){
    const row=document.createElement("tr");
    td(row,user.email);
    td(row,date(user.createdAt));
    td(row,date(user.lastSignInAt));
    statusCell(row,user.confirmed?"Verified":"Pending",user.confirmed?"yes":"no");
    statusCell(row,user.mfa===true?"Enabled":user.mfa===false?"Off":"Unknown",
      user.mfa===true?"yes":user.mfa===false?"no":"unknown");
    const action=document.createElement("td"),btn=document.createElement("button");
    btn.type="button";
    btn.textContent=user.isSelf?"Reset my cloud password":"Send recovery email";
    btn.disabled=!user.confirmed;
    btn.addEventListener("click",()=>sendRecovery(user));
    action.append(btn);
    if(!user.isSelf){
      const manage=document.createElement("button");
      manage.type="button";manage.textContent="Manage";manage.style.marginLeft="8px";
      manage.setAttribute("aria-label","Manage cloud access and support for "+user.email);
      manage.addEventListener("click",()=>openSupport(user,manage));
      action.append(manage);
    }
    row.append(action);body.append(row);
  }
  visible("empty",accounts.length===0);
}
function setLoading(value){
  get("directory-table").setAttribute("aria-busy",String(value));
  get("prev").disabled=value||page<=1;
  get("next").disabled=value||page*30>=matching;
  get("directory-state").textContent=value?"Loading accounts…":"";
}
async function loadPage(n){
  if(deviceRevocationBusy)return;
  const seq=++loadSequence;
  setLoading(true);
  try{
    const data=await api("list",{
      page:n,search:get("search").value.trim(),
      emailState:get("email-filter").value,
      mfaState:get("mfa-filter").value,
    });
    if(seq!==loadSequence||!accessToken)return;
    if(!Array.isArray(data.users)||typeof data.total!=="number"||
       typeof data.matching!=="number"||!data.stats)throw Error("Invalid directory response");
    page=n;accounts=data.users;matching=data.matching;
    get("total").textContent=data.total.toLocaleString();
    get("verified-stat").textContent=data.stats.verified.toLocaleString();
    get("mfa-stat").textContent=data.stats.mfaEnabled.toLocaleString();
    get("active-stat").textContent=data.stats.active30Days.toLocaleString();
    get("filtered-count").textContent=matching.toLocaleString()+" matching / "+
      data.total.toLocaleString()+" total";
    const pageCount=Math.max(1,Math.ceil(matching/30));
    get("page-label").textContent="Page "+page+" of "+pageCount;
    visible("retry-directory",false);display();alertText("");
  }catch(error){
    if(seq!==loadSequence)return;
    accounts=[];matching=0;
    get("users").replaceChildren();visible("empty",false);
    get("directory-state").textContent="Unable to load accounts. Retry or adjust filters.";
    visible("retry-directory",true);alertText(error.message,true);
  }finally{
    if(seq===loadSequence){
      const state=get("directory-state").textContent;
      setLoading(false);
      if(state.startsWith("Unable"))get("directory-state").textContent=state;
    }
  }
}
function scheduleSearch(){
  clearTimeout(searchDelay);
  searchDelay=setTimeout(()=>{if(accessToken)loadPage(1)},350);
}
async function sendRecovery(user){
  const message=user.isSelf
    ? "Send yourself a cloud account password reset email? Your encrypted vault master passphrase will not change."
    : "Send a cloud account password recovery email to "+user.email+"? This will not change their master passphrase.";
  if(!confirm(message))return;
  try{await api("recover",{id:user.id});alertText("Recovery email requested for "+user.email+". Check the inbox and spam folder.");}
  catch(error){alertText(error.message,true);}
}
get("retry-directory").addEventListener("click",()=>{if(accessToken)loadPage(page)});
let supportTarget=null,pendingSupport=null,supportTrigger=null;
async function loadSupportStatus(){
  if(!supportTarget)return;
  const id=supportTarget.id;
  const state=get("support-cloud-state");
  state.textContent="Checking server-side cloud login status…";
  try{
    const data=await api("supportStatus",{id});
    if(!supportTarget||supportTarget.id!==id)return;
    const item=data.cloudLogin||{};
    state.textContent=item.state==="suspended"
      ? "Cloud sign-in suspended until "+new Date(item.until).toLocaleString()+". Existing JWTs may remain valid; offline vaults are unaffected."
      : item.state==="active"
      ? "Cloud login currently active. This does not indicate that all device sessions have signed out."
      : "Cloud login status could not be determined. Do not assume the account is active.";
  }catch(error){
    if(supportTarget&&supportTarget.id===id)
      state.textContent="Could not verify cloud login status: "+error.message;
  }
}
get("refresh-support-status").addEventListener("click",loadSupportStatus);
let pendingDevice=null,pendingDeviceTrigger=null,deviceRevocationBusy=false,cloudSyncBusy=false,pendingSyncSuspend=null,pendingSyncTrigger=null,cloudDeviceLoadSequence=0;
function clearDeviceConfirmation({restoreFocus=false,force=false}={}){
  if(deviceRevocationBusy&&!force)return;
  const trigger=pendingDeviceTrigger;
  pendingDevice=null;pendingDeviceTrigger=null;
  get("device-revoke-email").value="";
  get("device-revoke-otp").value="";
  visible("device-revoke-panel",false);
  if(restoreFocus){
    if(trigger?.isConnected)trigger.focus();
    else get("cloud-device-heading").focus();
  }
}
function setDeviceRevocationBusy(busy){
  deviceRevocationBusy=busy;
  const panel=get("device-revoke-panel"),form=get("device-revoke-form");
  panel.setAttribute("aria-busy",String(busy));
  for(const control of form.querySelectorAll("input,button"))control.disabled=busy;
  get("refresh-cloud-devices").disabled=busy;
  get("apply-cloud-device-filters").disabled=busy;
  get("pause-cloud-sync").disabled=busy;
  get("resume-cloud-sync").disabled=busy;
  get("close-support").disabled=busy;
  get("logout").disabled=busy;
  for(const control of get("cloud-device-list").querySelectorAll("button"))control.disabled=busy;
  if(busy)get("cloud-device-state").textContent="Revocation is in progress. Keep this page open for the result.";
}
const SYNC_OUTCOME_LABELS={success:"Successful",failure:"Failed",unknown:"Not reported"};
const SYNC_ERROR_LABELS={quota_exceeded:"Storage limit reached",sync_suspended:"Sync paused",
  network:"Network issue",auth:"Sign-in required",transport:"Cloud service issue",unknown:"Other issue"};
function safeBytes(value){return Number.isSafeInteger(value)&&value>=0?value:null;}
function formatBytes(value){
  if(value<1024)return value+" bytes";
  return (value/(1024*1024)).toFixed(2)+" MiB";
}
function dateTime(value){
  return typeof value==="string"&&Number.isFinite(Date.parse(value))
    ?new Date(value).toLocaleString():"—";
}
function renderCloudDevices(devices){
  const list=get("cloud-device-list");list.replaceChildren();
  for(const item of devices){
    const li=document.createElement("li");
    const title=document.createElement("h4");
    title.textContent=item.display_name||"Unnamed app session";
    li.append(title);
    const platform=document.createElement("p");
    platform.textContent=[item.platform,item.os_version?"OS "+item.os_version:"",item.app_version?"App "+item.app_version:""]
      .filter(Boolean).join(" · ")||"Platform details not reported";
    li.append(platform);
    const dates=document.createElement("p");
    dates.textContent="Registered "+dateTime(item.registered_at)+
      " · Last successful sync "+dateTime(item.last_sync_at);
    li.append(dates);
    const activity=document.createElement("p");
    const outcome=Object.hasOwn(SYNC_OUTCOME_LABELS,item.last_sync_outcome)
      ?SYNC_OUTCOME_LABELS[item.last_sync_outcome]:"Not reported";
    const bytes=safeBytes(item.last_sync_bytes);
    const error=Object.hasOwn(SYNC_ERROR_LABELS,item.last_sync_error_code)
      ?SYNC_ERROR_LABELS[item.last_sync_error_code]:"";
    activity.textContent="Last attempt "+dateTime(item.last_sync_attempt_at)+" · "+outcome+
      (bytes===null?"":" · "+formatBytes(bytes))+(error?" · "+error:"");
    li.append(activity);
    const state=document.createElement("p");
    state.textContent=item.revoked_at
      ?"Revoked "+dateTime(item.revoked_at)
      :"Cloud session not revoked by Lockvyn";
    li.append(state);
    if(!item.revoked_at){
      const button=document.createElement("button");
      button.type="button";button.textContent="Revoke cloud access for this session";
      button.setAttribute("aria-label","Revoke cloud access for "+
        (item.display_name||"unnamed app session")+" for "+supportTarget.email);
      button.disabled=deviceRevocationBusy||cloudSyncBusy;
      button.addEventListener("click",()=>{
        if(deviceRevocationBusy||cloudSyncBusy)return;
        pendingDevice=item;pendingDeviceTrigger=button;
        get("device-revoke-summary").textContent=
          "This blocks "+(item.display_name||"this app session")+
          " from cloud snapshots and pairing for "+supportTarget.email+
          ". Offline data on the app remains.";
        get("device-revoke-email").value="";
        get("device-revoke-otp").value="";
        visible("device-revoke-panel",true);
        get("device-revoke-email").focus();
      });
      li.append(button);
    }
    list.append(li);
  }
  if(!devices.length){
    const empty=document.createElement("li");
    empty.textContent="No registered sessions match these filters.";
    list.append(empty);
  }
}
async function loadCloudDevices(){
  if(!supportTarget)return;
  const id=supportTarget.id,state=get("cloud-device-state"),sequence=++cloudDeviceLoadSequence;
  state.textContent="Loading registered cloud sessions and sync activity…";
  try{
    const selectedSince=get("cloud-device-since").value;
    const data=await api("deviceList",{
      id,
      platform:get("cloud-device-platform").value||null,
      outcome:get("cloud-device-outcome").value,
      sinceDays:selectedSince==="all"?null:Number(selectedSince),
      deviceQuery:get("cloud-device-query").value.trim()||null,
    });
    if(sequence!==cloudDeviceLoadSequence||!supportTarget||supportTarget.id!==id)return;
    if(!Array.isArray(data.devices)||typeof data.suspended!=="boolean")throw Error("Invalid sync inventory response");
    for(const key of ["snapshotCount","ciphertextBytes","quotaBytes","remainingBytes"])
      if(!Number.isSafeInteger(data[key])||data[key]<0)throw Error("Invalid sync usage response");
    renderCloudDevices(data.devices);
    get("cloud-sync-usage").textContent="Encrypted cloud snapshots: "+data.snapshotCount+
      " of "+data.maxSnapshots+" · "+formatBytes(data.ciphertextBytes)+" used of "+
      formatBytes(data.quotaBytes)+" · "+formatBytes(data.remainingBytes)+" remaining";
    get("cloud-sync-state").textContent=data.suspended
      ?"Cloud sync is paused. Offline vault data and encrypted cloud snapshots are preserved."
      :"Cloud sync is active. Pausing it will preserve offline vault data and encrypted cloud snapshots.";
    visible("pause-cloud-sync",!data.suspended);
    visible("resume-cloud-sync",data.suspended);
    state.textContent=data.devices.length
      ?data.devices.length+" registered session"+(data.devices.length===1?"":"s")+" match these filters."
      :"No registered sessions match these filters.";
  }catch(error){
    if(sequence===cloudDeviceLoadSequence&&supportTarget&&supportTarget.id===id){
      state.textContent="Could not load cloud sync activity: "+error.message;
      get("cloud-sync-usage").textContent="Sync usage is unavailable.";
      get("cloud-sync-state").textContent="Cloud sync state is unavailable.";
    }
  }
}
get("refresh-cloud-devices").addEventListener("click",loadCloudDevices);
get("apply-cloud-device-filters").addEventListener("click",loadCloudDevices);
get("cancel-device-revoke").addEventListener("click",()=>clearDeviceConfirmation({restoreFocus:true}));
get("device-revoke-form").addEventListener("submit",async e=>{
  e.preventDefault();
  if(deviceRevocationBusy)return;
  const selected=pendingDevice,target=supportTarget;
  if(!selected||!target){alertText("Select an active registered session first.",true);return;}
  const email=get("device-revoke-email").value.trim();
  if(email.toLowerCase()!==target.email.toLowerCase()){
    alertText("The confirmation email does not match.",true);return;
  }
  if(!confirm("Revoke cloud access for this app session? Offline vault data stays on the app."))return;
  setDeviceRevocationBusy(true);
  try{
    const user=await authFetch("user",null,"GET",accessToken);
    const factor=(Array.isArray(user.factors)?user.factors:[])
      .find(x=>x.factor_type==="totp"&&x.status==="verified");
    if(!factor)throw Error("No verified administrator authenticator factor.");
    const challenge=await authFetch("factors/"+encodeURIComponent(factor.id)+"/challenge",{},"POST",accessToken);
    const fresh=await authFetch("factors/"+encodeURIComponent(factor.id)+"/verify",{
      challenge_id:challenge.id,code:get("device-revoke-otp").value.trim(),
    },"POST",accessToken);
    get("device-revoke-otp").value="";
    if(jwtAal(fresh.access_token)!=="aal2")throw Error("Fresh MFA verification did not succeed.");
    accessToken=fresh.access_token;
    await api("deviceRevoke",{id:target.id,deviceId:selected.device_id,confirmEmail:email});
    clearDeviceConfirmation({force:true});
    await loadCloudDevices();
    get("cloud-device-state").focus();
    alertText("Cloud access revoked for that signed-in app session. Offline vault data remains.");
  }catch(error){
    get("device-revoke-otp").value="";
    alertText(error.message,true);
  }finally{setDeviceRevocationBusy(false);}
});
function setCloudSyncBusy(busy){
  cloudSyncBusy=busy;
  const form=get("cloud-sync-form");
  get("cloud-sync-confirm").setAttribute("aria-busy",String(busy));
  for(const control of form.querySelectorAll("input,select,button"))control.disabled=busy;
  get("refresh-cloud-devices").disabled=busy;
  get("apply-cloud-device-filters").disabled=busy;
  get("pause-cloud-sync").disabled=busy;
  get("resume-cloud-sync").disabled=busy;
  get("close-support").disabled=busy;
  get("logout").disabled=busy;
  for(const control of get("cloud-device-list").querySelectorAll("button"))control.disabled=busy;
  if(busy)get("cloud-sync-state").textContent="Saving cloud sync state. Keep this page open for the result.";
}
function openCloudSyncControl(suspend,trigger){
  if(!supportTarget||deviceRevocationBusy||cloudSyncBusy)return;
  pendingSyncSuspend=suspend;pendingSyncTrigger=trigger;
  get("cloud-sync-confirm-summary").textContent=(suspend
    ?"Pause encrypted cloud snapshot sync for "
    :"Resume encrypted cloud snapshot sync for ")+supportTarget.email+
    "? Offline vault data and encrypted cloud snapshots will be preserved. A fresh authenticator code and typed email are required.";
  get("cloud-sync-email").value="";get("cloud-sync-otp").value="";
  visible("cloud-sync-confirm",true);get("cloud-sync-email").focus();
}
function closeCloudSyncConfirmation(restoreFocus=false){
  if(cloudSyncBusy)return;
  const trigger=pendingSyncTrigger;
  pendingSyncSuspend=null;pendingSyncTrigger=null;
  get("cloud-sync-email").value="";get("cloud-sync-otp").value="";
  visible("cloud-sync-confirm",false);
  if(restoreFocus&&trigger?.isConnected)trigger.focus();
}
get("pause-cloud-sync").addEventListener("click",e=>openCloudSyncControl(true,e.currentTarget));
get("resume-cloud-sync").addEventListener("click",e=>openCloudSyncControl(false,e.currentTarget));
get("cancel-cloud-sync").addEventListener("click",()=>closeCloudSyncConfirmation(true));
get("cloud-sync-form").addEventListener("submit",async e=>{
  e.preventDefault();
  if(pendingSyncSuspend===null||!supportTarget||deviceRevocationBusy||cloudSyncBusy)return;
  const suspend=pendingSyncSuspend,target=supportTarget,email=get("cloud-sync-email").value.trim();
  if(email.toLowerCase()!==target.email.toLowerCase()){
    alertText("The confirmation email does not match.",true);return;
  }
  if(!confirm((suspend?"Pause":"Resume")+" cloud sync for "+target.email+
      "? Offline vault data and encrypted cloud snapshots are preserved."))return;
  const button=get("cloud-sync-form").querySelector('button[type="submit"]');
  button.disabled=true;setCloudSyncBusy(true);
  try{
    const user=await authFetch("user",null,"GET",accessToken);
    const factor=(Array.isArray(user.factors)?user.factors:[])
      .find(x=>x.factor_type==="totp"&&x.status==="verified");
    if(!factor)throw Error("No verified administrator authenticator factor.");
    const challenge=await authFetch("factors/"+encodeURIComponent(factor.id)+"/challenge",{},"POST",accessToken);
    const fresh=await authFetch("factors/"+encodeURIComponent(factor.id)+"/verify",{
      challenge_id:challenge.id,code:get("cloud-sync-otp").value.trim(),
    },"POST",accessToken);
    get("cloud-sync-otp").value="";
    if(jwtAal(fresh.access_token)!=="aal2")throw Error("Fresh MFA verification did not succeed.");
    accessToken=fresh.access_token;
    const done=await api("syncControl",{
      id:target.id,suspend,reasonCode:get("cloud-sync-reason").value,confirmEmail:email,
    });
    pendingSyncSuspend=null;pendingSyncTrigger=null;
    visible("cloud-sync-confirm",false);
    setCloudSyncBusy(false);
    await loadCloudDevices();
    get("cloud-sync-state").focus();
    alertText(done.warning);
  }catch(error){
    get("cloud-sync-otp").value="";
    alertText(error.message,true);
  }finally{
    setCloudSyncBusy(false);button.disabled=false;
  }
});
function openSupport(user,trigger=null){
  if(deviceRevocationBusy||cloudSyncBusy){alertText("A cloud control is still being processed. Wait for its result.",true);return;}
  closeCloudSyncConfirmation();
  supportTrigger=trigger;
  supportTarget={id:user.id,email:user.email,isSelf:user.isSelf};
  pendingSupport=null;visible("support-confirm",false);
  get("support-target").textContent="Selected: "+user.email;
  visible("account-support",true);
  void loadSupportStatus();
  clearDeviceConfirmation();
  get("cloud-device-list").replaceChildren();
  get("cloud-device-state").textContent="Loading registered cloud sessions…";
  void loadCloudDevices();
  get("account-support").scrollIntoView({block:"start",behavior:"instant"});
  get("support-heading").focus();
}
function closeSupport(){
  if(deviceRevocationBusy||cloudSyncBusy){alertText("A cloud control is still being processed. Wait for its result.",true);return;}
  const trigger=supportTrigger;
  closeCloudSyncConfirmation();
  supportTrigger=null;
  supportTarget=null;pendingSupport=null;
  get("support-email").value="";get("support-otp").value="";
  clearDeviceConfirmation();get("cloud-device-list").replaceChildren();
  get("cloud-device-state").textContent="Registered sessions have not been loaded.";
  visible("support-confirm",false);visible("account-support",false);
  get("support-cloud-state").textContent="Cloud login status has not been checked.";
  if(trigger?.isConnected)trigger.focus();
  else get("directory-heading").focus();
}
get("close-support").addEventListener("click",closeSupport);
get("support-kind").addEventListener("change",()=>{pendingSupport=null;visible("support-confirm",false)});
get("support-reason").addEventListener("change",()=>{pendingSupport=null;visible("support-confirm",false)});
get("prepare-support").addEventListener("click",async()=>{
  if(!supportTarget||supportTarget.isSelf)return;
  const kind=get("support-kind").value,reason=get("support-reason").value;
  try{
    const prepared=await api("supportPrepare",{id:supportTarget.id,kind,reason});
    pendingSupport={caseId:prepared.caseId,id:supportTarget.id,kind,reason,email:prepared.targetEmail};
    get("support-summary").textContent="Action: "+kind+". Confirm exactly "+
      prepared.targetEmail+". This case expires in 5 minutes. No action occurs until confirmation.";
    visible("support-confirm",true);
    get("support-email").value="";get("support-otp").value="";
    alertText("Case prepared. Reconfirm target email and your authenticator code.");
  }catch(e){alertText(e.message,true);}
});
get("support-form").addEventListener("submit",async e=>{
  e.preventDefault();
  const selected=pendingSupport;
  if(!selected){alertText("Prepare a new support case first.",true);return;}
  if(get("support-email").value.trim().toLowerCase()!==selected.email.toLowerCase()){
    alertText("The confirmation email does not match.",true);return;
  }
  if(!confirm("Confirm "+selected.kind+" for "+selected.email+"? This affects CLOUD access only."))return;
  const confirmBtn=get("support-form").querySelector('button[type="submit"]');
  confirmBtn.disabled=true;
  try{
    const user=await authFetch("user",null,"GET",accessToken);
    const factor=(Array.isArray(user.factors)?user.factors:[])
      .find(x=>x.factor_type==="totp"&&x.status==="verified");
    if(!factor)throw Error("No verified administrator authenticator factor.");
    const challenge=await authFetch("factors/"+encodeURIComponent(factor.id)+"/challenge",{},"POST",accessToken);
    const fresh=await authFetch("factors/"+encodeURIComponent(factor.id)+"/verify",{
      challenge_id:challenge.id,code:get("support-otp").value.trim(),
    },"POST",accessToken);
    get("support-otp").value="";
    if(jwtAal(fresh.access_token)!=="aal2")throw Error("Fresh MFA verification did not succeed.");
    accessToken=fresh.access_token;
    const done=await api("supportExecute",{
      id:selected.id,kind:selected.kind,reason:selected.reason,
      caseId:selected.caseId,confirmEmail:get("support-email").value.trim(),
    });
    await loadSupportStatus();await loadPage(page);await loadHistory();
    closeSupport();
    alertText("Support case recorded: "+done.status+". "+done.warning);
  }catch(error){get("support-otp").value="";alertText(error.message,true);}
  finally{confirmBtn.disabled=false;}
});
const SECURITY_EVENT_LABELS={
  account_recovery_requested:"Cloud password recovery requested",
  cloud_device_unregistered:"Cloud session inventory consent withdrawn",
  cloud_session_registered:"New opt-in cloud session registered",
  cloud_session_revoked:"Cloud session access revoked",
  cloud_sync_suspended:"Encrypted cloud sync paused",
  cloud_sync_resumed:"Encrypted cloud sync resumed",
  support_case_opened:"Support case opened",
  support_action_completed:"Cloud support action completed",
  support_action_failed:"Cloud support action failed",
  support_review_pending:"Support action sent for review",
  admin_step_up_denied:"Admin access blocked: verified 2FA required",
  admin_allowlist_denied:"Admin access blocked: account is not authorized",
  admin_inactive_session:"Admin access blocked: Auth session is inactive"
};
function safeCount(value){return Number.isSafeInteger(value)&&value>=0?value:0;}
function shortId(value){return typeof value==="string"&&/^[a-f0-9-]{36}$/i.test(value)?value.slice(0,8):"";}
function renderSecurityEvents(data){
  if(!data||!Array.isArray(data.events)||!data.alerts)throw Error("Invalid security activity response");
  const alerts=data.alerts,host=get("security-alerts");host.replaceChildren();
  const repeated=safeCount(alerts.repeatedDeniedActors15m);
  const cards=[
    ["Repeated denied actors · 15 min",repeated,repeated?"Review repeated admin-access denials.":"No repeated actor reached the alert threshold.",repeated>0],
    ["2FA step-up denials · 15 min",safeCount(alerts.adminStepUpDenials15m),"Valid accounts blocked before admin access.",false],
    ["New opt-in sessions · 24 h",safeCount(alerts.newCloudSessions24h),"Session registrations reported by opted-in clients.",false],
    ["Failed support actions · 24 h",safeCount(alerts.failedSupportActions24h),"Review the corresponding support history.",false]
  ];
  for(const [label,count,hint,warning] of cards){
    const card=document.createElement("article");card.className="security-alert"+(warning?" warning":"");
    const title=document.createElement("small");title.textContent=label;
    const value=document.createElement("strong");value.textContent=String(count);
    const note=document.createElement("small");note.textContent=hint;
    card.append(title,value,note);host.append(card);
  }
  const list=get("security-events");list.replaceChildren();
  for(const item of data.events){
    const row=document.createElement("li");
    const title=document.createElement("strong");
    title.textContent=Object.hasOwn(SECURITY_EVENT_LABELS,item.eventType)?SECURITY_EVENT_LABELS[item.eventType]:"Security activity";
    const meta=document.createElement("p");
    const actor=shortId(item.actorId),subject=shortId(item.subjectId);
    const time=typeof item.occurredAt==="string"&&Number.isFinite(Date.parse(item.occurredAt))
      ?new Date(item.occurredAt).toLocaleString():"Unknown time";
    const details=[];
    if(actor)details.push("actor "+actor);
    if(subject)details.push("account "+subject);
    if(typeof item.detailCode==="string")details.push(item.detailCode.replaceAll("_"," "));
    details.push(time);
    meta.textContent=details.join(" · ");
    row.append(title,meta);list.append(row);
  }
  get("security-events-state").textContent=data.events.length
    ?"Showing "+data.events.length+" recent metadata-only security events."
    :"No retained security events yet.";
}

function renderReleaseCatalog(data){
  window.releaseCatalog=data.releases;window.releasePolicies=data.policies;
  const rows=get("release-rows");rows.replaceChildren();
  for(const release of data.releases){
    const row=document.createElement("tr");
    const identity=document.createElement("td");
    const link=document.createElement("a");link.href=release.url;link.target="_blank";link.rel="noreferrer noopener";
    link.textContent=release.platform+" · "+release.tag;identity.append(link);
    const dateCell=document.createElement("td");dateCell.textContent=dateTime(release.publishedAt);
    const ci=document.createElement("td");
    ci.textContent=release.checks.state==="success"
      ? release.checks.passed+"/"+release.checks.total+" passed"
      : release.checks.state==="failed"?"Failed checks":release.checks.state==="pending"?"Checks pending":"CI status unavailable";
    const signature=document.createElement("td");
    signature.textContent=release.platform==="windows"?release.windowsSignature:
      (release.androidSignature||"Android signing metadata unavailable");
    if(release.androidManifest&&release.androidManifest.signerSha256){
      const fingerprint=document.createElement("small");
      fingerprint.textContent="Signer SHA256 · "+release.androidManifest.signerSha256;
      signature.append(fingerprint);
    }
    const assets=document.createElement("td");
    for(const asset of release.assets){
      const item=document.createElement("small");
      item.textContent=asset.name+(asset.sha256?" · SHA256 "+asset.sha256:" · SHA256 unavailable");
      assets.append(item);
    }
    const changelog=document.createElement("td");
    const details=document.createElement("details");
    const summary=document.createElement("summary");summary.textContent="View notes";
    const notes=document.createElement("pre");notes.className="release-notes";
    notes.textContent=release.changelog||"No changelog supplied.";
    details.append(summary,notes);changelog.append(details);
    row.append(identity,dateCell,ci,signature,assets,changelog);rows.append(row);
  }
  const policies=data.policies||{};
  get("release-policy-state").textContent=["windows","android"].map(platform=>{
    const p=policies[platform]||{};
    return (platform==="windows"?"Windows":"Android")+": "+
      (p.candidateTag||"latest stable")+" · minimum "+(p.minimumVersion||"—")+
      " · rollout "+(Number.isSafeInteger(p.rolloutPercent)?p.rolloutPercent:"—")+"%";
  }).join(" | ");
  populateReleaseCandidates(data.releases);
  applyReleasePolicyFields(data.policies);
  get("save-release-policy").disabled=false;
}
function applyReleasePolicyFields(policies){
  const platform=get("release-platform").value,p=policies?.[platform]||{};
  get("release-minimum-label").firstChild.textContent=platform==="android"?"Minimum Android versionCode":"Minimum Windows version";
  get("release-minimum").value=typeof p.minimumVersion==="string"?p.minimumVersion:(platform==="android"?"0":"0.0.0");
  get("release-rollout").value=Number.isSafeInteger(p.rolloutPercent)?String(p.rolloutPercent):"100";
  const candidate=p.candidateTag||"";
  const select=get("release-candidate");
  if([...select.options].some(x=>x.value===candidate))select.value=candidate;
  get("release-confirm-tag").value="";
  get("release-confirm-tag").placeholder=candidate||"LATEST";
}
function populateReleaseCandidates(releases){
  const platform=get("release-platform").value;
  const select=get("release-candidate"),selected=select.value;
  select.replaceChildren();
  const latest=document.createElement("option");latest.value="";latest.textContent="Latest stable feed";select.append(latest);
  const platformReleases=releases.filter(x=>x.platform===platform);
  const visible=platform==="windows"?platformReleases.slice(0,1):platformReleases;
  for(const release of visible){
    const option=document.createElement("option");option.value=release.tag;
    option.textContent=release.tag+" · "+dateTime(release.publishedAt);select.append(option);
  }
  if([...select.options].some(x=>x.value===selected))select.value=selected;
  else if(window.releasePolicies?.[platform]?.candidateTag)
    select.value=window.releasePolicies[platform].candidateTag;
  const tag=select.value;get("release-confirm-tag").value="";
  get("release-confirm-tag").placeholder=tag||"LATEST";
}
async function loadReleaseManager(){
  const state=get("release-manager-state");state.textContent="Loading published release metadata and CI…";
  try{
    const data=await api("releaseManager",{});
    if(!Array.isArray(data.releases)||!data.policies)throw Error("Invalid release manager response");
    renderReleaseCatalog(data);
    state.textContent="Loaded "+data.releases.length+" published stable releases. Release notes and asset digests are public metadata.";
  }catch(error){
    state.textContent="Could not load release metadata. Retry when connected.";
    get("release-rows").replaceChildren();get("save-release-policy").disabled=true;
    alertText(error.message,true);
  }
}
get("refresh-releases").addEventListener("click",()=>{if(accessToken)loadReleaseManager()});
get("release-platform").addEventListener("change",()=>{
  populateReleaseCandidates(window.releaseCatalog||[]);
  applyReleasePolicyFields(window.releasePolicies||{});
});
get("release-candidate").addEventListener("change",()=>{
  const tag=get("release-candidate").value;
  get("release-confirm-tag").value="";get("release-confirm-tag").placeholder=tag||"LATEST";
});
get("release-policy-form").addEventListener("submit",async e=>{
  e.preventDefault();alertText("");
  if(!accessToken)return;
  try{
    const tag=get("release-candidate").value||null;
    const result=await api("releasePolicySet",{
      platform:get("release-platform").value,candidateTag:tag,
      minimumVersion:get("release-minimum").value.trim(),
      rolloutPercent:Number(get("release-rollout").value),
      reasonCode:get("release-reason").value,
      confirmTag:get("release-confirm-tag").value.trim(),
    });
    get("release-confirm-tag").value="";
    alertText(result.note||"Release policy saved.");
    await loadReleaseManager();
  }catch(error){alertText(error.message,true);}
});

async function loadSecurityEvents(){
  if(!accessToken)return;
  const state=get("security-events-state");
  state.textContent="Loading security activity…";
  try{
    const data=await api("securityEvents",{limit:50});
    renderSecurityEvents(data);
  }catch(error){
    get("security-events").replaceChildren();
    get("security-alerts").replaceChildren();
    state.textContent="Could not load security activity.";
    alertText(error.message,true);
  }
}
get("refresh-security-events").addEventListener("click",()=>{if(accessToken)loadSecurityEvents()});
async function loadHistory(){
  const info=get("support-history-state");
  info.textContent="Loading support cases…";
  try{
    const data=await api("supportHistory",{});
    const list=get("support-history");list.replaceChildren();
    for(const item of data.cases){
      const li=document.createElement("li");
      li.textContent=item.kind+" · "+item.state+" · "+item.reason+" · "+
        date(item.createdAt)+" · target "+item.targetId;
      list.append(li);
    }
    info.textContent=data.cases.length?"Latest 25 cases":"No support cases yet.";
  }catch(e){info.textContent="Could not load support cases.";alertText(e.message,true);}
}
get("refresh-history").addEventListener("click",()=>{if(accessToken)loadHistory()});
async function freshRoleVerification(code){
  if(!/^[0-9]{6}$/.test(code||""))throw Error("Enter a six-digit authenticator code.");
  const user=await authFetch("user",null,"GET",accessToken);
  const factor=(Array.isArray(user.factors)?user.factors:[])
    .find(f=>f.factor_type==="totp"&&f.status==="verified");
  if(!factor)throw Error("A verified authenticator is required for role changes.");
  const challenge=await authFetch("factors/"+encodeURIComponent(factor.id)+"/challenge",{},"POST",accessToken);
  const verified=await authFetch("factors/"+encodeURIComponent(factor.id)+"/verify",
    {challenge_id:challenge.id,code},"POST",accessToken);
  if(jwtAal(verified.access_token)!=="aal2")throw Error("Authenticator verification did not establish AAL2.");
  accessToken=verified.access_token;
}
async function submitRoleMutation(action,payload,codeId){
  try{
    await freshRoleVerification(get(codeId).value.trim());
    get(codeId).value="";
    await api(action,payload);
    get("role-confirm-id").value="";get("role-bootstrap-confirm").value="";
    alertText(action==="roleRevoke"?"Administrator role revoked.":"Administrator role updated.");
    await loadRoleStatus();
  }catch(error){alertText(error.message,true);}
}
get("refresh-role-status").addEventListener("click",()=>{if(accessToken)loadRoleStatus()});
get("role-bootstrap-button").addEventListener("click",()=>{
  if(!roleStatusData?.userId||get("role-bootstrap-confirm").value.trim()!==roleStatusData.userId){
    alertText("Type your account UUID exactly to initialize the first owner.",true);return;
  }
  submitRoleMutation("roleBootstrap",{
    targetId:roleStatusData.userId,roleName:"owner",confirmId:roleStatusData.userId,
  },"role-bootstrap-code");
});
get("role-form").addEventListener("submit",e=>{
  e.preventDefault();
  const targetId=get("role-target-id").value.trim();
  if(!targetId||get("role-confirm-id").value.trim()!==targetId){
    alertText("Type the target account UUID exactly to confirm.",true);return;
  }
  submitRoleMutation("roleAssign",{targetId,roleName:get("role-name").value,confirmId:targetId},"role-code");
});
get("role-revoke").addEventListener("click",()=>{
  const targetId=get("role-target-id").value.trim();
  if(!targetId||get("role-confirm-id").value.trim()!==targetId){
    alertText("Type the target account UUID exactly to confirm.",true);return;
  }
  submitRoleMutation("roleRevoke",{targetId,confirmId:targetId},"role-code");
});

get("search").addEventListener("input",scheduleSearch);
get("email-filter").addEventListener("change",()=>{if(accessToken)loadPage(1)});
get("mfa-filter").addEventListener("change",()=>{if(accessToken)loadPage(1)});
get("clear-filters").addEventListener("click",()=>{
  get("search").value="";get("email-filter").value="all";
  get("mfa-filter").value="all";if(accessToken)loadPage(1);
});
get("prev").addEventListener("click",()=>{if(page>1&&!get("prev").disabled)loadPage(page-1);});
get("next").addEventListener("click",()=>{if(!get("next").disabled)loadPage(page+1);});
get("logout").addEventListener("click",()=>{
  if(deviceRevocationBusy||cloudSyncBusy){alertText("A cloud control is still being processed. Wait for its result.",true);return;}
  ++loadSequence;clearTimeout(searchDelay);
  accessToken="";accounts=[];matching=0;closeSupport();
  get("support-history").replaceChildren();clearAnalytics();
  visible("dashboard",false);visible("login",true);
  visible("logout",false);alertText("Signed out.");get("email").focus();
});
if(RECOVERY){
  const params=new URLSearchParams(location.hash.slice(1));
  const token=params.get("access_token"),type=params.get("type");
  history.replaceState(null,"",location.pathname);
  if(type!=="recovery"||!token){alertText("Invalid or expired recovery link.",true);get("recovery-form").hidden=true;}
  else{
    get("recovery-form").addEventListener("submit",async e=>{
      e.preventDefault();alertText("");
      const first=get("new-password").value,second=get("confirm-password").value;
      if(first!==second||first.length<16){alertText("Use matching passwords with at least 16 characters.",true);return;}
      try{await authFetch("user",{password:first},"PUT",token);
        get("new-password").value="";get("confirm-password").value="";
        alertText("Cloud account password updated. Sign in to Lockvyn with your new cloud password.");
      }catch(error){alertText(error.message,true);}
    });
  }
}
let announcementRows=[],selectedAnnouncement=null;
function clearAnnouncementForm(){
  selectedAnnouncement=null;
  for(const id of ["announcement-title-en","announcement-title-ar","announcement-body-en","announcement-body-ar","announcement-min","announcement-max","announcement-expires"])
    get(id).value="";
  get("announcement-severity").value="info";
  get("announcement-platform").value="all";
  get("announcement-notify").checked=false;
  get("announcement-confirm-id").value="";
  get("announcement-selected-id").textContent="Select a notice to see its ID and actions.";
  get("announcement-save").disabled=false;
  get("announcement-save").textContent="Save as draft";
  get("announcement-publish").disabled=true;
  get("announcement-archive").disabled=true;
}
function announcementPayload(){
  const expires=get("announcement-expires").value;
  return {
    titleEn:get("announcement-title-en").value.trim(),
    titleAr:get("announcement-title-ar").value.trim(),
    bodyEn:get("announcement-body-en").value.trim(),
    bodyAr:get("announcement-body-ar").value.trim(),
    severity:get("announcement-severity").value,
    targetPlatform:get("announcement-platform").value,
    minVersion:get("announcement-min").value.trim()||null,
    maxVersion:get("announcement-max").value.trim()||null,
    notify:get("announcement-notify").checked,
    expiresAt:expires?new Date(expires).toISOString():null,
  };
}
function updateAnnouncementConfirm(){
  if(!selectedAnnouncement)return;
  const matches=get("announcement-confirm-id").value.trim()===selectedAnnouncement.id;
  get("announcement-publish").disabled=!matches||selectedAnnouncement.status!=="draft";
  get("announcement-archive").disabled=!matches||selectedAnnouncement.status==="archived";
}
function selectAnnouncement(item){
  selectedAnnouncement=item;
  get("announcement-title-en").value=item.titleEn||"";
  get("announcement-title-ar").value=item.titleAr||"";
  get("announcement-body-en").value=item.bodyEn||"";
  get("announcement-body-ar").value=item.bodyAr||"";
  get("announcement-severity").value=item.severity||"info";
  get("announcement-platform").value=item.targetPlatform||"all";
  get("announcement-min").value=item.minVersion||"";
  get("announcement-max").value=item.maxVersion||"";
  get("announcement-notify").checked=Boolean(item.notify);
  if(item.expiresAt){
    const expiry=new Date(item.expiresAt);
    get("announcement-expires").value=Number.isNaN(expiry.getTime())?"":new Date(expiry.getTime()-expiry.getTimezoneOffset()*60000).toISOString().slice(0,16);
  }else get("announcement-expires").value="";
  get("announcement-confirm-id").value="";
  get("announcement-selected-id").textContent="Selected ID: "+item.id+" · "+item.status+" · Type this ID to enable publication or archive.";
  get("announcement-save").disabled=item.status!=="draft";
  get("announcement-save").textContent=item.status==="draft"?"Save draft changes":"Published notices are read-only";
  updateAnnouncementConfirm();
  previewAnnouncement();
}
function renderAnnouncements(items){
  announcementRows=items;
  const list=get("announcement-list");list.replaceChildren();
  for(const item of items){
    const row=document.createElement("li");
    const button=document.createElement("button");button.type="button";
    button.textContent=(item.titleEn||item.titleAr||"Untitled draft")+" · "+item.status;
    button.setAttribute("aria-label","Select "+(item.titleEn||item.titleAr||"untitled notice")+" · "+item.status);
    button.addEventListener("click",()=>selectAnnouncement(item));
    const details=document.createElement("small");
    details.textContent=(item.targetPlatform||"all")+" · "+(item.minVersion||"any version")+"–"+(item.maxVersion||"latest")+" · "+(item.severity||"info");
    row.append(button,details);list.append(row);
  }
  if(!items.length){
    const empty=document.createElement("li");empty.textContent="No notices yet. Create a draft on the right.";list.append(empty);
  }
}
async function loadAnnouncementManager(){
  if(!accessToken)return;
  const state=get("announcement-state");state.textContent="Loading notices…";
  try{
    const data=await api("announcements",{});
    if(!Array.isArray(data.announcements))throw Error("Invalid announcement response");
    renderAnnouncements(data.announcements);
    state.textContent="Loaded "+data.announcements.length+" notices. Drafts are private; published notices are delivered only to matching platform versions.";
  }catch(error){
    get("announcement-list").replaceChildren();
    state.textContent="Could not load notices. Retry when connected.";
    alertText(error.message,true);
  }
}
function previewAnnouncement(){
  const p=announcementPayload();
  get("announcement-preview").textContent="English\\n"+(p.titleEn||"Untitled")+"\\n"+(p.bodyEn||"Message preview")+
    "\\n\\nالعربية\\n"+(p.titleAr||"بدون عنوان")+"\\n"+(p.bodyAr||"معاينة الرسالة");
}
get("refresh-announcements").addEventListener("click",()=>{if(accessToken)loadAnnouncementManager()});
get("announcement-new").addEventListener("click",clearAnnouncementForm);
get("announcement-preview-button").addEventListener("click",previewAnnouncement);
get("announcement-confirm-id").addEventListener("input",updateAnnouncementConfirm);
get("announcement-form").addEventListener("submit",async e=>{
  e.preventDefault();if(!accessToken)return;
  try{
    const saved=await api("announcementSave",{
      announcementId:selectedAnnouncement?.id||null,announcement:announcementPayload(),
    });
    alertText(selectedAnnouncement?"Draft updated.":"Draft created.");
    await loadAnnouncementManager();
    const current=announcementRows.find(item=>item.id===saved.announcement.id);
    if(current)selectAnnouncement(current);
  }catch(error){alertText(error.message,true);}
});
async function changeAnnouncement(action){
  if(!selectedAnnouncement)return;
  const id=selectedAnnouncement.id;
  if(get("announcement-confirm-id").value.trim()!==id){alertText("Type the selected notice ID to confirm.",true);return;}
  try{
    await api(action,{id,confirmId:id});
    alertText(action==="announcementPublish"?"Notice published.":"Notice archived.");
    await loadAnnouncementManager();
    const current=announcementRows.find(item=>item.id===id);
    if(current)selectAnnouncement(current);
  }catch(error){alertText(error.message,true);}
}
get("announcement-publish").addEventListener("click",()=>changeAnnouncement("announcementPublish"));
get("announcement-archive").addEventListener("click",()=>changeAnnouncement("announcementArchive"));

</script>
</body></html>`;
}
