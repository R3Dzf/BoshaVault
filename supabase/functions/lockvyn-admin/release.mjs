export function releaseVersion(platform, value) {
  if (platform === "android") {
    if (typeof value !== "string" || !/^(0|[1-9][0-9]{0,9})$/.test(value)) return null;
    const n = Number(value);
    return Number.isSafeInteger(n) && n <= 2147483647 ? [n] : null;
  }
  if (platform !== "windows" || typeof value !== "string" ||
      !/^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$/.test(value)) return null;
  return value.split(".").map(Number);
}
export function compareReleaseVersions(platform, left, right) {
  const a = releaseVersion(platform, left), b = releaseVersion(platform, right);
  if (!a || !b) return null;
  for (let i = 0; i < a.length; i++) {
    if (a[i] !== b[i]) return a[i] < b[i] ? -1 : 1;
  }
  return 0;
}
export function validCandidateTag(platform, tag) {
  if (tag === null) return platform === "windows" || platform === "android";
  if (typeof tag !== "string") return false;
  return platform === "windows"
    ? /^windows-v(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$/.test(tag)
    : platform === "android"
      ? /^android-v(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$/.test(tag)
      : false;
}
export function validateReleasePolicy(input) {
  if (!input || !["windows", "android"].includes(input.platform)) return "Invalid platform";
  if (!validCandidateTag(input.platform, input.candidateTag)) return "Select a published stable release or latest stable";
  if (!releaseVersion(input.platform, input.minimumVersion)) return "Invalid minimum client version";
  if (!Number.isSafeInteger(input.rolloutPercent) ||
      input.rolloutPercent < 0 || input.rolloutPercent > 100) return "Rollout must be from 0 to 100";
  if (!["planned_rollout", "rollback", "minimum_version", "incident_response"].includes(input.reasonCode))
    return "Select a valid reason";
  if (input.platform === "windows" && input.candidateTag) {
    const candidate = input.candidateTag.slice("windows-v".length);
    if (compareReleaseVersions("windows", input.minimumVersion, candidate) > 0)
      return "Minimum version cannot exceed the selected release";
  }
  return null;
}
export function shouldOfferRelease({platform, currentVersion, availableVersion, candidateTag,
  availableTag, rolloutPercent, minimumVersion, sample}) {
  const current = releaseVersion(platform, currentVersion);
  const available = releaseVersion(platform, availableVersion);
  const minimum = releaseVersion(platform, minimumVersion);
  if (!current || !available || !minimum || !validCandidateTag(platform, availableTag) ||
      !Number.isSafeInteger(rolloutPercent) || rolloutPercent < 0 || rolloutPercent > 100 ||
      !Number.isSafeInteger(sample) || sample < 0 || sample > 99) return false;
  if (candidateTag && candidateTag !== availableTag) return false;
  const cmp = compareReleaseVersions(platform, availableVersion, currentVersion);
  // Never offer a downgrade, even during rollback or a forced minimum-version update.
  if (cmp === null || cmp <= 0) return false;
  const belowFloor = compareReleaseVersions(platform, currentVersion, minimumVersion) < 0;
  return belowFloor || sample < rolloutPercent;
}
