import { createClient } from "npm:@supabase/supabase-js@2.58.0";
import { html } from "./ui.ts";
import { validCandidateTag, validateReleasePolicy, compareReleaseVersions } from "./release.mjs";
import { securityHtml } from "./security.ts";
import { verifiedUserGate, adminMembershipGate } from "./access.mjs";
import { validSupportRequest, validCaseConfirmation, freshVerifiedTotp, safeTarget, isAutomaticAction, cloudLoginStatus } from "./support.mjs";

// The service-role key is only read inside this Edge Function. It is never
// embedded in the HTML response, shipped to clients, or logged.
const apiUrl = Deno.env.get("SUPABASE_URL") ?? "";
const publicKey = Deno.env.get("SUPABASE_ANON_KEY") ?? "";
const serviceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? "";
const dashboardOrigin = "https://lockvyn-admin-ui.r3dzf-studio.workers.dev";
const recoveryRedirect = Deno.env.get("LOCKVYN_RECOVERY_REDIRECT_URL") ??
  dashboardOrigin + "/recover";
const service = createClient(apiUrl, serviceKey, {
  auth: { autoRefreshToken: false, persistSession: false },
});

function result(data: unknown, status = 200): Response {
  return new Response(JSON.stringify(data), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
      "Referrer-Policy": "no-referrer",
      "X-Frame-Options": "DENY",
      "Access-Control-Allow-Origin": dashboardOrigin,
      "Vary": "Origin",
    },
  });
}

function nonce(): string {
  const b = crypto.getRandomValues(new Uint8Array(24));
  return btoa(String.fromCharCode(...b));
}

async function recordSecuritySignal(actorId: string, signal: string): Promise<void> {
  try {
    await service.rpc("lockvyn_admin_record_security_signal", {
      p_actor: actorId,
      p_signal: signal,
    });
  } catch {
    // A telemetry write must never change a fail-closed authorization decision.
  }
}



function publicPolicyResponse(data,status=200) {
  return new Response(JSON.stringify(data), {status, headers: {
    "Content-Type":"application/json; charset=utf-8",
    "Cache-Control":"public, max-age=30, stale-while-revalidate=30",
    "Access-Control-Allow-Origin":"*",
    "X-Content-Type-Options":"nosniff",
    "Referrer-Policy":"no-referrer",
    "X-Frame-Options":"DENY",
  }});
}

let releaseCatalogCache = {at:0,value:[]};
async function githubJson(path,limit=512*1024) {
  const response=await fetch("https://api.github.com/repos/R3Dzf/BoshaVault/"+path,{
    headers:{Accept:"application/vnd.github+json","User-Agent":"Lockvyn-Release-Manager"},
    signal:AbortSignal.timeout(8000),
  });
  if(!response.ok)throw new Error("GitHub release metadata unavailable");
  const length=Number(response.headers.get("content-length")||0);
  if(length>limit)throw new Error("GitHub response too large");
  const body=await response.text();
  if(body.length>limit)throw new Error("GitHub response too large");
  return JSON.parse(body);
}
async function releaseChecks(tag){
  try{
    const data=await githubJson("commits/"+encodeURIComponent(tag)+"/check-runs?per_page=30",128*1024);
    const checks=Array.isArray(data.check_runs)?data.check_runs.slice(0,30):[];
    const failed=checks.some(x=>["failure","cancelled","timed_out","action_required"].includes(x.conclusion));
    const pending=checks.some(x=>!x.completed_at);
    const passed=checks.filter($1=>["success","neutral","skipped"].includes(x.conclusion)).length;
    return {state:failed?"failed":pending?"pending":checks.length?"success":"unknown",
      passed,total:checks.length,checks:checks.slice(0,8).map($1=>({
        name:String(x.name||"Check").slice(0,120),
        conclusion:typeof x.conclusion==="string"?x.conclusion:"pending",
      }))};
  }catch{return {state:"unknown",passed:0,total:0,checks:[]};}
}
async function androidReleaseManifest(assetId,tag){
  if(typeof assetId!=="number"||!Number.isSafeInteger(assetId)||assetId<=0)return null;
  try{
    const response=await fetch("https://api.github.com/repos/R3Dzf/BoshaVault/releases/assets/"+assetId,{
      headers:{Accept:"application/octet-stream","User-Agent":"Lockvyn-Release-Manager"},
      signal:AbortSignal.timeout(8000),
    });
    if(!response.ok||!["api.github.com","github.com"].includes(new URL(response.url).hostname)&&
       !new URL(response.url).hostname.endsWith(".githubusercontent.com"))return null;
    const length=Number(response.headers.get("content-length")||0);
    if(length>16384)return null;
    const body=await response.text();
    if(body.length>16384)return null;
    const data=JSON.parse(body);
    if(data.tag!==tag||data.packageName!=="com.lockvyn.app"||
       !Number.isSafeInteger(data.versionCode)||data.versionCode<1||
       !/^[a-f0-9]{64}$/i.test(data.signerSha256||"")||
       !/^[a-f0-9]{64}$/i.test(data.sha256||"")||
       data.url!=="https://github.com/R3Dzf/BoshaVault/releases/download/"+tag+"/Lockvyn-Android.apk")return null;
    return {versionName:String(data.versionName||"").slice(0,32),versionCode:data.versionCode,
      signerSha256:data.signerSha256.toLowerCase(),apkSha256:data.sha256.toLowerCase(),
      minSdk:data.minSdk};
  }catch{return null;}
}
async function loadReleaseCatalog(){
  if(Date.now()-releaseCatalogCache.at<300000&&releaseCatalogCache.value.length)return releaseCatalogCache.value;
  const releases=await githubJson("releases?per_page=30",512*1024);
  if(!Array.isArray(releases))throw new Error("Invalid release catalog");
  const stable=releases.filter(r=>r&&!r.draft&&!r.prerelease&&
    typeof r.tag_name==="string"&&
    (validCandidateTag("windows",r.tag_name)||validCandidateTag("android",r.tag_name))).slice(0,12);
  const output=await Promise.all(stable.map(async $1=>{
    const tag=r.tag_name;
    const platform=validCandidateTag("windows",tag)?"windows":"android";
    const rawAssets=Array.isArray(r.assets)?r.assets.slice(0,40):[];
    const assets=rawAssets.filter(a=>a&&typeof a.name==="string"&&a.name.length<=160)
      .map(a=>({name:a.name,size:Number.isSafeInteger(a.size)?a.size:null,
        sha256:typeof a.digest==="string"&&/^sha256:[a-f0-9]{64}$/i.test(a.digest)
          ?a.digest.slice(7).toLowerCase():null}))
      .slice(0,24);
    const manifestAsset=platform==="android"?rawAssets.find($1=>a?.name==="android-update.json"):null;
    const [checks,manifest]=await Promise.all([
      releaseChecks(tag),
      manifestAsset?androidReleaseManifest(manifestAsset.id,tag):Promise.resolve(null),
    ]);
    return {platform,tag,name:String(r.name||tag).slice(0,160),
      publishedAt:typeof r.published_at==="string"?r.published_at:null,
      changelog:typeof r.body==="string"?r.body.slice(0,4000):"",
      url:"https://github.com/R3Dzf/BoshaVault/releases/tag/"+encodeURIComponent(tag),
      assets,checks,androidManifest:manifest,
      windowsSignature:platform==="windows"?"Unsigned · Authenticode publisher signing is not configured":null,
      androidSignature:platform==="android"?(manifest?
        "Signing fingerprint published; clients enforce continuity":"Signing metadata unavailable"):null};
  }));
  releaseCatalogCache={at:Date.now(),value:output};
  return output;
}
async function currentReleasePolicies(){
  const platforms=["windows","android"];
  const pairs=await Promise.all(platforms.map(async platform=>{
    const {data,error}=await service.rpc("lockvyn_release_policy_public",{p_platform:platform});
    if(error||!data||typeof data!=="object")throw new Error("Release policy unavailable");
    return [platform,data];
  }));
  return Object.fromEntries(pairs);
}

Deno.serve(async (request) => {
  const url = new URL(request.url);
  if(request.method==="OPTIONS"&&url.pathname.endsWith("/lockvyn-admin/release-policy")){
    return new Response(null,{status:204,headers:{
      "Access-Control-Allow-Origin":"*",
      "Access-Control-Allow-Methods":"GET, OPTIONS",
      "Access-Control-Allow-Headers":"apikey, content-type",
      "Access-Control-Max-Age":"600",
      "Cache-Control":"public, max-age=600",
    }});
  }
  if (request.method === "GET" && url.pathname.endsWith("/lockvyn-admin/release-policy")) {
    const platform=url.searchParams.get("platform");
    if(platform!=="windows"&&platform!=="android")
      return publicPolicyResponse({error:"Invalid platform"},400);
    if(!apiUrl||!serviceKey)return publicPolicyResponse({error:"Policy unavailable"},503);
    const {data,error}=await service.rpc("lockvyn_release_policy_public",{p_platform:platform});
    if(error||!data)return publicPolicyResponse({error:"Policy unavailable"},503);
    return publicPolicyResponse(data);
  }
  if (request.method === "OPTIONS" &&
      url.pathname.endsWith("/lockvyn-admin") &&
      request.headers.get("origin") === dashboardOrigin) {
    return new Response(null, {
      status: 204, headers: {
        "Access-Control-Allow-Origin": dashboardOrigin,
        "Access-Control-Allow-Methods": "POST, OPTIONS",
        "Access-Control-Allow-Headers": "authorization, content-type",
        "Access-Control-Max-Age": "600",
        "Cache-Control": "no-store",
        "Vary": "Origin",
      },
    });
  }
  // Independent user-owned security page: GET serves public HTML only.
  // It is not an admin operation; credentials go from the browser directly to
  // Supabase Auth. No service-role RPC, admin token or vault data is involved.
  if (request.method === "GET" && url.pathname.endsWith("/lockvyn-admin/security")) {
    if (!apiUrl || !publicKey) return result({ error: "Service unavailable" }, 503);
    const n = nonce();
    return new Response(securityHtml(n, publicKey, apiUrl), {
      headers: {
        "Content-Type": "text/html; charset=utf-8",
        "Cache-Control": "no-store",
        "X-Content-Type-Options": "nosniff",
        "Referrer-Policy": "no-referrer",
        "X-Frame-Options": "DENY",
        "Content-Security-Policy":
          "default-src 'none'; connect-src 'self'; img-src 'self' data:; " +
          "script-src 'nonce-" + n + "'; style-src 'nonce-" + n + "'; " +
          "form-action 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'",
      },
    });
  }
  if (request.method === "GET" && (url.pathname.endsWith("/lockvyn-admin") ||
      url.pathname.endsWith("/lockvyn-admin/") ||
      url.pathname.endsWith("/lockvyn-admin/recover"))) {
    const n = nonce();
    const response = new Response(html(n, publicKey, url.pathname.endsWith("/recover")), {
      headers: {
        "Content-Type": "text/html; charset=utf-8",
        "Cache-Control": "no-store",
        "X-Content-Type-Options": "nosniff",
        "Referrer-Policy": "no-referrer",
        "X-Frame-Options": "DENY",
        "Content-Security-Policy":
          "default-src 'none'; connect-src 'self'; img-src 'self' data:; " +
          "script-src 'nonce-" + n + "'; style-src 'nonce-" + n + "'; " +
          "form-action 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'",
      },
    });
    return response;
  }
  if (!url.pathname.endsWith("/lockvyn-admin") || request.method !== "POST") {
    return result({ error: "Not found" }, 404);
  }

  // Reject browser cross-origin requests before evaluating any user credentials.
  if (![url.origin, dashboardOrigin].includes(request.headers.get("origin") ?? "")) return result({ error: "Forbidden" }, 403);
  if (!request.headers.get("content-type")?.startsWith("application/json")) {
    return result({ error: "Invalid content type" }, 415);
  }
  const bearer = request.headers.get("authorization") ?? "";
  if (!/^Bearer [A-Za-z0-9._-]{20,8192}$/.test(bearer)) {
    return result({ error: "Sign in first" }, 401);
  }
  if (!apiUrl || !serviceKey || !publicKey) return result({ error: "Service unavailable" }, 503);

  const token = bearer.slice(7);
  const { data: verified, error: invalid } = await service.auth.getUser(token);
  if (invalid || !verified.user?.email_confirmed_at) return result({ error: "Unauthorized" }, 401);
  // getUser() validates the token with Supabase Auth; only inspect claims *after*
  // the verification succeeds, and never authorize from user_metadata.
  let claims;
  try {
    const encoded = token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
    claims = JSON.parse(atob(encoded));
  } catch { return result({ error: "Unauthorized" }, 401); }
  const denied = verifiedUserGate(verified.user, claims);
  if (denied) {
    if (denied.status === 403 && claims.sub === verified.user.id &&
        claims.role === "authenticated") {
      await recordSecuritySignal(verified.user.id, "admin_step_up_denied");
    }
    return result(denied.body, denied.status);
  }
  // Supabase refresh-token revocation does not invalidate an already-issued
  // JWT by itself. Check the Auth session row before any privileged action.
  const sessionId = claims.session_id;
  if (typeof sessionId !== "string" ||
      !/^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/i.test(sessionId)) {
    return result({ error: "Unauthorized" }, 401);
  }
  const { data: sessionActive, error: sessionError } = await service.rpc(
    "lockvyn_auth_session_is_active",
    { p_user_id: verified.user.id, p_session_id: sessionId },
  );
  if (sessionError || typeof sessionActive !== "boolean") {
    return result({ error: "Unable to validate active session" }, 503);
  }
  if (!sessionActive) {
    await recordSecuritySignal(verified.user.id, "admin_inactive_session");
    return result({ error: "Session is no longer active" }, 401);
  }
  const { data: admin, error: permissionError } = await service
    .from("lockvyn_admins").select("user_id").eq("user_id", verified.user.id).maybeSingle();
  if (permissionError) return result({ error: "Administration is not configured" }, 503);
  if (!adminMembershipGate(admin, verified.user.id)) {
    await recordSecuritySignal(verified.user.id, "admin_allowlist_denied");
    return result({ error: "Not an authorized Lockvyn administrator" }, 403);
  }

  let body: Record<string, unknown>;
  try {
    const text = await request.text();
    if (text.length > 2048) return result({ error: "Request too large" }, 413);
    const parsed = JSON.parse(text);
    if (!parsed || Array.isArray(parsed) || typeof parsed !== "object") throw new Error();
    body = parsed;
  } catch { return result({ error: "Invalid request" }, 400); }


  if (body.action === "releaseManager") {
    try {
      const [policies,releases]=await Promise.all([currentReleasePolicies(),loadReleaseCatalog()]);
      return result({policies,releases});
    } catch {
      return result({error:"Unable to load verified release metadata"},502);
    }
  }
  if (body.action === "releasePolicySet") {
    const input={platform:typeof body.platform==="string"?body.platform:"",
      candidateTag:typeof body.candidateTag==="string"?body.candidateTag:null,
      minimumVersion:typeof body.minimumVersion==="string"?body.minimumVersion:"",
      rolloutPercent:typeof body.rolloutPercent==="number"?body.rolloutPercent:Number.NaN,
      reasonCode:typeof body.reasonCode==="string"?body.reasonCode:""};
    const invalid=validateReleasePolicy(input);
    const confirmation=input.candidateTag??"LATEST";
    if(invalid||body.confirmTag!==confirmation)
      return result({error:invalid||"Type the selected release tag to confirm the change"},400);
    if(!freshVerifiedTotp(claims,Math.floor(Date.now()/1000)))
      return result({error:"Verify a fresh authenticator code before changing release policy"},403);
    let catalog;
    try{catalog=await loadReleaseCatalog();}
    catch{return result({error:"Unable to verify the published release. Retry later."},502);}
    const candidates=catalog;
    const eligible=candidates.filter(x=>x.platform===input.platform);
    const selected=input.candidateTag
      ?eligible.find(x=>x.tag===input.candidateTag)
      :eligible[0];
    if(!selected)return result({error:"The selected stable release is not published"},409);
    if(input.platform==="windows"&&eligible[0]?.tag!==selected.tag)
      return result({error:"Windows updater follows the latest stable feed; older candidates are held"},409);
    const names=new Set((selected.assets||[]).map(x=>x.name));
    if(input.platform==="windows"&&(!names.has("Lockvyn-win-Setup.exe")||!names.has("releases.win.json")))
      return result({error:"The Windows release is missing its installer or stable update feed"},409);
    if(input.platform==="android"){
      const manifest=selected.androidManifest;
      if(!names.has("Lockvyn-Android.apk")||!names.has("android-update.json")||
         !manifest||!Number.isSafeInteger(manifest.versionCode)||
         Number(input.minimumVersion)>manifest.versionCode)
        return result({error:"The Android release metadata or minimum version could not be verified"},409);
    }
    if(input.platform==="windows"){
      const selectedVersion=selected.tag.slice("windows-v".length);
      if(compareReleaseVersions("windows",input.minimumVersion,selectedVersion)>0)
        return result({error:"Minimum version cannot exceed the selected release"},400);
    }
    const {data,error}=await service.rpc("lockvyn_admin_set_release_policy",{
      p_actor:verified.user.id,p_actor_session:sessionId,p_actor_aal:claims.aal,
      p_fresh_totp:true,p_platform:input.platform,p_candidate_tag:input.candidateTag,
      p_minimum_version:input.minimumVersion,p_rollout_percent:input.rolloutPercent,
      p_reason_code:input.reasonCode,
    });
    if(error){
      if(error.code==="54000")return result({error:"Release policy rate limit reached. Wait before trying again."},429);
      if(error.code==="42501")return result({error:"A fresh active administrator session is required"},403);
      if(error.code==="22023")return result({error:"The release policy is invalid"},400);
      return result({error:"Unable to save release policy"},502);
    }
    return result({policy:data,rollbackSafe:true,
      note:input.rolloutPercent===0?"Optional rollout stopped. Installed clients and encrypted vaults are unchanged.":"Policy saved. No client data or encrypted vault contents were accessed."});
  }

  if (body.action === "securityEvents") {
    const limit = body.limit ?? 50;
    if (typeof limit !== "number" || !Number.isSafeInteger(limit) || limit < 1 || limit > 100) {
      return result({ error: "Invalid security event page size" }, 400);
    }
    const { data, error } = await service.rpc("lockvyn_admin_security_feed", {
      p_actor: verified.user.id,
      p_actor_session: sessionId,
      p_actor_aal: claims.aal,
      p_limit: limit,
    });
    if (error || !data || !Array.isArray(data.events) || !data.alerts ||
        typeof data.alerts !== "object") {
      return result({ error: "Unable to load security activity" }, 502);
    }
    return result(data);
  }

  if (body.action === "list") {
    const page = body.page;
    const search = body.search ?? "";
    const emailState = body.emailState ?? "all";
    const mfaState = body.mfaState ?? "all";
    if (typeof page !== "number" || !Number.isSafeInteger(page) ||
        page < 1 || page > 10000 ||
        typeof search !== "string" || search.length > 120 ||
        typeof emailState !== "string" ||
        !["all","verified","pending"].includes(emailState) ||
        typeof mfaState !== "string" ||
        !["all","enabled","disabled"].includes(mfaState)) {
      return result({ error: "Invalid directory filters" }, 400);
    }
    // Database owns global totals and filters: do not report page-only
    // percentages as platform-wide data.
    const { data, error } = await service.rpc("lockvyn_admin_directory", {
      p_actor: verified.user.id,
      p_page: page,
      p_search: search.trim(),
      p_email_state: emailState,
      p_mfa_state: mfaState,
    });
    if (error || !data || !Array.isArray(data.users) ||
        typeof data.total !== "number" || typeof data.matching !== "number") {
      return result({ error: "Unable to load account directory" }, 502);
    }
    return result(data);
  }
  if (body.action === "deviceList" || body.action === "deviceRevoke" || body.action === "syncControl") {
    if (typeof body.id !== "string" ||
        !/^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/i.test(body.id)) {
      return result({ error: "Invalid target" }, 400);
    }
    if (body.id === verified.user.id) {
      return result({ error: "Administrator accounts are protected" }, 403);
    }
    const { data: targetAdmin, error: targetAdminError } = await service
      .from("lockvyn_admins").select("user_id").eq("user_id", body.id).maybeSingle();
    if (targetAdminError) return result({ error: "Unable to verify protected account" }, 503);
    if (targetAdmin) return result({ error: "Administrator accounts are protected" }, 403);

    if (body.action === "deviceList") {
      const platform = body.platform ?? null;
      const outcome = body.outcome ?? null;
      const sinceDays = body.sinceDays === undefined ? 30 : body.sinceDays;
      const deviceQuery = body.deviceQuery ?? null;
      if ((platform !== null && (typeof platform !== "string" ||
           !["windows","android","macos","ios","linux","other"].includes(platform))) ||
          (outcome !== null && (typeof outcome !== "string" ||
           !["all","success","failure","unknown"].includes(outcome))) ||
          (sinceDays !== null && (typeof sinceDays !== "number" ||
           !Number.isSafeInteger(sinceDays) || sinceDays < 1 || sinceDays > 365)) ||
          (deviceQuery !== null && (typeof deviceQuery !== "string" || deviceQuery.length > 80))) {
        return result({ error: "Invalid sync activity filters" }, 400);
      }
      const { data, error } = await service.rpc("lockvyn_admin_cloud_sync_overview", {
        p_actor: verified.user.id,
        p_actor_session: sessionId,
        p_actor_aal: claims.aal,
        p_target: body.id,
        p_platform: platform,
        p_outcome: outcome,
        p_since_days: sinceDays,
        p_device_query: typeof deviceQuery === "string" && deviceQuery.trim()
          ? deviceQuery.trim() : null,
      });
      if (error) {
        if (error.code === "42501") return result({ error: "Sync inventory is not available for this account" }, 403);
        if (error.code === "22023") return result({ error: "Invalid sync activity filter or target" }, 400);
        return result({ error: "Unable to load cloud sync activity" }, 502);
      }
      if (!data || !Array.isArray(data.devices) ||
          !Number.isSafeInteger(data.snapshotCount) ||
          !Number.isSafeInteger(data.ciphertextBytes) ||
          !Number.isSafeInteger(data.quotaBytes) ||
          typeof data.suspended !== "boolean") {
        return result({ error: "Invalid cloud sync activity response" }, 502);
      }
      return result(data);
    }

    if (body.action === "syncControl") {
      if (typeof body.suspend !== "boolean" ||
          typeof body.confirmEmail !== "string" || body.confirmEmail.length > 320 ||
          (body.reasonCode !== "support_review" &&
           body.reasonCode !== "suspected_compromise")) {
        return result({ error: "Invalid cloud sync control request" }, 400);
      }
      const { data: target, error: targetError } = await service.auth.admin.getUserById(body.id);
      if (targetError || !target?.user?.email) return result({ error: "Target account not found" }, 404);
      if (target.user.email.toLowerCase() !== body.confirmEmail.trim().toLowerCase()) {
        return result({ error: "The confirmation email does not match this account" }, 409);
      }
      if (!freshVerifiedTotp(claims, Math.floor(Date.now()/1000))) {
        return result({ error: "Verify a fresh authenticator code before changing cloud sync" }, 403);
      }
      const { data, error } = await service.rpc("lockvyn_admin_set_cloud_sync_suspension", {
        p_actor: verified.user.id,
        p_actor_session: sessionId,
        p_actor_aal: claims.aal,
        p_fresh_totp: true,
        p_target: body.id,
        p_suspend: body.suspend,
        p_reason_code: body.reasonCode,
        p_confirm_email: body.confirmEmail,
      });
      if (error) {
        if (error.code === "54000") return result({ error: "Cloud sync control rate limit reached. Wait before trying again." }, 429);
        if (error.code === "42501") return result({ error: "Cloud sync control is not allowed for this account" }, 403);
        if (error.code === "22023") return result({ error: "Invalid cloud sync control target or reason" }, 400);
        return result({ error: "Unable to change cloud sync state" }, 502);
      }
      return result({
        suspended: body.suspend,
        changed: data === true,
        cloudOnly: true,
        warning: body.suspend
          ? "Cloud sync is paused. Offline vault data and encrypted cloud snapshots are preserved."
          : "Cloud sync is resumed. Offline vault data and encrypted cloud snapshots were preserved.",
      });
    }

    if (typeof body.deviceId !== "string" ||
        !/^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/i.test(body.deviceId) ||
        typeof body.confirmEmail !== "string" || body.confirmEmail.length > 320) {
      return result({ error: "Invalid session revocation request" }, 400);
    }
    const { data: target, error: targetError } = await service.auth.admin.getUserById(body.id);
    if (targetError || !target?.user?.email) return result({ error: "Target account not found" }, 404);
    if (target.user.email.toLowerCase() !== body.confirmEmail.trim().toLowerCase()) {
      return result({ error: "The confirmation email does not match this account" }, 409);
    }
    if (!freshVerifiedTotp(claims, Math.floor(Date.now()/1000))) {
      return result({ error: "Verify a fresh authenticator code before revoking this session" }, 403);
    }
    const { data: revoked, error: revokeError } = await service.rpc(
      "lockvyn_admin_revoke_cloud_device", {
        p_actor: verified.user.id,
        p_actor_session: sessionId,
        p_actor_aal: claims.aal,
        p_fresh_totp: true,
        p_target: body.id,
        p_device_id: body.deviceId,
        p_confirm_email: body.confirmEmail,
      },
    );
    if (revokeError) {
      if (revokeError.code === "54000") return result({ error: "Cloud session revocation rate limit reached" }, 429);
      if (revokeError.code === "42501") return result({ error: "A fresh verified administrator session is required" }, 403);
      return result({ error: "Unable to revoke this cloud session" }, 502);
    }
    if (revoked !== true) return result({ error: "Session is already revoked or unavailable" }, 409);
    return result({ revoked: true });
  }
  if (body.action === "supportStatus") {
    if (typeof body.id !== "string" ||
        !/^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/i.test(body.id))
      return result({ error: "Invalid target" }, 400);
    if (body.id === verified.user.id)
      return result({ error: "Owner accounts are protected" }, 403);
    const { data: targetAdmin, error: adminError } = await service
      .from("lockvyn_admins").select("user_id").eq("user_id", body.id).maybeSingle();
    if (adminError) return result({ error: "Cannot verify protected account" }, 503);
    if (targetAdmin) return result({ error: "Administrator accounts are protected" }, 403);
    const { data: target, error: targetError } =
      await service.auth.admin.getUserById(body.id);
    if (targetError || !target?.user) return result({ error: "Target not found" }, 404);
    return result({
      cloudLogin: cloudLoginStatus(target.user.banned_until),
      caveat: "Cloud login only. Existing JWTs may remain valid until expiry; offline encrypted vaults remain accessible."
    });
  }
  if (body.action === "supportHistory") {
    const { data, error } = await service.rpc("lockvyn_admin_support_recent", {
      p_actor: verified.user.id,
    });
    if (error || !Array.isArray(data)) return result({ error: "Unable to load support history" }, 502);
    return result({ cases: data });
  }
  if (body.action === "supportPrepare" || body.action === "supportExecute") {
    const executing = body.action === "supportExecute";
    if (!validSupportRequest(body) || (executing && !validCaseConfirmation(body)) ||
        typeof body.id !== "string" || typeof body.kind !== "string" ||
        typeof body.reason !== "string" ||
        (executing && (typeof body.caseId !== "string" || typeof body.confirmEmail !== "string"))) {
      return result({ error: "Invalid support request" }, 400);
    }
    // Never permit a support action on the authenticated Owner, or on any
    // other administrator. Policy is repeated inside the service-only RPC.
    const { data: target, error: targetError } = await service.auth.admin.getUserById(body.id);
    if (targetError || !target?.user?.email) {
      return result({ error: "Target account is unavailable" }, 404);
    }
    const { data: otherAdmin, error: otherAdminError } = await service
      .from("lockvyn_admins").select("user_id").eq("user_id", body.id).maybeSingle();
    if (otherAdminError) return result({ error: "Unable to verify protected account" }, 503);
    if (!safeTarget(body.id, verified.user.id, Boolean(otherAdmin))) {
      return result({ error: "Administrator accounts cannot be changed here" }, 403);
    }
    if (!executing) {
      const { data: caseId, error: prepareError } =
        await service.rpc("lockvyn_admin_support_prepare", {
          p_actor: verified.user.id, p_target: body.id,
          p_kind: body.kind, p_reason: body.reason,
        });
      if (prepareError || typeof caseId !== "string") {
        return result({ error: "Unable to start case. Check the one-minute cooldown." }, 429);
      }
      return result({
        caseId, expiresInSeconds: 300, targetEmail: target.user.email,
        kind: body.kind, cloudOnly: true,
      });
    }
    // Explicit fresh TOTP challenge required; a refreshed AAL2 JWT cannot
    // extend the timestamp of its last factor verification.
    if (!freshVerifiedTotp(claims, Math.floor(Date.now()/1000))) {
      return result({ error: "Verify a fresh authenticator code before confirming this action" }, 403);
    }
    const { data: claimed, error: claimError } =
      await service.rpc("lockvyn_admin_support_claim", {
        p_actor: verified.user.id, p_case: body.caseId, p_target: body.id,
        p_kind: body.kind, p_email: body.confirmEmail,
      });
    if (claimError || !claimed) {
      return result({ error: "Confirmation expired, was already used, or email did not match" }, 409);
    }
    let status = "review_pending";
    let operationError = null;
    if (isAutomaticAction(body.kind)) {
      const { error: updateError } = await service.auth.admin.updateUserById(body.id, {
        ban_duration: body.kind === "suspend_24h" ? "24h" : "none",
      });
      operationError = updateError;
      status = updateError ? "failed" : "completed";
    }
    const { data: finished, error: finishError } =
      await service.rpc("lockvyn_admin_support_finish", {
        p_actor: verified.user.id, p_case: body.caseId, p_status: status,
      });
    if (finishError || !finished) {
      // The provider may have applied the action already; do NOT replay it
      // without checking its status with the support history/admin API.
      return result({ error: "Support state is uncertain. Check case history; do not retry." }, 503);
    }
    if (operationError) return result({ error: "Cloud Auth did not apply the operation; case logged as failed" }, 502);
    return result({
      status, caseId: body.caseId, cloudOnly: true,
      warning: status === "review_pending"
        ? "A review case was recorded; no factor, session or account was changed."
        : "Existing access JWTs may remain valid until expiry; offline vaults are not controlled.",
    });
  }
  if (body.action === "recover") {
    if (recoveryRedirect !== dashboardOrigin + "/recover") {
      return result({ error: "Account recovery link is not configured" }, 503);
    }
    const id = body.id;
    if (typeof id !== "string" ||
        !/^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/i.test(id)) {
      return result({ error: "Invalid user" }, 400);
    }
    // The approved administrator can request recovery of their own cloud login
    // too. Delivery remains email-verified, audited, and rate-limited just like
    // support requests for other accounts. This never changes the vault key.
    const { data: target, error: targetError } = await service.auth.admin.getUserById(id);
    if (targetError || !target.user?.email || !target.user.email_confirmed_at) {
      return result({ error: "Account not found or unconfirmed" }, 404);
    }
    const { data: permitted, error: claimError } = await service.rpc("lockvyn_claim_recovery", {
      p_actor: verified.user.id, p_target: id,
    });
    if (claimError) return result({ error: "Recovery cannot be requested" }, 403);
    if (!permitted) return result({ error: "A recovery email was already requested recently" }, 429);
    const { error: sendError } = await service.auth.resetPasswordForEmail(target.user.email, {
      redirectTo: recoveryRedirect,
    });
    if (sendError) return result({ error: "Email delivery failed. Check the configured SMTP service." }, 502);
    return result({ sent: true });
  }
  return result({ error: "Unsupported action" }, 400);
});
