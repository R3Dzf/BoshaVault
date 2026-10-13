import { createClient } from "npm:@supabase/supabase-js@2.58.0";
import { html } from "./ui.ts";

// The service-role key is only read inside this Edge Function. It is never
// embedded in the HTML response, shipped to clients, or logged.
const apiUrl = Deno.env.get("SUPABASE_URL") ?? "";
const publicKey = Deno.env.get("SUPABASE_ANON_KEY") ?? "";
const serviceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? "";
const recoveryRedirect = Deno.env.get("LOCKVYN_RECOVERY_REDIRECT_URL") ?? "";
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
    },
  });
}

function nonce(): string {
  const b = crypto.getRandomValues(new Uint8Array(24));
  return btoa(String.fromCharCode(...b));
}

Deno.serve(async (request) => {
  const url = new URL(request.url);
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
  if (request.headers.get("origin") !== url.origin) return result({ error: "Forbidden" }, 403);
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
  let assurance = "";
  try {
    const encoded = token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
    const claims = JSON.parse(atob(encoded));
    assurance = claims.aal;
    if (claims.sub !== verified.user.id || claims.role !== "authenticated") {
      return result({ error: "Unauthorized" }, 401);
    }
  } catch { return result({ error: "Unauthorized" }, 401); }
  if (assurance !== "aal2") return result({ error: "Admin access requires verified 2FA", mfaRequired: true }, 403);
  const { data: admin, error: permissionError } = await service
    .from("lockvyn_admins").select("user_id").eq("user_id", verified.user.id).maybeSingle();
  if (permissionError) return result({ error: "Administration is not configured" }, 503);
  if (!admin) return result({ error: "Not an authorized Lockvyn administrator" }, 403);

  let body: Record<string, unknown>;
  try {
    const text = await request.text();
    if (text.length > 2048) return result({ error: "Request too large" }, 413);
    const parsed = JSON.parse(text);
    if (!parsed || Array.isArray(parsed) || typeof parsed !== "object") throw new Error();
    body = parsed;
  } catch { return result({ error: "Invalid request" }, 400); }

  if (body.action === "list") {
    const page = body.page;
    if (typeof page !== "number" || !Number.isSafeInteger(page) || page < 1 || page > 10000) {
      return result({ error: "Invalid page" }, 400);
    }
    const { data, error } = await service.auth.admin.listUsers({ page, perPage: 30 });
    if (error || !data) return result({ error: "Unable to load accounts" }, 502);
    return result({
      page, total: data.total ?? null,
      users: data.users.map((user) => ({
        id: user.id, email: user.email ?? "", createdAt: user.created_at,
        lastSignInAt: user.last_sign_in_at ?? null,
        confirmed: Boolean(user.email_confirmed_at),
        mfa: Boolean(user.factors?.some((factor) => factor.status === "verified")),
      })),
    });
  }
  if (body.action === "recover") {
    if (!/^https:\/\/[^\s]+$/.test(recoveryRedirect) ||
        !new URL(recoveryRedirect).hostname.endsWith(".supabase.co")) {
      return result({ error: "Account recovery link is not configured" }, 503);
    }
    const id = body.id;
    if (typeof id !== "string" ||
        !/^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/i.test(id)) {
      return result({ error: "Invalid user" }, 400);
    }
    if (id === verified.user.id) return result({ error: "Use your own account recovery flow" }, 400);
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
