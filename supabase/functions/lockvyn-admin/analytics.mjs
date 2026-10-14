const METRICS = new Set(["signups","active_app_versions","update_uptake","sync_reliability","resource_usage"]);
const PLATFORMS = new Set(["all","windows","android"]);
const BUCKETS = new Set(["none","lt_1mib","1_10mib","gt_10mib"]);
const VERSION = /^([0-9]{1,5}(\.[0-9]{1,5}){0,3}(-[A-Za-z0-9.-]{1,20})?|unknown|all)$/;
function dayMillis(value) {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
  const millis = Date.parse(value + "T00:00:00Z");
  if (!Number.isFinite(millis) || new Date(millis).toISOString().slice(0,10) !== value) return null;
  return millis;
}
export function parseAnalyticsQuery(input,today=new Date()) {
  if (!input || typeof input !== "object" || Array.isArray(input))
    return {ok:false,error:"Choose a valid analytics date range."};
  const fromDate=input.fromDate,toDate=input.toDate,from=dayMillis(fromDate),to=dayMillis(toDate);
  const now=dayMillis(typeof today==="string"?today:today.toISOString().slice(0,10));
  if (from===null || to===null || now===null || from>to || to>now ||
      from<now-89*86400000 || to-from>89*86400000)
    return {ok:false,error:"Choose dates within the last 90 days."};
  return {ok:true,fromDate,toDate};
}
export function sanitizeAnalyticsReport(data,query) {
  if (!data || typeof data!=="object" || Array.isArray(data) ||
      data.fromDate!==query.fromDate || data.toDate!==query.toDate ||
      data.minimumCohort!==5 || !Array.isArray(data.rows) || data.rows.length>1500) return null;
  const allowed=new Set(["day","metric","platform","appVersion","outcome","resourceBucket","users","events"]);
  const rows=[];
  for (const row of data.rows) {
    if (!row || typeof row!=="object" || Array.isArray(row) ||
        Object.keys(row).some(key=>!allowed.has(key))) return null;
    if (typeof row.day!=="string" || dayMillis(row.day)===null ||
        row.day<query.fromDate || row.day>query.toDate || !METRICS.has(row.metric) ||
        !PLATFORMS.has(row.platform) || typeof row.appVersion!=="string" ||
        !VERSION.test(row.appVersion) ||
        !(row.outcome===null || row.outcome==="success" || row.outcome==="failure") ||
        !(row.resourceBucket===null || BUCKETS.has(row.resourceBucket)) ||
        !Number.isSafeInteger(row.users) || row.users<5 ||
        !Number.isSafeInteger(row.events) || row.events<row.users || row.events>1000000000) return null;
    rows.push({day:row.day,metric:row.metric,platform:row.platform,appVersion:row.appVersion,
      outcome:row.outcome,resourceBucket:row.resourceBucket,users:row.users,events:row.events});
  }
  return {fromDate:query.fromDate,toDate:query.toDate,minimumCohort:5,rows};
}
