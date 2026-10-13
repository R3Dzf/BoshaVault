# Lockvyn Admin Dashboard — Delivery plan

> **Source of truth:** This file and the linked GitHub issues. Do not mark a
> phase completed until its GitHub issue has every acceptance item checked and
> applicable CI, staging, security review and deployment evidence linked.
> An explicit owner instruction may administratively waive/defer unchecked
> gates so work can proceed; record that in the issue and mark the phase
> **waived**, never completed. Do not silently remove requirements.
>
> **Zero-cost rule:** All phases must use no-charge tools only. See
> [ZERO-COST-POLICY.md](ZERO-COST-POLICY.md). Hosted paid staging, cloud
> paid branches, paid overages, and billing upgrades are prohibited.

## Work order

| Phase | Area | Issue | State |
| --- | --- | --- | --- |
| 00 | Foundation & security gates | [#18](https://github.com/R3Dzf/BoshaVault/issues/18) | Completed |
| 01 | Overview & account directory | [#19](https://github.com/R3Dzf/BoshaVault/issues/19) | Completed |
| 02 | Account support actions | [#20](https://github.com/R3Dzf/BoshaVault/issues/20) | Owner-waived; acceptance incomplete |
| 03 | Device inventory & access | [#21](https://github.com/R3Dzf/BoshaVault/issues/21) | In progress by owner direction |
| 04 | Security Center | [#22](https://github.com/R3Dzf/BoshaVault/issues/22) | Queued |
| 05 | Encrypted sync & quotas | [#23](https://github.com/R3Dzf/BoshaVault/issues/23) | Queued |
| 06 | Release Manager | [#24](https://github.com/R3Dzf/BoshaVault/issues/24) | Queued |
| 07 | Announcements & notices | [#25](https://github.com/R3Dzf/BoshaVault/issues/25) | Queued |
| 08 | Support tickets | [#26](https://github.com/R3Dzf/BoshaVault/issues/26) | Queued |
| 09 | Privacy-conscious analytics | [#27](https://github.com/R3Dzf/BoshaVault/issues/27) | Queued |
| 10 | Roles, operations & sign-off | [#28](https://github.com/R3Dzf/BoshaVault/issues/28) | Queued |

### Required delivery sequence per phase

1. Inspect the deployed app/backend and existing data schema; identify gaps.
2. Specify UX flows, permissions, privacy effects, rollback/compatibility strategy and a documented **$0 execution/test path**.
3. Implement code and migrations on a **dedicated short-lived branch** (small commits).
4. Add automated functional, security, regression and accessibility tests on a free local or quota-limited runner; never create billable staging.
5. Verify denial paths for unauthenticated, AAL1, unauthorized and suspended actors.
6. Review and merge after CI succeeds on the **exact commit**.
7. Deploy behind safe defaults; verify real behavior without exposing vault secrets.
8. Record proof in the phase issue. Close a phase as completed only after every acceptance item passes. If the owner explicitly waives remaining items, close it as **waived**, list every deferred item, and proceed only at that owner's direction.

**Gate:** no paid resource may be created, even temporarily. A phase with unchecked requirements is not complete. Delayed work and blocked features stay visible in their issue; an owner waiver is recorded as deferred, never as passed.

**Current owner-directed transition:** Phase 02 (#20) was closed with its unchecked acceptance gates deferred at the owner's explicit direction. This administrative closure is not a passing phase. Phase 03 (#21) is now in progress at that direction; see [Phase 02 record](ADMIN-PHASE-02.md).

## Existing capabilities to preserve

- Supabase Auth admin list (pagination) and user email/verification/verified MFA.
- Admin requires AAL2 with a server-managed allowlist; no client admin switch.
- Owner-directed cloud password recovery, audited and target-rate-limited.
- Cloudflare worker hosts dashboard HTML, while Supabase Edge Function enforces
  authorization. No service-role secret is allowed in the browser/Cloudflare UI.
- Verified combined Windows+Android releases and old update feeds must remain
  operational.

## Security non-negotiables

- No administrator can see user vault passwords, master passphrases, secret
  recovery material, encryption keys or decrypted snapshots.
- Admin actions may limit **cloud** access, never claim to remotely destroy or
  decrypt already stored **offline** vault data.
- Every privileged action requires verified identity, minimal privileges,
  explicit user-impact disclosure, audit metadata, and denial tests.
- Use only opt-in and minimal telemetry. No silent device inventory/fingerprinting.
- Never enable destructive controls solely on the client; check permissions on
  each backend request.
- No bulk email/password resets, insecure MFA bypass, or remote silent autofill.

## Completion definition

All 11 phase issues resolved. A phase counts as completed only when its
checklist and evidence pass; owner-waived phases remain visibly incomplete and
do not count as final sign-off. Complete one final security/threat-model
review, update docs, rollback procedures and support playbook, and verify all
published Windows and Android versions remain updatable.

**Status:** Phases 00–01 completed. Phase 02 is owner-waived with acceptance
gates deferred; Phase 03 is in progress by that direction. Phases 04–10 are
queued.