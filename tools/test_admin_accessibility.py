#!/usr/bin/env python3
"""Zero-cost static accessibility and responsive behavior contracts."""
from pathlib import Path
s=(Path(__file__).resolve().parents[1]/"supabase/functions/lockvyn-admin/ui.ts").read_text()
required={
  'keyboard skip navigation': 'href="#directory-section"',
  'directory heading association': 'aria-labelledby="directory-heading"',
  'scrollable table accessible name': 'aria-label="Accounts table (scroll horizontally on narrow screens)"',
  'keyboard-focusable table': 'tabindex="0" aria-busy="false"',
  'screen-reader feedback': 'id="directory-state" role="status" aria-live="polite"',
  'busy state': 'setAttribute("aria-busy",String(value))',
  'empty data': 'id="empty" hidden role="status"',
  'retry on failure': 'id="retry-directory" type="button" hidden',
  'retry handler': 'get("retry-directory").addEventListener("click"',
  'explicit error state': 'Unable to load accounts. Retry or adjust filters.',
  'mobile layout': '@media(max-width:520px)',
  'reduced motion support': '@media(prefers-reduced-motion:reduce)',
  'no stale results': 'if(seq!==loadSequence||!accessToken)return',
  'search debounce': 'searchDelay=setTimeout',
  'non-secret admin view': 'No passwords, vault records or recovery keys',
  'security center heading association': 'aria-labelledby="security-center-heading"',
  'security activity status': 'id="security-events-state" role="status" aria-live="polite"',
  'security alert group name': 'aria-label="Recent security signal counts"',
  'security refresh control': 'id="refresh-security-events" type="button"',
  'metadata-only privacy disclosure': 'Password login failures, emails, IP addresses, tokens, and vault data are not collected here.',
  'safe event labels': 'SECURITY_EVENT_LABELS',
  'safe event rendering': 'meta.textContent=details.join',
  'sync usage status': 'id="cloud-sync-usage" role="status" aria-live="polite"',
  'sync state status': 'id="cloud-sync-state" class="support-note" role="status" aria-live="polite" tabindex="-1"',
  'sync activity filter group': 'aria-label="Cloud sync activity filters"',
  'cloud sync pause control': 'id="pause-cloud-sync" type="button" hidden',
  'cloud sync resume control': 'id="resume-cloud-sync" type="button" hidden',
  'typed email and fresh MFA controls': 'Fresh code from your own authenticator app',
  'preserved cloud and offline data': 'Offline vault data and encrypted cloud snapshots are preserved.',
  'safe sync error labels': 'SYNC_ERROR_LABELS',
  'sync row text-only rendering': 'activity.textContent="Last attempt "',
  'announcement heading association': 'aria-labelledby="announcement-heading"',
  'announcement loading status': 'id="announcement-state" role="status" aria-live="polite"',
  'announcement preview announcement status': 'id="announcement-preview" class="announcement-preview" role="status" aria-live="polite"',
  'announcement confirmation control': 'id="announcement-confirm-id"',
  'announcement safe text preview': 'get("announcement-preview").textContent=',
  'announcement safe list rendering': 'button.textContent=(item.titleEn||item.titleAr',
  'announcement opt-in disclosure': 'Client alerts stay opt-in and local.',
  'notice confirmation control disabled by default': 'id="announcement-publish" type="button" disabled',
  'ticket section heading association': 'aria-labelledby="ticket-heading"',
  'ticket workflow loads on admin sign-in': 'await loadSupportTickets();await loadPage(1)',
  'ticket retention and vault boundary disclosure': 'No vault contents are available here.',
  'ticket write confirmation controls are server-gated': 'Type the selected ticket ID to enable changes',

  'ticket queue live status': 'id="ticket-queue-state" role="status" aria-live="polite"',
  'ticket conversation named region': 'role="region" aria-label="Support ticket conversation" tabindex="0"',
  'ticket diagnostics allowlist rendering': 'for(const key of ["platform","appVersion","osVersion","errorCode"])',
  'ticket user content rendered as text': 'body.textContent=String(message.body||"")',
  'ticket typed confirmation': 'id="ticket-confirm-id" autocomplete="off" maxlength="36"',
  'ticket changes disabled by default': 'id="ticket-assign-me" type="button" disabled',
  'ticket admin endpoint': '/functions/v1/lockvyn-admin/tickets',
  'ticket endpoint uses AAL2 API action names': 'ticketAdminAssign',
  'ticket message safely rendered as text': 'body.textContent=String(message.body||"")',

}
for label, marker in required.items():
    assert marker in s, f"Missing {label}: {marker}"
assert 'innerHTML' not in s and 'insertAdjacentHTML' not in s
print("PASS accessible keyboard navigation, responsive directory, safe loading/error/empty states")
