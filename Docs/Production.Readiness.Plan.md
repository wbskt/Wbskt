# Production Readiness Plan — Phases 1–3

> ## ⚡ HANDOFF — read this first
>
> **This document is self-sufficient. You do not need any prior conversation.**
>
> It follows a production-readiness review of the whole platform (2026-08-25) that catalogued 19
> findings, `F1`…`F19`. **Phase 0 (F1, F2, F3, F4, F7) is complete and merged** — see
> [Phase 0 — what already landed](#phase-0--what-already-landed) for what changed, because several
> items below build directly on it.
>
> ### How to use this document
>
> Each phase is meant to be executed in **its own session**, so the whole plan never has to fit in
> one context window. To pick up a phase:
>
> 1. Read [Working in this repo](#working-in-this-repo) — shared conventions, commands and traps.
>    It is short and applies to every phase.
> 2. Read [Phase 0 — what already landed](#phase-0--what-already-landed).
> 3. Read **only your phase's section**. Each item is self-contained: why it matters, which files,
>    the steps, the traps, and what "done" means.
> 4. Check [Decisions the owner must make](#decisions-the-owner-must-make) for anything your item
>    depends on. If a decision is unanswered, ask rather than guessing — several of them change
>    the shape of the work.
>
> Items keep the `F<n>` identifiers from the original review so they can be cross-referenced.
> Effort figures are rough and assume familiarity with the area, not with this document.
>
> ### The ordering principle
>
> Phases are sequenced by **what each one unlocks**, not by size:
>
> | Phase | Gate it opens |
> |---|---|
> | **0** ✅ | Before anyone else's data is on the box |
> | **1** | Before a self-serve signup |
> | **2** | Before a second paying customer |
> | **3** | Market fit and revenue |
>
> Nothing in Phase 1 is workflow-engine work. The engine is the most finished part of this system;
> resisting the pull to add the twenty-second node kind, and spending that time on the operational
> floor and the device SDKs instead, is the whole strategic decision behind this ordering.

---

## Working in this repo

### Verification — run these before claiming anything is done

```bash
dotnet build Wbskt.slnx -v q --nologo
dotnet test Tests/Wbskt.Workflow.Engine.Host.Tests/Wbskt.Workflow.Engine.Host.Tests.csproj --nologo -v q
dotnet build Databases/Wbskt.Database/Wbskt.Database.sqlproj -c Debug --nologo
```

Baseline as of the Phase 0 merge: **664 unit tests passing, 39 integration tests passing**, build
clean apart from three pre-existing warnings (`Result.cs` CS0108, `TriggerRegistrationProvider.cs`
CS8601, `RegisterRequest.cs` CS1587).

The DB integration suite needs a live SQL Server, and its mail-delivery tests need an SMTP sink:

```bash
docker run -e "ACCEPT_EULA=1" -e "MSSQL_SA_PASSWORD=Welcome1234" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
docker run -d -p 1025:1025 -p 8025:8025 axllent/mailpit
dotnet tool install -g microsoft.sqlpackage
dotnet build Databases/Wbskt.Database/Wbskt.Database.sqlproj -c Debug --nologo
dotnet build Databases/Wbskt.Database.Auth/Wbskt.Database.Auth.sqlproj -c Debug --nologo
dotnet test Tests/Wbskt.Workflow.Engine.Host.IntegrationTests/Wbskt.Workflow.Engine.Host.IntegrationTests.csproj --nologo -v q
```

Both DACPACs, because the suite deploys the workflow database for the engine's provider tests and the
auth database for the account-recovery procedures — separate databases, as in production.

Without one, every test there **skips** (`[SkippableFact]` + `Skip.IfNot`). A skipped run is not a
passing run — CI fails the job if the suite skips, and you should treat a local skip the same way.

### CI is a real gate now

`.github/workflows/build-images.yml` runs `test`, `integration` and `scan`, and the four `hosts`
image builds plus `migrator` are gated on all three via `needs:`. A red commit publishes no images.
Assume your change must pass all three.

### Conventions that matter

- **References, never IDs.** Public routes address resources by opaque `Guid` (`RefId`). Internal
  integer IDs never appear in a URL or a response body.
- **A reference that does not resolve is a 403, not a 404**, so endpoints cannot be used to
  enumerate. See "The ID Boundary" in `Docs/Coding.Conventions.md`.
- **Stored procedures** are `Table_Action.sql` under `Databases/Wbskt.Database/StoredProcedures/`
  (or `Wbskt.Database.Auth/`), tables under `Tables/`. Providers extend `BaseSqlProvider` and never
  depend on other providers; orchestration lives in services.
- **Providers are Scoped**; engine core is Singleton resolving providers via `IServiceScopeFactory`.
- **Tests**: xUnit + Moq + FluentAssertions. Unit tests in `Tests/Wbskt.Workflow.Engine.Host.Tests`
  (despite the name, it covers the Management, Socket and Auth hosts too — see its `ManagementHost/`,
  `SocketHost/` and `AuthHost/` folders), DB integration in
  `Tests/Wbskt.Workflow.Engine.Host.IntegrationTests`, full-stack E2E in `Tests/Wbskt.E2E.FeatureTests`.
- **A secret or credential never lives in a workflow definition.** A definition is readable by the
  whole workspace and frozen into every published version.
- **Licence pins are deliberate.** Do not upgrade MassTransit past v8 or FluentAssertions past v7 —
  both become non-free. There are comments saying so in `Directory.Packages.props`; respect them.

### Traps that have already cost time

- **Mixed line endings.** Some files are LF, some CRLF. Bulk `sed`/`perl` patterns anchored on `;\n`
  silently match only half the files — always allow `\r?\n`.
- **Adding a method to `IRunProvider`/`IHistoryEventProvider`** means patching ~15 test doubles.
  Expect it; batch it.
- **`AddWorkflowEngine` is not independently usable.** It registers all 21 node executors but not
  three of their dependencies: `IDeviceCommandPublisher`, `IToastPublisher` (outbound RabbitMQ
  adapters) and `IHttpClientFactory` (from `AddHttpClient`). The engine host supplies all three in
  `Program.cs`. The gap only surfaces when something resolves the executor registry, so it is easy
  to hit one dependency at a time. For tests, use
  `Tests/…IntegrationTests/Infrastructure/EngineTestHost.BuildServices`, which assembles the whole
  thing. **If you are changing DI here, consider whether the split is still right** — it is a
  defensible boundary, not an obviously correct one.
- **A DACPAC column drop needs `MIGRATE_ALLOW_DATA_LOSS=true`** on that deploy. `migrate.sh` defaults
  to `BlockOnPossibleDataLoss=True` and that default is load-bearing.
- **`HASHBYTES` over `NVARCHAR` hashes UTF-16LE**, while C# `Encoding.UTF8.GetBytes` hashes UTF-8.
  They agree only if the SQL side does `CONVERT(VARCHAR(n), …)` and the content is ASCII. Getting
  this wrong fails every device at once. `ClientSecretHashingIntegrationTests` pins it.

---

## Phase 0 — what already landed

Merged to `master`. Relevant because later items build on it.

| ID | What changed |
|---|---|
| **F1** | CI runs unit + integration + vulnerable-package jobs; image publishing gated behind them. Fixed two Linux-only bugs in `DatabaseDeployer` (DACPAC path missing the TFM folder; `FindSqlPackage` probing with Windows-only `where`) that had made the integration suite unrunnable — it now executes 39 tests instead of silently skipping. |
| **F2** | `/healthz` is liveness (probes nothing, so a dependency failure cannot cause a restart loop); `/healthz/ready` runs real probes. Auth probes SQL + bus, Management SQL + bus, Socket bus only (it has no database), Engine SQL + bus + leadership. Compose healthchecks point at readiness for auth/management/socket; the engine deliberately stays on liveness so standby replicas do not hang `deploy.sh`. |
| **F3** | Device secrets are SHA-256 in `Clients.SecretHash`, compared in C# with `FixedTimeEquals`. `Client_Verify` replaced by `Client_GetCredentialBy_RefId`. |
| **F4** | `deploy/scripts/backup.sh` (backup → verify → mandatory off-box upload) and `restore.sh` (defaults to a scratch DB; `--force` for real recovery). |
| **F7** | Default-deny `FallbackPolicy` on Auth and Socket. `/ws` opts out explicitly because it authenticates in middleware before `UseAuthentication`. `EndpointAuthorizationTests` enforces that every controller action states its posture. |

**Two Phase 0 items are still open and belong to the owner, not to a coding session:**

1. **The restore rehearsal has not been done.** `backup.sh`/`restore.sh` are syntax-checked but have
   never run against a live instance. Until a restore has been performed and row counts compared,
   what exists is a backup script, not a backup.
2. **The F3 deploy needs `MIGRATE_ALLOW_DATA_LOSS=true`.** Documented under *Database migrations* in
   `deploy/README.md`.

---

## Phase 1 — Before a self-serve signup

**Goal:** a stranger can sign up, recover their account, connect their own integrations, and leave an
audit trail — without you doing anything by hand.

Six items. `P1-1` gates `P1-2`; `P1-3` gates `P1-4`. The rest are independent.

---

### P1-1 · Mail transport (F13) — *do this first*

**Why.** The Auth host cannot send mail at all. Invitations mint a token and hand it back in the API
response for an administrator to deliver by hand (`Docs/API.Endpoints.md` says so outright). Email
exists in the codebase only inside the workflow engine, as `action:email`. This small piece of
plumbing blocks password reset, email verification, and security notifications.

**Where.**
- `Wbskt.Workflow.Abstraction/Runtime/IEmailSender.cs` — the existing abstraction.
- `Wbskt.Workflow/Runtime/SmtpEmailSender.cs` — the existing SMTP implementation.
- `Wbskt.Workflow.Abstraction/Configuration/NotificationOptions.cs` — `EmailOptions`
  (Host, Port, UseStartTls, UserName, Password, FromAddress, FromDisplayName, `IsConfigured`).
- `Wbskt.Infrastructure/` — where the shared version should live.

**Steps.**
1. Lift `IEmailSender` + `EmailOptions` into `Wbskt.Infrastructure` so the Auth host can use them
   without referencing the workflow library. Leave the engine consuming the same abstraction.
2. Add a templated sender on top: a small interface taking a template name plus a model, so call
   sites do not build HTML inline. Three templates to start: invitation, password reset, email
   verification.
3. Register it in the Auth host and bind from configuration.
4. Wire invitations to actually send. Keep returning the raw token in the API response for now —
   the console may rely on it, and removing it is a separate decision.

**Traps.**
- `EmailOptions.IsConfigured` exists precisely so a host with no relay configured fails with a clear
  message rather than deep inside the SMTP stack. Preserve that behaviour on the new path.
- Do not put the SMTP password anywhere near a workflow definition or an API response.
- Sending mail inline on the request path makes registration latency hostage to the relay. Prefer
  publishing an event and sending from a consumer, or at minimum do not await the send inside the
  transaction.

**Done when.** An invitation issued through `POST api/tenants/{ref}/invitations` arrives as an email
in a local catcher (MailHog/Papercut), and a host with no relay configured reports that clearly
instead of throwing.

**Effort.** ~1 day.

---

### P1-2 · Password reset and email verification (F12)

**Why.** Neither exists. A user who forgets their password has no route back to their account short
of a manual database edit. Anyone can register with any address without proving they control it —
including addresses belonging to other people. Both are table stakes for accepting a single paying
signup.

**Depends on P1-1.**

**Where.**
- `Hosts/Wbskt.Auth.Host/Controllers/AuthController.cs` — new anonymous endpoints.
- `Hosts/Wbskt.Auth.Host/Services/AuthService.cs`, `IAuthService.cs`.
- `Hosts/Wbskt.Auth.Host/Providers/SqlAuthProvider.cs`, `IAuthProvider.cs`.
- `Hosts/Wbskt.Auth.Host/Services/InvitationTokens.cs` — **copy this pattern exactly.**
- `Databases/Wbskt.Database.Auth/Tables/` — new tables. `TenantInvitations.sql` is the model to
  follow (`TokenHash VARBINARY(32)`, expiry, single-use).

**Steps.**
1. Two new tables — `PasswordResetTokens` and `EmailVerificationTokens` — each storing only the
   SHA-256 hash of the token, an expiry, a consumed-at, and a user reference. Mirror
   `TenantInvitations`.
2. New endpoints on `AuthController`, all `[AllowAnonymous]` and all carrying
   `[EnableRateLimiting(RateLimitPolicies.Authentication)]`:
   - `POST forgot-password` — always returns 204 regardless of whether the address exists.
   - `POST reset-password` — token + new password; revokes every refresh token for that user.
   - `POST verify-email` — token; marks the account verified.
   - `POST resend-verification` — authenticated, rate limited.
3. Set `Users.IsEmailVerified` (new column, default 0; existing rows backfill to 1 so current
   accounts are not locked out). Decide what unverified accounts may do — see **D3**.
4. Send verification on registration via P1-1.

**Traps.**
- **`forgot-password` must not reveal whether an address is registered.** Same response, same
  status, and ideally similar latency, whether or not the user exists.
- **Reset must revoke refresh tokens.** Otherwise an attacker who already has a session keeps it
  after the victim recovers the account.
- Tokens must be single-use and short-lived (15–60 minutes). `InvitationTokens.Generate` is the
  right generator; do not invent a second one.
- Registration currently creates a tenant, roles, membership and a default workspace in one
  transaction. Do not break that atomicity when adding verification.

**Done when.** A full round trip works end to end: register → receive verification mail → verify →
forget password → receive reset mail → reset → old refresh tokens rejected → new password works.
Cover it in `Tests/Wbskt.E2E.FeatureTests/Scenarios/Auth/`.

**Effort.** ~2–3 days.

---

### P1-3 · Per-workspace integration credentials (F14)

**Why.** This is the largest piece of architecture still missing, and it gates P1-4 and most of
Phase 3. SMTP settings and the Telegram bot token are **host-level configuration**, bound once from
`WorkflowEngine:Email` and `WorkflowEngine:Telegram`. Every workspace on the platform therefore
shares one relay and one bot. A tenant's alert arrives from *your* bot; their mail sends from *your*
envelope address; and there is no way for a customer to connect an account of their own.

The reasoning that put credentials in host config is sound and well documented — a workflow
definition is workspace-readable and frozen into every version, so a secret must not live in one.
But the intended destination, a per-workspace credential store, was never built. `Docs/RoughFeatures.md`
lists *Integrations* as one of five core workspace entities; there is no table, no service and no
endpoint for it.

**Where.**
- `Databases/Wbskt.Database/Tables/` — new `Integrations` table.
- `Hosts/Wbskt.Management.Host/` — provider, service, controller (CRUD, workspace-scoped).
- `Wbskt.Primitives/Constants/Permissions.cs` — new slugs, e.g. `integrations.read` /
  `integrations.manage`. Note the existing read/manage split convention.
- `Wbskt.Workflow.Abstraction/Configuration/NotificationOptions.cs` — the current
  `EmailOptions`/`TelegramOptions` become the *fallback*, not the only source.
- `Wbskt.Workflow/NodeExecutors/Actions/` — executors resolve a credential by reference.

**Steps.**
1. `Integrations` table: `Id`, `RefId`, `WorkspaceId`, `Kind` (`smtp`, `telegram`, `http`…),
   `Name`, `SecretCiphertext VARBINARY(MAX)`, `KeyId`, `CreatedAt`, `CreatedBy`. Never a plaintext
   column — Phase 0's F3 exists because of exactly that mistake.
2. Envelope encryption. A data key per row, encrypted under a master key from configuration or a
   KMS. `KeyId` records which master key so rotation is possible without re-encrypting everything
   at once. See **D1** for where the master key lives.
3. CRUD endpoints under the workspace, gated on the new permission slugs. **The plaintext secret is
   returned never** — write-only in, reference out. Follow the `ClientRegistrationResponse` pattern:
   shown once at creation if at all.
4. A resolver in the engine: given a credential reference and a workspace, decrypt at execution time.
   Cache briefly; never log the plaintext; never put it in node output or the history stream.
5. Node configs gain an optional credential reference. When absent, fall back to host config so
   nothing breaks on upgrade.

**Traps.**
- The workflow history stream and node outputs are workspace-readable. A decrypted secret must never
  reach either. `WorkflowEngineClient` already strips the webhook secret from the trigger payload for
  this reason — read that code before designing the resolver.
- A credential belongs to a workspace; a run belongs to a workflow definition which belongs to a
  workspace. Verify the chain on every resolve, or one tenant's workflow can use another's
  credential.
- `Runs` has no `WorkspaceId` column; scoping goes through `WorkflowDefinitions`. Plan the join.

**Done when.** Two workspaces can each configure their own Telegram bot, and a workflow in one
cannot resolve the other's credential — proven by a test. Host config still works for a workspace
that has configured nothing.

**Effort.** ~4–5 days. The largest item in Phase 1.

---

### P1-4 · Webhook action: headers, auth, timeout, response (F15)

**Why.** `WebhookNotificationConfig` carries a URL, a method and a body. No headers, no
authentication, no timeout, no response mapping beyond a raw string. Essentially every real API needs
a bearer token or an API-key header, so the one node that connects Wbskt to the rest of the world can
only reach endpoints that need no credentials.

**Benefits from P1-3** for the credential half; headers and timeout can land independently.

**Where.**
- `Wbskt.Workflow.Abstraction/Models/Nodes/Actions/WebhookNotificationConfig.cs`
- `Wbskt.Workflow/NodeExecutors/Actions/WebhookNodeExecutor.cs`
- `Wbskt.Workflow/Runtime/OutboundAddressGuard.cs` — careful existing SSRF work; do not weaken it.

**Steps.**
1. Add `Headers` (a string map), an optional credential reference, a per-node timeout, and JSON
   response extraction into node output.
2. Resolve the credential through P1-3 and inject it as a header at send time — never store it in
   the config.
3. Keep the `workflow-webhook` named client's `AllowAutoRedirect = false`. It exists so a 3xx to an
   internal address cannot sidestep the address guard.

**Traps.**
- Headers are author-supplied. Reject hop-by-hop headers and anything that would let an author
  rewrite `Host` or smuggle a second request.
- The address guard resolves DNS once and there is a rebinding window it cannot close — documented
  in its own comments. Do not widen that window by adding a redirect follow.
- Response bodies can be large. Cap what goes into node output, or one webhook can bloat the history
  stream.

**Done when.** A workflow can call an endpoint requiring `Authorization: Bearer …` using a
workspace credential, the secret appears in neither the definition nor the history, and the SSRF
tests still pass.

**Effort.** ~1–2 days.

---

### P1-5 · Make the audit trail attributable and readable (F16)

**Why — and read this carefully, because the original review got it wrong.**

The review claimed auth events were "published and then discarded". **That is incorrect.**
`Hosts/Wbskt.Management.Host/Handlers/Events/EventLoggerHandler.cs` is an `IConsumer<IEvent>` — it
consumes **every** event on the bus, auth events included (they implement `IEvent` via `BaseEvent`),
and persists each one to `EventLogs` with the full serialized message as `EventData`.

The real problem is narrower and cheaper to fix:

1. The handler extracts `IWorkspaceContext`, `IPolicyContext`, `IClientContext` and
   `IWorkflowContext` — but **never `IUserContext`**, even though it exists
   (`Events/Wbskt.Events/Abstractions/IUserContext.cs`) and auth events implement it.
2. `EventLogs` has **no `UserId`, `UserRefId` or `TenantId` column**, so there is nowhere to put it.
3. The only read path, `EventLog_GetBy_Workspace`, filters `WHERE el.WorkspaceId = @WorkspaceId`.
   Auth events have no workspace, so their rows are written with `WorkspaceId = NULL` and **can
   never be returned by any endpoint**.

So the data is being captured and is unreachable. This is a plumbing job, not a new subsystem.

**Where.**
- `Databases/Wbskt.Database/Tables/EventLogs.sql`
- `Hosts/Wbskt.Management.Host/Handlers/Events/EventLoggerHandler.cs`
- `Hosts/Wbskt.Management.Host/Services/EventLogBuffer.cs` and the `EventLogEntry` record
- `Databases/Wbskt.Database/StoredProcedures/EventLogs_InsertBatch.sql`,
  `EventLog_GetBy_Workspace.sql`
- `Hosts/Wbskt.Management.Host/Controllers/EventLogsController.cs`

**Steps.**
1. Add `UserId INT NULL`, `UserRefId UNIQUEIDENTIFIER NULL`, `TenantId INT NULL` to `EventLogs`,
   with an index supporting a tenant-scoped read.
2. Extend `EventLogEntry`, the buffer, and `EventLogs_InsertBatch` to carry them.
3. In the handler, read `IUserContext` exactly as the other four contexts are read, with the same
   JSON-property fallback.
4. Add a tenant-scoped read: `EventLog_GetBy_Tenant`, an endpoint under `api/tenants/{tenantRef}/…`,
   gated on `logs.read` (the slug already exists in `Permissions.cs`).
5. Decide retention — see **D4**. `HistoryRetentionGc` is the existing model for a retention job.

**Traps.**
- Auth events carry no tenant today. `UserId` alone does not scope a read, because a user can belong
  to several tenants. You will likely need to resolve tenant at write time, or add tenant to the
  events themselves — decide deliberately and write down which.
- `EventData` is the full message JSON. Confirm no auth event carries anything sensitive before
  exposing this to tenant administrators — check `SecurityAlertEvent` in particular, whose `Details`
  field is free text.
- The handler swallows its own exceptions by design (a logging failure must not break the bus).
  Keep that, but do not let a new required column turn every insert into a silent no-op.

**Done when.** A tenant administrator can read their own tenant's login successes, failures, role
changes and security alerts through the API, attributed to a user, and cannot see another tenant's.

**Effort.** ~2 days.

---

### P1-6 · Rate limits and quotas on the authenticated API (F10)

**Why.** Throttling covers exactly two surfaces: the anonymous credential endpoints
(`RateLimitPolicies.Authentication`) and the public workflow callback (`PublicCallbackPolicy`).
Every authenticated endpoint is unlimited. A logged-in tenant — or a leaked token — can hammer the
management API, create unbounded workflows and start unbounded runs. Per-run credit budgets cap a
single runaway loop; nothing caps a thousand of them.

**Where.**
- `Wbskt.Infrastructure/RateLimitPolicies.cs` — currently one constant, with a good explanatory
  comment. Follow its style.
- `Hosts/Wbskt.Management.Host/Program.cs` — `AddRateLimiter`.
- `Hosts/Wbskt.Auth.Host/Program.cs` — same.

**Steps.**
1. A per-tenant partitioned limiter for the authenticated API, partitioning on the tenant claim
   rather than the IP (many users behind one NAT must not throttle each other).
2. Ceilings per workspace on concurrent runs, total workflows, and registered clients. Enforce in
   the service layer where the resource is created, not only at the HTTP edge.
3. Return `429` with `Retry-After`.

**Traps.**
- These ceilings become plan tiers in Phase 3 (`P3-2`). Put the numbers in configuration, not
  constants, so they can vary per tenant later without a redeploy.
- Do not throttle the device data plane the same way as the console API. A fleet of 5,000 devices
  reconnecting after a socket-host restart is legitimate traffic.
- `UseRateLimiter` must sit after `UseForwardedHeaders` so the partition key is the real client, not
  Traefik. The Auth host already gets this right — copy the ordering.

**Done when.** A tenant exceeding the limit gets 429s while another tenant is unaffected, proven by
a test, and a workspace cannot exceed its configured workflow ceiling.

**Effort.** ~2 days.

---

## Phase 2 — Before a second paying customer

**Goal:** you can see what production is doing, and one machine failing is not the end of the
service.

---

### P2-1 · Observability: tracing, metrics, alerting (F5)

**Why.** OpenTelemetry is wired into the **Auth host only**, metrics only, behind a Prometheus
exporter that the code's own comment concedes nothing scrapes. The other three hosts emit no
telemetry. There is no Prometheus or Grafana in the compose file, no alerting, and no distributed
tracing across four hosts and a message bus — which is precisely the topology where a request
disappears and you need to find out where. Serilog writes to console only, with no aggregation and
no rotation policy.

**Where.**
- `Hosts/Wbskt.Auth.Host/Program.cs` — the existing (partial) OTel setup, the model to generalise.
- `Hosts/*/Program.cs` — the other three.
- `Wbskt.Workflow/Telemetry/WorkflowMetrics.cs` — existing engine metrics.
- `Hosts/Wbskt.Auth.Host/Telemetry/AuthMetrics.cs` — existing auth metrics.
- `deploy/compose/docker-compose.yml`, `deploy/config/serilog.json`.

**Steps.**
1. OTel tracing in all four hosts, with context propagated through MassTransit so a trace spans
   inbound HTTP → bus → engine → outbound.
2. An OTLP collector in the compose stack, plus somewhere to put traces and metrics.
3. Log aggregation. Console-only logging on a single VM means logs vanish with the container.
4. Alerts on the handful of things that mean customer pain: run failure rate, bookmark backlog
   depth, dead-letter count, socket disconnect rate, and **backup job failure** (Phase 0 shipped
   `BACKUP_ALERT_CMD` expecting exactly this).

**Traps.**
- `node_id` is deliberately **not** a metric tag — unbounded cardinality. Per-node timings come from
  the history stream instead. Do not undo that decision.
- The Prometheus endpoint is `RequireAuthorization()` on the Auth host because it is publicly
  routed. Keep any new scrape endpoint off the public routers or authenticated.
- A standby engine legitimately reports not-ready. Alerting on readiness will page you for normal
  behaviour unless you account for it.

**Effort.** ~3–4 days.

---

### P2-2 · Get the E2E suite into CI (F1 continued)

**Why.** `Tests/Wbskt.E2E.FeatureTests` contains **321 assertions** across auth, triggers, branching,
timeouts, error handling and state management — and it has never run in CI, because it needs four
live hosts plus RabbitMQ and SQL. Phase 0 proved what happens to a suite nothing runs: seven of the
39 DB integration tests had rotted, and five of those could never have passed.

Assume the same is true here. **Budget for finding failures, and treat each one as a real
question — stale test or real bug — rather than something to make green.**

**Where.**
- `Tests/Wbskt.E2E.FeatureTests/README.md`, `E2EConfig.cs`, `Fixtures/ServicesFixture.cs`
- `.github/workflows/build-images.yml`
- `deploy/compose/docker-compose.yml`

**Steps.**
1. Bring the stack up in CI. The compose file already exists; the images already build in the same
   workflow. Either compose the freshly built images or use Testcontainers.
2. Point `E2EConfig` at the composed endpoints.
3. Add the job, gated like the others, and **assert the suite actually ran** — the same guard the
   integration job uses. A fully-skipped E2E run must fail.

**Traps.**
- The fixture previously failed setup with **HTTP 429 from the auth host** — the credential rate
  limiter throttling the test's own account creation. Either raise the limit in the CI profile or
  space the fixture's calls. This is a known landmine.
- `NestedLoopE2ETests` was noted in the gap report as building a definition the current PFE↔Join
  rule rejects, with a command count derived from old fan-out arithmetic. Expect it to fail.
- `ForEachFanOutE2ETests` was rewritten for sequential `ForEach` and never executed.

**Effort.** ~3–4 days, most of it fixing what surfaces.

---

### P2-3 · Split the JWT keys (F8)

**Why.** Every host signs and validates with the same HS256 secret from `Jwt:Key`, and every host
disables issuer and audience validation (`ValidateIssuer = false, ValidateAudience = false`). Three
consequences: any compromised host can mint tokens accepted by all the others; a device token and a
console token are structurally interchangeable at the signature layer; and rotating the key requires
restarting the whole platform at once.

There is an existing E2E test (`AUTH_TK_06`/`07`) documenting that a client token minted by the
Management host carries a signature the Auth host accepts, and is refused only because a client's
subject is a Guid while the identity middleware parses an int — an accident of ID types rather than
a control. Read it before starting.

**Where.** `Hosts/*/Program.cs` (`AddJwtBearer`), `Wbskt.Infrastructure/Security/JwtService.cs`,
`Hosts/Wbskt.Socket.Host/Middleware/WebSocketAuthMiddleware.cs`.

**Steps.** Asymmetric signing — Auth holds the private key, the others verify against a published
JWKS. If that is too large a step, distinct audiences per host with validation **on** is most of the
benefit for an afternoon.

**Traps.** `WebSocketAuthMiddleware` calls `IJwtService.ValidateToken` directly, outside the ASP.NET
authentication pipeline. It must be updated in lockstep or every device disconnects.

**Effort.** ~2–3 days asymmetric, ~0.5 day for audiences only.

---

### P2-4 · Short-lived access tokens with revocation (F9)

**Why.** Access tokens last 60 minutes and cannot be revoked. `Docs/API.Endpoints.md` states it:
"a deactivated account keeps working until its current access token expires." Disabling a compromised
administrator leaves them fully operational for up to an hour.

The shape is already solved for devices: `Hosts/Wbskt.Socket.Host/Infrastructure/RevocationCache.cs`
exists precisely because a revoked client's JWT stays valid for an hour. Humans need the same.

**Steps.** Shorten access tokens to 5–15 minutes (rotating refresh already works well, including
replay detection that revokes the whole family). Push a revocation list to the hosts through Redis,
which is already in the stack.

**Traps.** Shorter tokens mean more refresh traffic — make sure P1-6's limiter does not throttle
legitimate refreshes. The console must handle a mid-session refresh transparently.

**Effort.** ~2 days.

---

### P2-5 · Remove the single points of failure (F6)

**Why.** The engine is active/standby by design, pinned at `ENGINE_REPLICAS=1`, so all workflow
throughput for all tenants runs in one process. SQL, RabbitMQ and Redis are single containers on the
same host as the workloads they serve. This is a reasonable staging posture and a documented one; it
is not a production posture.

**Steps.** Managed SQL (which also retires most of Phase 0's F4 burden) and managed RabbitMQ, then
the engine work that lets replicas exceed one. `deploy/README.md` §Scaling flags the prerequisite.

**Traps.** Raising `ENGINE_REPLICAS` above 1 changes the leadership picture that Phase 0's
`LeadershipHealthCheck` and the compose healthcheck decision were built around — re-read the comment
on the engine's `healthcheck:` block before changing it.

**Effort.** Days to weeks depending on **D2**.

---

## Phase 3 — Market fit and revenue

**Goal:** the market you are aiming at can actually adopt the product, and you can charge them.

**Strategic context, because it decides the ordering here.** The review's recommendation was to
target **connected-device automation** — fleet operators of roughly 100–10,000 devices needing
*stateful* reactions to device events — and specifically the systems integrators and OEMs who run
devices for many end-customers and need each to see only their own. That is what the tenant →
workspace → group → scoped-role hierarchy is for, and it is the thing competitors cannot match:
AWS IoT has the device plane but a stateless rules engine; n8n has flows and no device plane;
Temporal has durability and nothing device-shaped.

If that positioning holds, **P3-1 is the single highest-value item in this document** and breadth of
action nodes is the wrong investment.

---

### P3-1 · Client SDKs for the platforms the market actually runs (F17)

**Why.** `Clients/Wbskt.Client.Sdk` targets .NET 10 and nothing else. It is also not packaged — no
`PackageId`, version, README or licence metadata — so it cannot be published to NuGet as it stands,
which means today there is **no supported way for anyone outside this repository to connect a
client at all**.

`Docs/RoughFeatures.md` promises "client libraries in most languages and platforms" for "embedded
devices, IoT devices, applications, scripts, websites". .NET 10 does not run on an ESP32, a Pi Pico,
or most of the hardware that description implies.

**Where.** `Clients/Wbskt.Client.Sdk/` — `IWbsktClient`, `WbsktClient`, `IClientStorage`,
`Internal/AuthClient.cs`, `Internal/SocketClient.cs`, `Models/`.

**The wire protocol is small**, which is what makes this tractable: register with a policy PIN →
receive `ClientRefId` + secret → exchange them at `api/client-auth/login` for a 1-hour JWT → open a
WebSocket to `/ws` with the token in a header or `?access_token=` → exchange JSON frames capped at
64 KB. Read `SocketHandler.cs` and `AuthClient.cs` and write it down as a protocol document first;
every SDK then implements the same document.

**Steps.**
1. Package the .NET client properly (id, version, README, licence, symbols).
2. **Python** and **TypeScript** — together these cover scripts, servers and browsers.
3. **MicroPython or a small C client** for real hardware.
4. One conformance test suite the E2E project runs against each.

**Traps.**
- The browser path must use `?access_token=` because browsers cannot set headers on a WebSocket
  upgrade. Phase 0's F7 work depends on that path staying anonymous to the authorization pipeline —
  see the comment on `/ws` in `Hosts/Wbskt.Socket.Host/Program.cs`.
- Devices store the secret locally and replay it. Phase 0 hashed it server-side; the client side is
  unchanged and must stay that way.
- A revoked client is rejected at connect by the revocation cache. SDKs need a sane backoff rather
  than a reconnect storm.

**Effort.** ~1 week per SDK after the protocol document.

---

### P3-2 · Metering and billing (F19)

**Why.** Credits exist per run — a guard against a workflow that loops forever — and work well for
that. But there is no account balance, no plan, no quota rollup and no payment integration. There is
no way to charge anyone.

**Where.** `Databases/Wbskt.Database/Tables/RunCounters.sql` (`CreditsConsumed`), `Runs.CreditBudget`,
`RunCounters_TryCharge.sql`, `Run_GetStatsBy_WorkspaceId.sql`.

**Steps.** Roll credits up to the tenant, add a plan with limits (the same ceilings as P1-6), then a
payment provider. `RunCounters` already tracks the raw numbers; this is aggregation and a billing
integration, not new instrumentation.

**Traps.** Credit costs are configuration, not compiled in — a deliberate decision. Keep it.
Aggregation must be exact; a run that goes `OutOfCredits` is a terminal status users will dispute.

**Effort.** ~1 week plus provider integration.

---

### P3-3 · Transform node (F18)

**Why.** Twenty-one node kinds, all implemented, and none reshapes data. You can route on an
expression and set a variable, but you cannot map an array, rename fields, or convert a device's
payload into the shape an API expects. Every comparable product has this node and users reach for it
constantly.

**Where.** `Wbskt.Workflow.Abstraction/Models/Nodes/NodeKind.cs`,
`Wbskt.Workflow/NodeExecutors/Controls/`, the expression engine under `Models/Expressions/`.

**Steps.** Extend the existing expression engine into a mapping node before considering a sandboxed
script node — it covers most cases without the isolation problem.

**Traps.** `NodeKind.NotYetImplemented` is currently empty and `NodeExecutorRegistryTests` pins the
set against the real DI container: adding a kind without an executor fails there first, which is the
intended behaviour. Expressions are **strictly typed with no coercion** (`"5" ≠ 5`) — a deliberate
decision; do not quietly relax it for the transform node.

**Effort.** ~3–4 days for mapping.

---

### P3-4 · MFA, then SSO (F12 continued)

**Why.** Neither exists. TOTP is a contained addition once P1-2's token infrastructure is in place.
Leave SSO until an enterprise deal asks for it by name — it is a large surface and the shape depends
on which provider the first customer uses.

**Effort.** ~3 days TOTP; SSO is a project.

---

## Decisions the owner must make

Ask before guessing — each changes the shape of the work.

| | Decision | Blocks | Notes |
|---|---|---|---|
| **D1** | Where does the integration master key live — configuration, cloud KMS, or a secrets manager? | P1-3 | Config is simplest and rotates worst. A KMS makes P2-5's managed-infra move easier. |
| **D2** | Managed SQL/RabbitMQ, or stay self-hosted? | P2-5, and retroactively F4 | Phase 0 chose self-hosted with nightly backups. Revisit before the second customer. |
| **D3** | What may an unverified account do — nothing, or read-only until verified? | P1-2 | Affects registration UX and the console. |
| **D4** | Audit log retention, and who can read it? | P1-5 | `HistoryRetentionGc` is the existing model for the job. |
| **D5** | Does the positioning hold — connected-device automation, integrators and OEMs first? | All of Phase 3 | If it does, P3-1 outranks everything else in that phase. |

---

## Inherited open items

Not part of Phases 1–3, but they will surface. Recorded so nobody rediscovers them.

- **`Docs/Workflow.Engine.Gap.Report.md`** tracks 37 engine findings; **WF-10** (windowed
  `MaxConcurrency` on `ParallelForEach`) and **WF-29** (a durable record of inbound arrivals that
  produce no run) remain partly done, both by deliberate owner decision. That document explains both
  in full and is the authority on the engine.
- **`AddWorkflowEngine` is not independently usable** — see [Working in this repo](#working-in-this-repo).
  Worth revisiting as a design question, not a bug.
- **No API versioning.** No `/v1` anywhere, so the first breaking change breaks every deployed
  device simultaneously. Cheapest to fix before P3-1 ships SDKs that bake in URLs.
- **No root README.** MIT-licensed repository with no front door.
- **`Runs` has no `WorkspaceId`**; tenant scoping joins through `WorkflowDefinitions`.
- **Toasts reach every workspace member** — the notification hub feed is per workspace, not per
  permission. Nothing permission-sensitive should go in a toast until that is closed.
