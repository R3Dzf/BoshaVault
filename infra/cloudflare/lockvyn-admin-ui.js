// HTML-only host for the Supabase admin UI. Never processes passwords or tokens.
const upstream = "https://ghmbcfqiomggoxgwghjs.supabase.co/functions/v1/lockvyn-admin";
const authOrigin = "https://ghmbcfqiomggoxgwghjs.supabase.co";

addEventListener('fetch', event => event.respondWith(handle(event.request)));
async function handle(request) {
    const url = new URL(request.url);
    if (request.method !== "GET" || !["/", "/recover", "/security"].includes(url.pathname))
      return new Response("Not found", {status:404});
    const response = await fetch(upstream + (url.pathname === "/recover" ? "/recover" : url.pathname === "/security" ? "/security" : ""), {
      redirect:"manual", headers:{"Accept":"text/html"}
    });
    if (response.status !== 200) return new Response("Dashboard unavailable", {status:502});
    const html = await response.text();
    const script = html.match(/<script nonce="([A-Za-z0-9+/_=-]+)">/);
    const style = html.match(/<style nonce="([A-Za-z0-9+/_=-]+)">/);
    if (html.length > 130000 || !html.includes("<html lang=\"en\">") ||
        !script || !style || script[1] !== style[1])
      return new Response("Unexpected dashboard content", {status:502});
    const nonce = script[1];
    return new Response(html, {
      status:200, headers:{
        "Content-Type":"text/html; charset=utf-8",
        "Cache-Control":"no-store",
        "X-Content-Type-Options":"nosniff",
        "X-Frame-Options":"DENY",
        "Referrer-Policy":"no-referrer",
        "Content-Security-Policy":
          "default-src 'none'; base-uri 'none'; frame-ancestors 'none'; object-src 'none'; " +
          "script-src 'nonce-" + nonce + "'; style-src 'nonce-" + nonce + "'; " +
          "img-src 'self' data:; connect-src 'self' " + authOrigin + "; form-action 'self'",
      }
    });
}
