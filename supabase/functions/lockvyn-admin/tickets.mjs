// Phase 08 ticket request policy shared by the Edge Function and synthetic tests.
// Support content is bounded, redacted, consent-based, and independent of vault data.
const UUID=/^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/i;
const DIAGNOSTIC_FIELDS=new Set(["platform","appVersion","osVersion","errorCode"]);
const USER_ACTIONS=new Set(["ticketCreate","ticketList","ticketGet","ticketReply","ticketClose","ticketReopen"]);
const ADMIN_ACTIONS=new Set(["ticketAdminList","ticketAdminGet","ticketAdminAssign","ticketAdminStatus","ticketAdminEscalate","ticketAdminReply"]);
const STATUSES=new Set(["open","in_progress","escalated","closed"]);
const PRIORITIES=new Set(["low","normal","high"]);

export function validTicketId(value) {
  return typeof value==="string" && UUID.test(value) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value);
}

export function redactSupportText(value) {
  if(typeof value!=="string")return "";
  return value.normalize("NFC")
    .replace(/\u0000/g,"")
    .replace(/-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----/gi,"[redacted private key]")
    .replace(/\bBearer\s+[A-Za-z0-9._~+/-]{12,}=?/gi,"Bearer [redacted]")
    .replace(/\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b/g,"[redacted token]")
    .replace(/\b(password|passphrase|master[ _-]?key|recovery[ _-]?key|api[ _-]?key|authorization|access[ _-]?token|refresh[ _-]?token|secret)\b\s*[:=]\s*["']?[^\s,;]+/gi,"[redacted sensitive field]")
    .replace(/\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/gi,"[redacted email]")
    .replace(/\b[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}\b/gi,"[redacted id]")
    .replace(/[\u0001-\u0008\u000B\u000C\u000E-\u001F\u007F]/g," ")
    .replace(/[ \t]{2,}/g," ")
    .trim();
}

function textField(value,max,min=1) {
  if(typeof value!=="string")return null;
  const text=redactSupportText(value);
  return text.length>=min&&text.length<=max?text:null;
}

export function safeDiagnostics(value,consent) {
  if(value===undefined||value===null)return consent===false?{ok:true,diagnostics:null}: {ok:false,error:"Attach diagnostics only after explicit consent."};
  if(consent!==true||!value||typeof value!=="object"||Array.isArray(value))return {ok:false,error:"Diagnostic consent is required."};
  const keys=Object.keys(value);
  if(keys.length<1||keys.length>4||keys.some(k=>!DIAGNOSTIC_FIELDS.has(k)))return {ok:false,error:"Diagnostics contain unsupported fields."};
  const diagnostics={};
  for(const key of keys){
    const clean=textField(value[key],120);
    if(!clean)return {ok:false,error:"Diagnostic fields are invalid."};
    if(key==="platform"&&!["windows","android"].includes(clean.toLowerCase()))return {ok:false,error:"Diagnostic platform is invalid."};
    if(key==="appVersion"&&!/^[0-9A-Za-z.+_-]{1,48}$/.test(clean))return {ok:false,error:"Diagnostic app version is invalid."};
    if(key==="errorCode"&&!/^[0-9A-Za-z._-]{1,80}$/.test(clean))return {ok:false,error:"Diagnostic error code is invalid."};
    diagnostics[key]=clean;
  }
  return {ok:true,diagnostics};
}

export function parseUserTicketRequest(body) {
  if(!body||typeof body!=="object"||Array.isArray(body)||!USER_ACTIONS.has(body.action))return {ok:false,error:"Invalid support ticket action."};
  if(body.action==="ticketList")return {ok:true,action:"list",ticketId:null,payload:{}};
  if(body.action==="ticketCreate"){
    if(!body.ticket||typeof body.ticket!=="object"||Array.isArray(body.ticket))return {ok:false,error:"Ticket details are required."};
    if(Object.keys(body.ticket).some(key=>!["subject","body","diagnosticConsent","diagnostics"].includes(key)))
      return {ok:false,error:"Only sanitized diagnostics are accepted; arbitrary file attachments are not supported."};
    const subject=textField(body.ticket.subject,120,3),message=textField(body.ticket.body,4000);
    if(!subject||!message)return {ok:false,error:"Enter a short title and message (up to 4,000 characters)."};
    if(typeof body.ticket.diagnosticConsent!=="boolean")return {ok:false,error:"Choose whether to include optional diagnostics."};
    const check=safeDiagnostics(body.ticket.diagnostics,body.ticket.diagnosticConsent);
    if(!check.ok)return check;
    return {ok:true,action:"create",ticketId:null,payload:{subject,body:message,diagnosticConsent:body.ticket.diagnosticConsent,diagnostics:check.diagnostics}};
  }
  if(!validTicketId(body.ticketId))return {ok:false,error:"A valid ticket ID is required."};
  if(body.action==="ticketGet")return {ok:true,action:"get",ticketId:body.ticketId,payload:{}};
  if(body.action==="ticketClose"||body.action==="ticketReopen")return {ok:true,action:body.action==="ticketClose"?"close":"reopen",ticketId:body.ticketId,payload:{}};
  const message=textField(body.body,4000);
  if(!message)return {ok:false,error:"Reply text is required (up to 4,000 characters)."};
  return {ok:true,action:"reply",ticketId:body.ticketId,payload:{body:message}};
}

export function parseAdminTicketRequest(body) {
  if(!body||typeof body!=="object"||Array.isArray(body)||!ADMIN_ACTIONS.has(body.action))return {ok:false,error:"Invalid agent ticket action."};
  if(body.action==="ticketAdminList")return {ok:true,action:"list",ticketId:null,payload:{},write:false};
  if(!validTicketId(body.ticketId))return {ok:false,error:"A valid ticket ID is required."};
  if(body.action==="ticketAdminGet")return {ok:true,action:"get",ticketId:body.ticketId,payload:{},write:false};
  if(body.action==="ticketAdminAssign"){
    if(body.assigneeId!==null&&!validTicketId(body.assigneeId))return {ok:false,error:"Assignee must be an active administrator."};
    return {ok:true,action:"assign",ticketId:body.ticketId,payload:{assigneeId:body.assigneeId},write:true};
  }
  if(body.action==="ticketAdminStatus"){
    if(!STATUSES.has(body.status)||!PRIORITIES.has(body.priority))return {ok:false,error:"Choose a valid status and priority."};
    return {ok:true,action:"status",ticketId:body.ticketId,payload:{status:body.status,priority:body.priority},write:true};
  }
  if(body.action==="ticketAdminEscalate")return {ok:true,action:"escalate",ticketId:body.ticketId,payload:{},write:true};
  const message=textField(body.body,4000);
  if(!message)return {ok:false,error:"Reply text is required (up to 4,000 characters)."};
  return {ok:true,action:"reply",ticketId:body.ticketId,payload:{body:message},write:true};
}

export function isAdminTicketAction(action) { return ADMIN_ACTIONS.has(action); }
