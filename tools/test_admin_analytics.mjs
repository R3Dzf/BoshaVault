import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { parseAnalyticsQuery, sanitizeAnalyticsReport } from "../supabase/functions/lockvyn-admin/analytics.mjs";

const today="2026-10-14", query={fromDate:today,toDate:today};
assert.equal(parseAnalyticsQuery({fromDate:"2026-08-01",toDate:today},today).ok,true);
assert.equal(parseAnalyticsQuery({fromDate:"2026-07-16",toDate:today},today).ok,false);
assert.equal(parseAnalyticsQuery({fromDate:today,toDate:"2026-10-15"},today).ok,false);
assert.equal(parseAnalyticsQuery({fromDate:"2026-10-15",toDate:today},today).ok,false);
assert.equal(parseAnalyticsQuery({fromDate:"2026-02-30",toDate:today},today).ok,false);
const row={day:today,metric:"sync_reliability",platform:"windows",appVersion:"3.2.0",
  outcome:"success",resourceBucket:null,users:5,events:7};
assert.equal(sanitizeAnalyticsReport({fromDate:today,toDate:today,minimumCohort:5,rows:[row]},query).rows[0].users,5);
assert.equal(sanitizeAnalyticsReport({fromDate:today,toDate:today,minimumCohort:5,
  rows:[{...row,userId:"private"}]},query),null,"Identity fields are rejected");
assert.equal(sanitizeAnalyticsReport({fromDate:today,toDate:today,minimumCohort:5,
  rows:[{...row,users:4}]},query),null,"Groups below five are rejected at the Edge boundary");
assert.equal(sanitizeAnalyticsReport({fromDate:today,toDate:today,minimumCohort:5,
  rows:[{...row,appVersion:"person@example.invalid"}]},query),null);
assert.equal(sanitizeAnalyticsReport({fromDate:today,toDate:today,minimumCohort:5,
  rows:[{...row,metric:"raw_errors"}]},query),null);

const migration=readFileSync(new URL("../supabase/migrations/20261014030000_phase09_privacy_analytics.sql",import.meta.url),"utf8");
const edge=readFileSync(new URL("../supabase/functions/lockvyn-admin/index.ts",import.meta.url),"utf8");
assert.match(migration,/analytics_events enable row level security/i);
assert.match(migration,/analytics_events force row level security/i);
assert.match(migration,/interval '90 days'/i);
assert.match(migration,/cron\.schedule\([\s\S]*lockvyn-analytics-retention/);
assert.match(migration,/count\(distinct user_id\)>=5/i);
assert.match(migration,/delete from lockvyn_private\.analytics_events where user_id=u/i);
assert.match(edge,/lockvyn_admin_privacy_analytics/);
assert.match(edge,/verifiedUserGate/);
const ui=readFileSync(new URL("../supabase/functions/lockvyn-admin/ui.ts",import.meta.url),"utf8");
const android=readFileSync(new URL("../android/app/src/main/java/com/bosha/vault/MainActivity.java",import.meta.url),"utf8");
const androidClient=readFileSync(new URL("../android/app/src/main/java/com/bosha/vault/CloudClient.java",import.meta.url),"utf8");
const windows=readFileSync(new URL("../src/BoshaVault.Windows/CloudSyncDialog.cs",import.meta.url),"utf8");
const windowsModel=readFileSync(new URL("../src/BoshaVault.Core/CloudAccount.cs",import.meta.url),"utf8");
assert.ok(android.includes("Share privacy-safe analytics for this cloud account"));
assert.ok(android.includes("Refresh privacy choice"));
assert.ok(android.includes('profile.optBoolean("analyticsConsent",false)'));
assert.ok(androidClient.includes("lockvyn_analytics_set_consent"));
assert.ok(androidClient.includes("lockvyn_analytics_record_event"));
assert.ok(androidClient.includes('"analyticsConsent"'));
assert.ok(windows.includes("Refresh analytics choice"));
assert.ok(windows.includes("Apply analytics choice"));
assert.ok(windowsModel.includes("bool AnalyticsConsent { get; set; }"));
for(const marker of ["analytics-from","analytics-to","analytics-chart","analytics-table-body",
  "analytics-export","URL.createObjectURL","minimumCohort","Privacy-conscious analytics"])
  assert(ui.includes(marker),"Missing analytics UI contract: "+marker);
console.log("PASS analytics date bounds, strict aggregate shape, RLS, retention, consent deletion, and client/dashboard controls");
