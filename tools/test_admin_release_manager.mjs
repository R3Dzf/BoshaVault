import assert from "node:assert/strict";
import {shouldOfferRelease, validateReleasePolicy} from "../supabase/functions/lockvyn-admin/release.mjs";

const policy = (overrides={}) => ({
  platform:"windows",candidateTag:"windows-v1.22.0",minimumVersion:"1.20.0",
  rolloutPercent:25,reasonCode:"planned_rollout",...overrides,
});
assert.equal(validateReleasePolicy(policy()),null);
assert.equal(validateReleasePolicy(policy({candidateTag:null})),null,"latest stable remains the default feed");
assert.match(validateReleasePolicy(policy({platform:"macos"}),/platform/);
assert.match(validateReleasePolicy(policy({candidateTag:"windows-v1.22.0-beta.1"}),/stable/);
assert.match(validateReleasePolicy(policy({minimumVersion:"1.23.0"}),/exceed/);
assert.match(validateReleasePolicy(policy({rolloutPercent:101}),/0 to 100/);
assert.match(validateReleasePolicy(policy({reasonCode:"free text"}),/reason/);
assert.equal(validateReleasePolicy(policy({
  platform:"android",candidateTag:"android-v1.22.0",minimumVersion:"120000",rolloutPercent:0
})),null);

const common={platform:"windows",currentVersion:"1.19.0",availableVersion:"1.22.0",
  candidateTag:"windows-v1.22.0",availableTag:"windows-v1.22.0",rolloutPercent:0,
  minimumVersion:"1.20.0",sample:99};
assert.equal(shouldOfferRelease(common),true,"below-minimum clients get the required update despite a paused rollout");
assert.equal(shouldOfferRelease({...common,currentVersion:"1.20.0"}),false,"paused rollout hides optional updates");
assert.equal(shouldOfferRelease({...common,currentVersion:"1.20.0",rolloutPercent:25,sample:24}),true);
assert.equal(shouldOfferRelease({...common,currentVersion:"1.20.0",rolloutPercent:25,sample:25}),false);
assert.equal(shouldOfferRelease({...common,candidateTag:"windows-v1.21.0"}),false,"candidate pin must match");
assert.equal(shouldOfferRelease({...common,currentVersion:"1.23.0"}),false,"policy cannot downgrade");
assert.equal(shouldOfferRelease({...common,platform:"android",currentVersion:"20",
  availableVersion:"22",candidateTag:"android-v1.22.0",availableTag:"android-v1.22.0",
  minimumVersion:"21",rolloutPercent:0}),true);
console.log("release manager policy checks passed");
