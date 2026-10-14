// Server-side role scope. Unknown and unassigned actions fail closed.
const ROLE_ACTIONS={
  owner:new Set(["list","recover","supportPrepare","supportExecute","supportStatus","supportHistory",
    "deviceList","deviceRevoke","syncControl","securityEvents","privacyAnalytics",
    "releaseManager","releasePolicySet","announcements","announcementSave","announcementPublish","announcementArchive",
    "roleList","roleAudit","roleAssign","roleRevoke",
    "ticketAdminList","ticketAdminGet","ticketAdminAssign","ticketAdminEscalate","ticketAdminStatus","ticketAdminReply"]),
  support:new Set(["list","recover","supportPrepare","supportExecute","supportStatus","supportHistory",
    "ticketAdminList","ticketAdminGet","ticketAdminAssign","ticketAdminEscalate","ticketAdminStatus","ticketAdminReply"]),
  security:new Set(["deviceList","deviceRevoke","syncControl","securityEvents","privacyAnalytics"]),
  release:new Set(["releaseManager","releasePolicySet","announcements","announcementSave","announcementPublish","announcementArchive"]),
};
export function isAdminRole(role){return typeof role==="string"&&Object.hasOwn(ROLE_ACTIONS,role);}
export function adminRoleAllows(role,action){
  return typeof role==="string"&&typeof action==="string"&&Object.hasOwn(ROLE_ACTIONS,role)&&ROLE_ACTIONS[role].has(action);
}
export function adminRoleActions(role){return typeof role==="string"&&Object.hasOwn(ROLE_ACTIONS,role)?[...ROLE_ACTIONS[role]].sort():[];}
