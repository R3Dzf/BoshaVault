#!/usr/bin/env node
// Payload and loading-path budget for the single-file admin dashboard.
import assert from "node:assert/strict";
import {readFileSync} from "node:fs";
const source=readFileSync(new URL("../supabase/functions/lockvyn-admin/ui.ts",import.meta.url),"utf8");
function utf8Bytes(value){return Buffer.byteLength(value,"utf8");}
const scriptStart=source.lastIndexOf('<script nonce="${n}">');
assert(scriptStart>=0,"Expected one nonce-protected inline dashboard script");
const htmlBudget=120*1024,scriptBudget=72*1024;
assert(utf8Bytes(source)<=htmlBudget,
  `Admin UI template exceeds ${htmlBudget} bytes: ${utf8Bytes(source)}`);
assert(utf8Bytes(source.slice(scriptStart))<=scriptBudget,
  `Admin inline script exceeds ${scriptBudget} bytes: ${utf8Bytes(source.slice(scriptStart))}`);
assert(source.includes('await loadRoleStatus()'),"Role status must load before admin data");
assert(source.includes('if(["owner","support"].includes(currentRole))tasks.push'),
  "Directory and support requests must load only for matching roles");
assert(source.includes('if(["owner","security"].includes(currentRole))tasks.push'),
  "Security reports must load only for matching roles");
assert(source.includes('if(["owner","release"].includes(currentRole))tasks.push'),
  "Release tools must load only for matching roles");
assert(source.includes("searchDelay=setTimeout"),"Directory search must remain debounced");
assert(source.includes("if(seq!==loadSequence||!accessToken)return"),"Stale directory results must be discarded");
console.log(`PASS admin UI payload budgets and role-specific loading (template ${utf8Bytes(source)} bytes)`);
