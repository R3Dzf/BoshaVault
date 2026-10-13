// Cloud-support controls. AAL2, allowlisted Owner and verified Supabase JWT
// are checked by index.ts BEFORE these pure policy rules are used.
export const SUPPORT_KINDS = Object.freeze([
  "suspend_24h", "restore_login", "session_review",
  "mfa_review", "deletion_review",
]);
export const SUPPORT_REASONS = Object.freeze([
  "user_request", "lost_device", "suspected_compromise",
  "access_problem", "policy_violation",
]);
export function validSupportRequest(data) {
  return Boolean(data && typeof data==="object" &&
    typeof data.id==="string" &&
    /^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/i.test(data.id) &&
    SUPPORT_KINDS.includes(data.kind) &&
    SUPPORT_REASONS.includes(data.reason));
}
export function validCaseConfirmation(data) {
  return validSupportRequest(data) &&
    typeof data.caseId==="string" &&
    /^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/i.test(data.caseId) &&
    typeof data.confirmEmail==="string" &&
    data.confirmEmail.length>2 && data.confirmEmail.length<=320;
}
// AMR records a *successful factor verification* time, which is not
// extended just because a browser refreshes its JWT. Fail closed if AMR is
// absent/rewritten/has no timestamp. No password-only freshness shortcut.
export function freshVerifiedTotp(claims, nowSeconds) {
  if (claims?.aal!=="aal2" || !Array.isArray(claims.amr) ||
      !Number.isFinite(nowSeconds))return false;
  return claims.amr.some(entry =>
    entry && typeof entry==="object" &&
    (entry.method==="totp" || entry.method==="mfa/totp") &&
    Number.isSafeInteger(entry.timestamp) &&
    nowSeconds>=entry.timestamp &&
    nowSeconds-entry.timestamp<=180);
}
export function safeTarget(targetId, actorId, targetIsAdmin) {
  return Boolean(targetId && actorId && targetId!==actorId && !targetIsAdmin);
}
// Read-only cloud-login status; never infer state from audit cases or from
// a past action. Malformed/missing provider timestamps must not mean active.
export function cloudLoginStatus(bannedUntil, nowMs = Date.now()) {
  if (bannedUntil === null || bannedUntil === undefined || bannedUntil === "") {
    return { state: "active", until: null };
  }
  if (typeof bannedUntil !== "string" || !Number.isFinite(nowMs)) {
    return { state: "unknown", until: null };
  }
  const parsed = Date.parse(bannedUntil);
  if (!Number.isFinite(parsed)) return { state: "unknown", until: null };
  return parsed > nowMs
    ? { state: "suspended", until: new Date(parsed).toISOString() }
    : { state: "active", until: null };
}

export function isAutomaticAction(kind) {
  return kind==="suspend_24h" || kind==="restore_login";
}
