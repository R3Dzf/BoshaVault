"use strict";
// Runs in the isolated content-script world, never the page's JavaScript realm.
// Browser crypto.getRandomValues is cryptographically secure. Rejection sampling
// removes modulo bias and Fisher-Yates avoids predictable grouping positions.
globalThis.BoshaPassword = Object.freeze((() => {
  const lower = "abcdefghijkmnopqrstuvwxyz";
  const upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
  const numbers = "23456789";
  const special = "!@#$%&*+-=?_";
  function secureIndex(count) {
    if (!Number.isInteger(count) || count < 1 || count > 65536)
      throw new Error("Invalid alphabet size.");
    const max = 0x100000000;
    const bound = Math.floor(max / count) * count;
    const bytes = new Uint32Array(1);
    let value;
    do {
      crypto.getRandomValues(bytes);
      value = bytes[0];
    } while (value >= bound);
    return value % count;
  }
  function generate(length=24, symbols=true) {
    if (!Number.isInteger(length) || length < 16 || length > 64)
      throw new Error("Length must be from 16 to 64.");
    if (typeof symbols !== "boolean") throw new Error("Invalid symbol setting.");
    const groups = symbols ? [lower, upper, numbers, special] : [lower, upper, numbers];
    const chars = groups.map(group => group[secureIndex(group.length)]);
    const alphabet = groups.join("");
    while (chars.length < length) chars.push(alphabet[secureIndex(alphabet.length)]);
    for (let i=chars.length-1;i>0;i--) {
      const j = secureIndex(i+1);
      [chars[i],chars[j]]=[chars[j],chars[i]];
    }
    return chars.join("");
  }
  return {generate};
})());
