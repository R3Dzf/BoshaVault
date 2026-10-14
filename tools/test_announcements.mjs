import assert from "node:assert/strict";
import {
  announcementInputError,
  announcementQueryError,
} from "../supabase/functions/lockvyn-admin/announcements.mjs";

const draft = {
  titleEn: "Service maintenance",
  titleAr: "صيانة الخدمة",
  bodyEn: "Cloud sync will be unavailable briefly.",
  bodyAr: "قد يتوقف التزامن السحابي لفترة قصيرة.",
  severity: "warning",
  targetPlatform: "windows",
  minVersion: "1.20.0",
  maxVersion: "1.22.0",
  notify: false,
  expiresAt: "2026-12-01T00:00:00.000Z",
};
assert.equal(announcementInputError(draft), null);
assert.equal(announcementInputError({ ...draft, minVersion: "1.23.0" }),
  "Minimum version cannot exceed maximum version");
assert.equal(announcementInputError({ ...draft, bodyAr: "owner@example.invalid" }),
  "Remove account, credential, or device identifiers from the notice");
assert.equal(announcementInputError({ ...draft, targetPlatform: "all" }),
  "Version bounds require a Windows or Android target");
assert.equal(announcementInputError({ ...draft, bodyEn: "Bearer abcdefghijklmnopqrstuvwxyz123456" }),
  "Remove account, credential, or device identifiers from the notice");
assert.equal(announcementInputError({ ...draft, bodyAr: "Bearer eyJabcdefghijk.abcdefghijk.abcdefghijk" }),
  "Remove account, credential, or device identifiers from the notice");
assert.equal(announcementInputError({ ...draft, targetPlatform: "android", minVersion: "21", maxVersion: "2147483648" }),
  "Android target versions must be valid version codes");
assert.equal(announcementInputError({ ...draft, notify: "true" }),
  "Invalid notification preference");

assert.equal(announcementQueryError("windows", "1.22.0", "en"), null);
assert.equal(announcementQueryError("android", "2147483647", "ar"), null);
assert.equal(announcementQueryError("windows", "1.22", "en"), "Invalid Windows version");
assert.equal(announcementQueryError("android", "2147483648", "en"), "Invalid Android version");
assert.equal(announcementQueryError("macos", "1.0.0", "en"), "Invalid platform");
assert.equal(announcementQueryError("android", "22", "fr"), "Invalid locale");
console.log("PASS bilingual content limits, identifier rejection, target/version validation, and locale selection");
