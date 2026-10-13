#!/usr/bin/env python3
"""Static safety checks for support access and combined release plumbing."""
import pathlib,re,py_compile
root=pathlib.Path(__file__).resolve().parents[1]
admin=(root/"supabase/functions/lockvyn-admin/index.ts").read_text()
ui=(root/"supabase/functions/lockvyn-admin/ui.ts").read_text()
migration=(root/"supabase/migrations/20261013123000_admin_console.sql").read_text()
combined=(root/".github/workflows/combine-releases.yml").read_text()
windows=(root/".github/workflows/release-windows.yml").read_text()
android=(root/".github/workflows/release-android.yml").read_text()
py_compile.compile(str(root/"tools/combine_releases.py"), doraise=True)
assert "service.auth.getUser(token)" in admin
assert 'verifiedUserGate(verified.user, claims)' in admin
assert 'adminMembershipGate(admin, verified.user.id)' in admin
assert 'service.auth.getUser(token)' in admin
assert '"./access.mjs"' in admin
assert '.from("lockvyn_admins")' in admin
assert 'service.rpc("lockvyn_admin_directory"' in admin
assert 'p_actor: verified.user.id' in admin
assert 'p_search: search.trim()' in admin
assert 'p_mfa_state: mfaState' in admin
directory=(root/"supabase/migrations/20261013173000_admin_directory.sql").read_text()
assert "security definer" in directory
assert "lockvyn_admins" in directory
assert "revoke all on function public.lockvyn_admin_directory" in directory
assert "to service_role" in directory
assert "auth.mfa_factors" in directory
assert "Master Passphrases cannot be reset" in ui
assert 'id="email-filter"' in ui and 'id="mfa-filter"' in ui
assert 'matching' in ui and 'verified-stat' in ui
assert 'service.auth.resetPasswordForEmail' in admin
assert 'Use your own account recovery flow' not in admin
assert "'isSelf',(pr.id=p_actor)" in directory
assert 'user.isSelf?"Reset my cloud password":"Send recovery email"' in ui
assert "service.rpc(\"lockvyn_claim_recovery\"" in admin
assert 'service.rpc("lockvyn_claim_recovery"' in admin
assert '[url.origin, dashboardOrigin].includes(request.headers.get("origin")' in admin
assert 'Cache-Control": "no-store' in admin
assert 'X-Frame-Options": "DENY"' in admin
assert 'verify_jwt=false' in (root/"docs/ADMIN-DASHBOARD.md").read_text()
assert "grant select on public.lockvyn_admins to service_role;" in migration
assert "grant execute on function public.lockvyn_claim_recovery(uuid,uuid) to service_role" in migration
assert "revoke all on public.lockvyn_admins from public, anon, authenticated" in migration
assert "pg_advisory_xact_lock" in migration
assert "interval '60 minutes'" in migration
assert 'SUPABASE_SERVICE_ROLE_KEY' not in ui
assert "localStorage" not in ui and "sessionStorage" not in ui
assert "Master Passphrases cannot be reset" in ui
worker=(root/"infra/cloudflare/lockvyn-admin-ui.js").read_text()
assert "Content-Type\":\"text/html; charset=utf-8" in worker
assert "Content-Security-Policy" in worker
assert "addEventListener" in worker
assert "Lockvyn-Android.apk" in (root/"tools/combine_releases.py").read_text()
assert "SHA256SUMS-Android.txt" in (root/"tools/combine_releases.py").read_text()
assert 'releases.win.json' in (root/"tools/combine_releases.py").read_text()
assert "combine-releases.yml" in windows and "combine-releases.yml" in android
assert "on:\n  release:" in combined
print("PASS admin authorization, audit, no client secrets, combined release contracts")
