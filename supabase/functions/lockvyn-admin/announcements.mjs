export function announcementQueryError(platform, version, locale) {
  if (platform !== "windows" && platform !== "android") return "Invalid platform";
  if (locale !== "en" && locale !== "ar") return "Invalid locale";
  if (typeof version !== "string" || version.length > 20) return "Invalid version";
  if (platform === "windows" &&
      !/^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$/.test(version))
    return "Invalid Windows version";
  if (platform === "android" &&
      (!/^(0|[1-9][0-9]{0,9})$/.test(version) || Number(version) > 2147483647))
    return "Invalid Android version";
  return null;
}

function hasSensitiveMarker(text) {
  return /[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/i.test(text) ||
    /\bBearer\s+[A-Za-z0-9._~+\/-]{20,}/i.test(text) ||
    /\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b/.test(text) ||
    /\b[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b/i.test(text);
}

export function announcementInputError(input) {
  if (!input || typeof input !== "object" || Array.isArray(input))
    return "Announcement content is required";
  const values = ["titleEn", "titleAr", "bodyEn", "bodyAr"];
  for (const key of values) {
    if (typeof input[key] !== "string") return "Both English and Arabic text fields are required";
    const value = input[key].trim();
    const max = key.startsWith("title") ? 120 : 800;
    if (value.length > max) return "Announcement text is too long";
    if (hasSensitiveMarker(value)) return "Remove account, credential, or device identifiers from the notice";
  }
  if (!["info", "warning", "critical"].includes(input.severity))
    return "Invalid severity";
  if (!["all", "windows", "android"].includes(input.targetPlatform))
    return "Invalid target platform";
  if (typeof input.notify !== "boolean") return "Invalid notification preference";
  for (const key of ["minVersion", "maxVersion"]) {
    if (input[key] !== null && input[key] !== undefined && typeof input[key] !== "string")
      return "Invalid target version";
    if (typeof input[key] === "string" && input[key].length > 20)
      return "Invalid target version";
  }
  const min = input.minVersion || null, max = input.maxVersion || null;
  if (input.targetPlatform === "all" && (min || max))
    return "Version bounds require a Windows or Android target";
  if (input.targetPlatform === "windows") {
    const pattern = /^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$/;
    if ((min && !pattern.test(min)) || (max && !pattern.test(max)))
      return "Windows target versions must use three numeric parts";
    if (min && max && compareVersionParts(min, max) > 0)
      return "Minimum version cannot exceed maximum version";
  }
  if (input.targetPlatform === "android") {
    const pattern = /^(0|[1-9][0-9]{0,9})$/;
    if ((min && (!pattern.test(min) || Number(min) > 2147483647)) ||
        (max && (!pattern.test(max) || Number(max) > 2147483647)))
      return "Android target versions must be valid version codes";
    if (min && max && Number(min) > Number(max))
      return "Minimum version cannot exceed maximum version";
  }
  if (input.expiresAt !== null && input.expiresAt !== undefined && input.expiresAt !== "") {
    if (typeof input.expiresAt !== "string" || input.expiresAt.length > 64 ||
        !Number.isFinite(Date.parse(input.expiresAt)))
      return "Invalid expiry time";
  }
  return null;
}

function compareVersionParts(a, b) {
  const x = a.split(".").map(Number), y = b.split(".").map(Number);
  for (let i = 0; i < 3; i++) if (x[i] !== y[i]) return x[i] < y[i] ? -1 : 1;
  return 0;
}
