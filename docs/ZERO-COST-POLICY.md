# Lockvyn — Zero-cost operations policy

**Owner decision: 2026-10-13. Hard budget: $0.**

All dashboard phases, Windows/Android development, cloud authentication,
releases and staging checks MUST stay within no-charge plans or fully
self-hosted/local free software. No paid resource is authorized.

## Rules

1. **Never create a paid Supabase branch or project.** A temporary
   Supabase branch was quoted at **USD 0.01344/hour** and was explicitly
   rejected. It was **not created**.
2. Never enable paid tiers, auto-top-ups, overages, credit purchases, or add
   billing/payment methods for Lockvyn.
3. Use an **isolated local test harness** with synthetic users and tokens
   (and, where feasible, a free temporary Docker-backed Supabase stack on
   a local/free CI runner) for security tests. Do **not** write fake users
   or test password resets into the live cloud project.
4. Existing Supabase Free and Cloudflare Workers Free resources may be used
   only within their published limits. The availability of a free plan does
   not imply unlimited usage, forever-free hosting, or guaranteed uptime.
5. GitHub Actions and artifact storage must stay within applicable free
   usage quotas. Use concurrency cancellation for old PR runs, bound job
   timeouts, avoid excessive artifacts and stop on quota exhaustion.
6. If a free quota or technical limit is reached, **fail closed / pause
   that feature**. Do not turn on billing or switch to a paid provider
   as a fallback. Notify the project owner and choose a free architecture
   or defer the feature visibly in its issue.
7. All new integrations and design proposals require a **$0 acceptance
   note**: where it runs, free limits, how usage is constrained, how to
   operate/roll back without billing, and evidence that it does not require
   a paid add-on.
8. Releases must preserve user data and existing signing / integrity
   checks. Cost savings never justify weakening encryption or access controls.

## Approved approaches (subject to no-charge quota)

| Need | Default | Guardrail |
|---|---|---|
| Cloud accounts | Existing Supabase Free project | No additional paid projects/branches |
| Admin API / database | Current project's free allocation | Monitor quotas, never enable paid overages |
| Admin HTML | Cloudflare Workers Free deployment | Respect request/CPU limits |
| Testing AAL1 / non-admin | Local synthetic Edge Function harness | No real user secrets, no production mutations |
| Full integration staging | Local Supabase CLI / Docker when feasible | No billable hosted staging |
| Windows + Android CI | GitHub Actions within free included minutes | Limit runs and archive size |
| Release binaries | GitHub Releases under applicable policies | Verify checksums and update compatibility |
| Notifications / email | Existing free allowance only | Back off or pause on limit; no paid SMTP |

## Evidence / phase gate

The roadmap `ADMIN-ROADMAP.md` and phase state
`admin-phases.json` include `zero_cost_only`. Do not close any
phase without demonstrating a zero-cost route to acceptance tests.
Security work that cannot be tested safely without paid infrastructure
remains **blocked**, not falsely marked complete.

## Billing caveat

This repository cannot prevent an external account owner from enabling
billing manually, nor can it guarantee that third-party free plans will
remain unchanged. Before any new provider integration, verify the live
plan's price and account billing settings. Never enter card details or
authorize charges for this project.
