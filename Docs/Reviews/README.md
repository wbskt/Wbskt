# Architecture reviews

A full review of every Wbskt component and the infrastructure runs **every 5th day**, but only when
something was committed since the last one. It is an architecture review, a product review and a
senior developer's code review at once (section 3). This file is the brief the review follows.
Change the lanes or checks here, not in the scheduled task that starts it.

Reports live beside this file as `YYYY-MM-DD.md`, one per review. The newest report's date is the
"last review" date. Older reviews, written before this routine, are in the project files
(`full-arch-review/`, `workflow-review/`, `db-review/` and others).

## 1. Decide whether to run

1. Take the date `D` of the newest `Docs/Reviews/YYYY-MM-DD.md` (none yet: 2026-11-02, the last
   full review, wbskt/Wbskt#537).
2. List commits on `master` since `D` in wbskt/Wbskt, wbskt/Wbskt.Console, wbskt/Wbskt.Python,
   wbskt/wbskt.dotnet, wbskt/Wbskt.Arduino, wbskt/wbskt.admin and richardjoy530/fluffy-waddle.
3. **No commits anywhere: stop.** Write nothing and file nothing. A run the owner asks for by hand
   skips this check. Otherwise carry on, and note which areas changed.
4. Map every changed path to the lane that owns it (section 2). A path no lane names goes to the
   nearest lane for this review, and the report lists it under "Unowned paths" so this table can be
   fixed in the report's PR.

Every lane reviews **its whole area** each time, not only what changed: the system is split so each
lane is small enough to read end to end. Changed paths are read first and most closely, because new
code is where new mistakes are.

## 2. Lanes

The system is split into components and into logical sets that cut across them (a sign-up, a
device's life, a run, a payment). Each lane is one of those and runs in parallel with the others.
A lane owns the code, SQL, tests (`Tests/`), docs (`Docs/`) and console screens of its area, and says
where the tests and docs no longer match the code. Every lane reviews its area from all three angles
in section 3.

| Lane | Area | Also checks |
|---|---|---|
| Identity | the auth host's sign-up, sign-in, sessions and refresh tokens, email verification, password reset and change, lockout, account profile (`RegistrationService`, `SignInService`, `SessionTokens`, `SecurityTokens`, `PasswordService`, `AccountLockout`, `AccountProfileService`, `SignUpChallenges`, `ReservedUsernames`), their controllers and tables; the console's `features/auth` and its interceptor and guards | enumeration, lockout, token reuse and rotation, a session ended on one device, what a user sees at each failure, mail that verifies or resets sent once |
| Tenants, users and access | the auth host's tenant, workspace, invitation, member and account-deletion code (`AccessControlService`, `TenantService`, `TenantLimits`, `TenantUsageService`, `WorkspaceService`, `AccountDeletionService`, `RoleGrantGuard`) and their controllers; the role, permission, member and invitation tables; `WorkspacePermissionFilter` and `ScopedPermissionFilter` in `Wbskt.Infrastructure`; [AccessControl.md](../AccessControl.md); the console's dashboard, members page and workspace switcher | one tenant never reaching another's data, and the 403/404 rules in [Coding.Conventions.md](../Coding.Conventions.md); every workspace-scoped action carrying `[RequiresPermission]`; granting a role above your own, removing the last owner, an invitation redeemed twice, late or by the wrong account; plan limits on members and workspaces enforced on every path that adds one; sessions and cached access dropped when a role, membership or the account changes or is deleted; account deletion reaching billing, mail and the workspace database |
| Billing and pricing model | the auth host's `Services/Billing`, `TenantPlanService` and its billing, webhook and plan controllers; `Wbskt.Infrastructure/Billing` (`PlanCatalog`); `Models/Wbskt.Models/Billing`, `Events/Wbskt.Events/Billing`; the billing, checkout, subscription and credit-purchase tables in `Wbskt.Database.Auth`; [Billing.md](../Billing.md), [Billing.Policies.md](../Billing.Policies.md); the site's plans (`app/src/app/layout/plans`) and terms in richardjoy530/fluffy-waddle; the console's billing and plan screens | webhook signatures, replays, duplicates and out-of-order events; a payment that succeeds at the provider but fails here, and the reverse; upgrade with proration, downgrade at period end, cancel, refund and a second checkout; billing notices and emails sent once; **one set of prices and limits**: `PlanCatalog.BuiltIn`, its override layers, Billing.md, the site's plan table and anything the console shows must agree, and every mismatch is a finding; edges of the model (a tenant exactly at a limit, a downgrade while over the lower plan's limits, a pack bought the day it expires) and whether each plan still clears its margin against `capacity/` in the project files |
| Credits and usage metering | `CreditUsageProvider`, `RunCountersProvider`, `DefaultCreditCostCalculator` and the credit paths in `RunStarter`, `RetryExecutor`, `RunFinalizer` and `TestRunStarter` (`Wbskt.Workflow`); the engine's `CreditUsagePoster`; the management host's `Services/Billing` and its usage, credit-cap and message-stats controllers; `MessageThrottle` in `Wbskt.Gateway.Core`; the `*Credit*` and `*Usage*` tables and their procedures; the console's usage and cap screens | every billable action is counted exactly once (retries, test runs, recovery after a crash, two replicas, a reconnect); credits drawn in the documented order (plan first, then packs, oldest expiry first) and caps held exactly under concurrent runs; month boundaries in UTC from `TimeProvider`; what metering does while the auth host or the broker is down and whether the hourly reconcile repairs it; the 80% and 100% announcements and the 2x throttle firing once and clearing; what a Business tenant's overage report counts against what [Billing.Policies.md](../Billing.Policies.md) says it bills |
| Devices | the management host's clients, registration policies and PINs, client auth and secrets, commands and the queued-command sweeper, readings and state variables, presence and the stale-presence sweeper (`Services/Client*`, `Services/Clients`, `Services/Readings`, `RegistrationPolicyService`, `RegistrationPins`, `ClientAccessRevoker`, `StalePresenceSweeper`, `QueuedCommandExpirySweeper`) and their controllers; the console's clients and policies explorers and pages | a device's whole life from PIN to retire: enrol, connect, rename, revoke, rotate, delete; what the console shows at each step and whether it is true; presence and latency kept right through reconnects and outages; one fetch per thing the page shows |
| Platform services | the management host's integrations, message templates, event log, audit and its retention, the outbound mail queue (`Services/Email`), the SignalR hub and every event it forwards, workspace retirement and the deleted-workspace sweeper; the console's integrations, audit, overview and status bar | secrets in integrations never returned or logged; mail kept until the relay takes it and never sent twice; every SignalR event has a console listener and every listener an event; background jobs that run on every replica; retention that actually deletes |
| Shared infrastructure | `Wbskt.Infrastructure`, `Wbskt.Primitives`, `Models/*`, `Events/*` (the event bus and RabbitMQ), `Clients/Wbskt.Api.Client`, each host's `Program.cs` | one host shape, `Result` vs fault handling, caching and invalidation, rate limits, paging and time types; helpers each host wrote for itself that belong here; events nobody handles and handlers for events nobody publishes; contracts declared in a host instead of a `*.Models` package |
| Gateways | `Wbskt.Gateway.Core`, the WebSocket and MQTT gateway hosts, `Clients/Wbskt.Protocol`, `Clients/Wbskt.Simulator`, `Tests/Conformance`, `Tests/Wbskt.LoadTester`, the protocol docs in `Docs/` | protocol and close codes match the docs, the conformance suite covers what the protocol docs promise, the two gateways behaving alike, reconnect and offline buffer behaviour on the server side, token handling |
| Workflow runtime | `Wbskt.Workflow` (`Engine`, `Runtime`, `Providers`, `Channels`), `Wbskt.Workflow.Abstraction`, the engine host, its procedures, `Docs/Workflow.Engine` | run lifecycle, leases and leader election, retries and idempotency, waits and long-running runs, recovery after a crash, two replicas |
| Workflow nodes and designer | `Wbskt.Workflow/NodeExecutors`, `Wbskt.Workflow/Integrations`, `Wbskt.Workflow.Builder`, `Tools/Wbskt.Workflow.Exporter`, the management host's workflow controllers and services (drafts, versions, test runs, runs); the console's workflow designer, runs list and run view | every node the designer offers runs, and every node the engine runs can be built; node settings named and validated alike on both sides; what a user sees when a node fails; draft, publish and test-run flows |
| Main database | `Databases/Wbskt.Database` | see "Database checks" below |
| Auth database and migrations | `Databases/Wbskt.Database.Auth`, `Databases/Migrations`, `Deploy-Databases.ps1` | see "Database checks" below, and that every migration can run twice and against a database that is part way through |
| Console core | wbskt/Wbskt.Console outside the feature screens other lanes take: `core/`, `shared/ui`, the workspace shell, explorer, editor and tab host, the API services, styles and tokens | feature-service structure, calls to endpoints that changed or no longer exist, unversioned API calls, dead components, style budgets, shared components copied instead of reused, [CODING_GUIDELINES.md](https://github.com/wbskt/Wbskt.Console/blob/master/CODING_GUIDELINES.md) |
| Python library | wbskt/Wbskt.Python | reconnect and back-off, `Retry-After`, offline buffer, credential storage, close codes, thread and `asyncio` safety, cancellation, the current conformance suite, README samples that run, release workflow and trusted publishing |
| .NET library | wbskt/wbskt.dotnet | the same as Python, plus async handlers, disposal, supported target frameworks and its own copy of the protocol types against `Clients/Wbskt.Protocol` |
| Arduino library | wbskt/Wbskt.Arduino | the same as Python where it applies, plus memory on small boards, OTA, supported boards and the three version files |
| Website | richardjoy530/fluffy-waddle, including its `docs` and `get-started` pages | every claim (features, limits, prices, supported devices) matches what ships; examples run against today's API and libraries; a newcomer can get from the home page to a connected device without a dead end |
| Admin | wbskt/wbskt.admin and the internal admin endpoints and `admin` views it uses in wbskt/Wbskt | it never writes to another host's database; every change carries a staff member, a reason and an idempotency key; staff sessions; what support staff can and cannot do for a stuck user |
| Infra and CI | `deploy/`, `.github/workflows/`, `Config/`, and the CI and release workflows of the other repos | compose services, networks, ports, env vars and secrets, deploy order and health waits, CI time and skipped checks, telemetry and alerts. Compare against [deploy/infra-map.html](../../deploy/infra-map.html) and list every place the map is wrong |
| Journeys | no code of its own: follows the main journeys end to end across every repo — sign up and create a workspace; enrol a first device with each library; send a command and see it arrive; build, test and publish a workflow; hit a plan limit and upgrade; invite a member; delete an account | the seams between lanes: steps that make no sense to a user, a dead end or a missing screen, data converted A to B and back to A across hosts, the same check or lookup done twice on one path, two hosts or two screens that disagree |

Where two lanes touch the same code (credit tables are also database, the throttle is also a
gateway), both read it: the owning lane for behaviour, the other for its own checks. Section 5
removes the duplicates.

### Database checks

- **Unused stored procedures.** List every `CREATE PROCEDURE` under `Databases/`. Search the C# for
  each name (providers call them by string). A procedure with no caller is a finding; the fix drops
  it in the project and in the pre-deployment script, as #573 did. A provider calling it is not
  enough: follow the provider method to a caller outside `Tests/`. One that only tests and test
  fakes reach is unused too, and goes with its interface method (#1000 found nine).
- **Indexes.** For each table, list its indexes and the queries that touch it. Flag indexes no query
  can use, duplicates and left-prefix overlaps, foreign keys with no supporting index, and hot
  queries that scan. Read the actual predicate, not the index name.
- **Deadlocks.** For every procedure or C# transaction that writes more than one table, write down the
  order it takes locks. Flag any two paths that take the same tables in opposite orders, reads
  followed by writes without `UPDLOCK`, and transactions held across a network call.
- **Production numbers.** When the export from wbskt/Wbskt#619 exists, read index usage, missing-index
  hints and deadlock graphs from it; a finding backed by production numbers outranks a code guess.
  Until then, say in the report that index and deadlock findings come from code alone.

## 3. Three angles

Every lane reads its area three times, as three different people. A finding names the angle that
found it.

**Product.** Read it as a user would meet it, through the console, a library, the site or an email.
- Gaps: something a user needs to finish a job that is missing, or reachable only through the API.
- Things that make no sense to a user: names, states, limits or errors that need knowledge of the
  internals; two screens that call one thing by different names; an error that says what failed but
  not what to do.
- Promises the product breaks: what the site, the docs or a screen says against what the code does.
- Dead ends: a failure with no way out but support, a state nothing ever leaves.

**Architecture.** Read it as the one who has to keep it running and change it next year.
- Blunders: a responsibility in the wrong host or layer, a source of truth kept in two places, a
  design that cannot hold at the next plan size, a failure that loses data or charges twice.
- Illogical design: a flow that goes the long way round, a host asking another for what it already
  has, coupling that forces two things to change together, an abstraction with one use.
- Consistency between components: the same concept modelled differently in two hosts, two databases
  or two libraries.

**Senior developer.** Read it line by line, across SQL, C#, TypeScript, Python and C++.
- Redundant work: a value converted A to B and back to A, a round trip that returns what the caller
  sent, the same lookup or query done twice on one path, a fetch the page already has.
- Repeated code that should be one shared pattern, and a pattern that exists but is not used (name
  where it lives, or where it should). A pattern that would help elsewhere is a finding even when
  the code it came from is fine.
- Cleanup: dead code, unused procedures, columns and settings, stale comments and docs, names that
  lie, tests that no longer test what they say.
- Edge cases, error handling, concurrency and lock order, performance, and breaks from
  [Coding.Conventions.md](../Coding.Conventions.md) (console:
  [CODING_GUIDELINES.md](https://github.com/wbskt/Wbskt.Console/blob/master/CODING_GUIDELINES.md)).

## 4. Verify

Each lane checks its own findings before handing them on. A finding needs a file and line, a concrete
scenario (input or state, then what goes wrong), and a second read that confirms it. Drop anything
that doesn't survive. Search open issues in both repos first and mark findings already filed.

## 5. Merge, rank and report

One final step merges the lanes:

1. Remove duplicates across lanes.
2. Rank: **P1** breaks something users see or loses data; **P2** should land before going public;
   **P3** is cleanup or post-launch.
3. Write `Docs/Reviews/YYYY-MM-DD.md` in the format below, and copy it to
   `arch-reviews/` in the project files.

## 6. Act

- Open one PR with the report (`docs(reviews): architecture review YYYY-MM-DD`), following
  [CONTRIBUTING.md](../../CONTRIBUTING.md).
- File an epic **"Architecture review (D Mon): follow-ups"** in wbskt/Wbskt, filed without waiting.
  Each P1 and P2 finding gets its own sub-issue; P3 findings are grouped into one "small items"
  sub-issue per lane. Console, site and production-state findings go to wbskt/Wbskt.Console and are
  attached as sub-issues too; library and admin findings are tracked in wbskt/Wbskt. Milestone: P1 to
  the current milestone, P2 to M3 Go public, P3 to Post-launch.
- Small, safe fixes (dead code, a missing index, a doc mismatch) can go in their own PRs. Anything
  larger waits for the owner.
- Update the roadmap's Reviews data
  ([Docs/Roadmap/wbskt-backend-roadmap.html](../Roadmap/wbskt-backend-roadmap.html)) and, when the
  infra lane found errors, the infra map, then republish both pages.

## Report format

```markdown
# Architecture review, YYYY-MM-DD

Range: <D> to <today>. Commits since the last review: Wbskt N, Console N, Python N, .NET N, Arduino N, admin N, site N.
Production numbers: <used | not available (code only)>.

## Summary
<three to five sentences: the most important findings and the overall trend>

## By angle
<a short paragraph each for product, architecture and senior developer: the themes, and the shared
patterns worth extracting>

## Findings
| # | P | Lane | Angle | Finding | Where | Issue |
|---|---|---|---|---|---|---|

### 1. <title>
**Where:** path:line · **Scenario:** … · **Fix:** …

## Infra map corrections
## Pricing mismatches
<every place PlanCatalog, Billing.md, the site and the console disagree, or "none">
## Unowned paths
<changed paths no lane named, and the lane that took them, or "none">
## Already filed (not re-filed)
```
