"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const {webcrypto} = require("node:crypto");
const source=fs.readFileSync("browser-extension/password-utils.js","utf8");
const sandbox={crypto:webcrypto,Uint32Array};
vm.createContext(sandbox);
vm.runInContext(source,sandbox);
const gen=sandbox.BoshaPassword.generate;
assert.equal(typeof gen,"function");
assert.throws(()=>gen(15),/Length/);
assert.throws(()=>gen(65),/Length/);
assert.throws(()=>gen(24,"false"),/symbol/);
assert.throws(()=>gen(24.5),/Length/);
const seen=new Set();
for(let i=0;i<600;i++){
  const pwd=gen(24,true);
  assert.equal(pwd.length,24);
  assert.match(pwd,/[a-z]/);
  assert.match(pwd,/[A-Z]/);
  assert.match(pwd,/[0-9]/);
  assert.match(pwd,/[!@#$%&*+\-=?_]/);
  assert.match(pwd,/^[a-zA-Z0-9!@#$%&*+\-=?_]+$/);
  assert.equal(seen.has(pwd),false,"Generator produced collision");
  seen.add(pwd);
}
for(let i=0;i<100;i++){
  const pwd=gen(64,false);
  assert.equal(pwd.length,64);
  assert.match(pwd,/[a-z]/);
  assert.match(pwd,/[A-Z]/);
  assert.match(pwd,/[0-9]/);
  assert.doesNotMatch(pwd,/[!@#$%&*+\-=?_]/);
}
console.log("PASS browser password generator: secure API, length, required groups, symbols, 600 no-collision sample");
