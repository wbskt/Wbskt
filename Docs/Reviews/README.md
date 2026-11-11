# Architecture reviews

A full architecture review of every Wbskt component and the infrastructure runs **every 5th day**,
but only when something was committed since the last one. This file is the brief the review follows.
Change the lanes or checks here, not in the scheduled task that starts it.

Reports live beside this file as `YYYY-MM-DD.md`, one per review. The newest report's date is the
"last review" date. Older reviews, written before this routine, are in the project files
(`full-arch-review/`, `workflow-review/`, `db-review/` and others).

## 1. Decide whether to run

1. Take the date `D` of the newest `Docs/Reviews/YYYY-MM-DD.md` (none yet: 2026-11-02, the last
   full review, wbskt/Wbskt#537).
2. List commits on `master` since `D` in wbskt/Wbskt, wbskt/Wbskt.Console, wbskt/Wbskt.Python,
   wbskt/wbskt.dotnet, wbskt/Wbskt.Arduino and richardjoy530/fluffy-waddle.
3. **No commits anywhere: stop.** Write nothing and file nothing. Otherwise carry on, and note which
   areas changed; those get the deep pass.
4. Map every changed path to the lane that owns it (section 2). A path no lane names goes to the
   nearest lane for this review, and the report lists it under "Unowned paths" so this table can be
   fixed in the report's PR.

## 2. Lanes

Run the ten lanes in parallel. Each lane reviews everything changed since `D` in depth, then takes a
lighter pass over the rest of its area for drift. A lane also owns the tests (`Tests/`) and the docs
(`Docs/`) for its area, and says where they no longer match the code. Every lane looks for: inconsistencies between
components, missed or misused design patterns, dead or obsolete code, edge cases, performance, and
breaks from [Coding.Conventions.md](../Coding.Conventions.md) (console:
`CODING_GUIDELINES.md`).

| Lane | Area | Also checks |
|---|---|---|
| Auth and management | `Hosts/Wbskt.Auth.Host` and `Hosts/Wbskt.Management.Host` outside the billing and access code below (clients, policies, integrations, templates, readings, audit, the outbound mail queue in `Services/Email`), `Wbskt.Infrastructure`, `Models`, `Events`, `Clients/Wbskt.Api.Client` | `Result` vs fault handling, caching and invalidation, SignalR events and their console listeners, background jobs that run on every replica, rate limits, mail kept until the relay takes it and never sent twice |
| Credits and usage metering | `CreditUsageProvider`, `RunCountersProvider`, `DefaultCreditCostCalculator` and the credit paths in `RunStarter`, `RetryExecutor`, `RunFinalizer` and `TestRunStarter` (`Wbskt.Workflow`); the engine's `CreditUsagePoster`; the management host's `Services/Billing` and its usage, credit-cap and message-stats controllers; `MessageThrottle` in `Wbskt.Gateway.Core`; the `*Credit*` and `*Usage*` tables and their procedures | every billable action is counted exactly once (retries, test runs, recovery after a crash, two replicas, a reconnect); credits drawn in the documented order (plan first, then packs, oldest expiry first) and caps held exactly under concurrent runs; month boundaries in UTC from `TimeProvider`; what metering does while the auth host or the broker is down and whether the hourly reconcile repairs it; the 80% and 100% announcements and the 2x throttle firing once and clearing; what a Business tenant's overage report counts against what [Billing.Policies.md](../Billing.Policies.md) says it bills |
| Billing and pricing model | the auth host's `Services/Billing`, `TenantPlanService` and its billing, webhook and plan controllers; `Wbskt.Infrastructure/Billing` (`PlanCatalog`); `Models/Wbskt.Models/Billing`, `Events/Wbskt.Events/Billing`; the billing, checkout, subscription and credit-purchase tables in `Wbskt.Database.Auth`; [Billing.md](../Billing.md), [Billing.Policies.md](../Billing.Policies.md); the site's plans (`app/src/app/layout/plans`) and terms in richardjoy530/fluffy-waddle | webhook signatures, replays, duplicates and out-of-order events; a payment that succeeds at the provider but fails here, and the reverse; upgrade with proration, downgrade at period end, cancel, refund and a second checkout; billing notices and emails sent once; **one set of prices and limits**: `PlanCatalog.BuiltIn`, its override layers, Billing.md, the site's plan table and anything the console shows must agree, and every mismatch is a finding; edges of the model (a tenant exactly at a limit, a downgrade while over the lower plan's limits, a pack bought the day it expires) and whether each plan still clears its margin against `capacity/` in the project files |
| Tenants, users and access | the auth host's tenant, workspace, invitation, account, sign-in and session code (`AccessControlService`, `TenantService`, `TenantLimits`, `TenantUsageService`, `WorkspaceService`, `RegistrationService`, `SignInService`, `AccountDeletionService` and their controllers); the role, permission, member and invitation tables; `WorkspacePermissionFilter` and `ScopedPermissionFilter` in `Wbskt.Infrastructure`; [AccessControl.md](../AccessControl.md); the console's members page and `features/auth` | one tenant never reaching another's data, and the 403/404 rules in [Coding.Conventions.md](../Coding.Conventions.md); every workspace-scoped action carrying `[RequiresPermission]`; granting a role above your own, removing the last owner, an invitation redeemed twice, late or by the wrong account; plan limits on members and workspaces enforced on every path that adds one; sessions and cached access dropped when a role, membership or the account changes or is deleted; account deletion reaching billing, mail and the workspace database; sign-up and sign-in abuse (enumeration, lockout, reserved names) |
| Gateways | `Wbskt.Gateway.Core`, the WebSocket and MQTT gateway hosts, `Clients/Wbskt.Protocol`, `Clients/Wbskt.Simulator`, `Tests/Conformance`, `Tests/Wbskt.LoadTester` | protocol and close codes match the docs in `Docs/`, the conformance suite covers what the protocol docs promise, reconnect and offline buffer behaviour on the server side, token handling |
| Client libraries | wbskt/Wbskt.Python, wbskt/wbskt.dotnet, wbskt/Wbskt.Arduino, and the site's `docs` and `get-started` pages | the three libraries behave alike (reconnect and back-off, `Retry-After`, offline buffer, credential storage, close-code handling) and each passes the current conformance suite; public API surface and breaking changes since the last release; thread and `asyncio` safety, cancellation and disposal; supported runtimes and boards; the version in every file that carries one, release workflows and trusted publishing; README and site examples that still run against today's API |
| Workflow engine | `Wbskt.Workflow*` (including `Wbskt.Workflow.Builder`), the engine host, `Tools/Wbskt.Workflow.Exporter` | run lifecycle, leases and leader election, retries and idempotency, waits and long-running runs, node contracts vs the console designer |
| Database | `Databases/` | see "Database checks" below |
| Console and site | wbskt/Wbskt.Console, and richardjoy530/fluffy-waddle outside the pages the billing and libraries lanes take | feature-service structure, calls to endpoints that changed or no longer exist, unversioned API calls, dead components, style budgets, live-update handlers; site claims (features, limits, supported devices) that no longer match what ships |
| Infra and CI | `deploy/`, `.github/workflows/`, `Config/`, `Deploy-Databases.ps1`, and the CI and release workflows of the other repos | compose services, networks, ports, env vars and secrets, deploy order and health waits, CI time and skipped checks, telemetry and alerts. Compare against [deploy/infra-map.html](../../deploy/infra-map.html) and list every place the map is wrong |

Where two lanes touch the same code (credit tables are also database, the throttle is also a
gateway), both read it: the owning lane for behaviour, the other for its own checks. Section 4
removes the duplicates.

### Database checks

- **Unused stored procedures.** List every `CREATE PROCEDURE` under `Databases/`. Search the C# for
  each name (providers call them by string). A procedure with no caller is a finding; the fix drops
  it in the project and in the pre-deployment script, as #573 did.
- **Indexes.** For each table, list its indexes and the queries that touch it. Flag indexes no query
  can use, duplicates and left-prefix overlaps, foreign keys with no supporting index, and hot
  queries that scan. Read the actual predicate, not the index name.
- **Deadlocks.** For every procedure or C# transaction that writes more than one table, write down the
  order it takes locks. Flag any two paths that take the same tables in opposite orders, reads
  followed by writes without `UPDLOCK`, and transactions held across a network call.
- **Production numbers.** When the export from wbskt/Wbskt#619 exists, read index usage, missing-index
  hints and deadlock graphs from it; a finding backed by production numbers outranks a code guess.
  Until then, say in the report that index and deadlock findings come from code alone.

## 3. Verify

Each lane checks its own findings before handing them on. A finding needs a file and line, a concrete
scenario (input or state, then what goes wrong), and a second read that confirms it. Drop anything
that doesn't survive. Search open issues in both repos first and mark findings already filed.

## 4. Merge, rank and report

One final step merges the lanes:

1. Remove duplicates across lanes.
2. Rank: **P1** breaks something users see or loses data; **P2** should land before going public;
   **P3** is cleanup or post-launch.
3. Write `Docs/Reviews/YYYY-MM-DD.md` in the format below, and copy it to
   `arch-reviews/` in the project files.

## 5. Act

- Open one PR with the report (`docs(reviews): architecture review YYYY-MM-DD`), following
  [CONTRIBUTING.md](../../CONTRIBUTING.md).
- File an epic **"Architecture review (D Mon): follow-ups"** in wbskt/Wbskt with one sub-issue per new
  finding, filed without waiting. Console findings go to wbskt/Wbskt.Console and are attached as
  sub-issues too. Milestone: P1 to the current milestone, P2 to M3 Go public, P3 to Post-launch.
- Small, safe fixes (dead code, a missing index, a doc mismatch) can go in their own PRs. Anything
  larger waits for the owner.
- Update the roadmap's Reviews data
  ([Docs/Roadmap/wbskt-backend-roadmap.html](../Roadmap/wbskt-backend-roadmap.html)) and, when the
  infra lane found errors, the infra map, then republish both pages.

## Report format

```markdown
# Architecture review, YYYY-MM-DD

Range: <D> to <today>. Commits reviewed: Wbskt N, Console N, Python N, .NET N, Arduino N, site N.
Production numbers: <used | not available (code only)>.

## Summary
<three to five sentences: the most important findings and the overall trend>

## Findings
| # | P | Lane | Finding | Where | Issue |
|---|---|---|---|---|---|

### 1. <title>
**Where:** path:line · **Scenario:** … · **Fix:** …

## Infra map corrections
## Pricing mismatches
<every place PlanCatalog, Billing.md, the site and the console disagree, or "none">
## Unowned paths
<changed paths no lane named, and the lane that took them, or "none">
## Already filed (not re-filed)
```
