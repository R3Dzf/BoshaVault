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
}
for label, marker in required.items():
    assert marker in s, f"Missing {label}: {marker}"
assert 'innerHTML' not in s and 'insertAdjacentHTML' not in s
print("PASS accessible keyboard navigation, responsive directory, safe loading/error/empty states")
