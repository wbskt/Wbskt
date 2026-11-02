# Wbskt.E2E.FeatureTests

Black-box multi-service E2E feature tests for the WBSKT platform.  
Tests exercise the **public HTTP APIs** and the real `WbsktClient` SDK end-to-end.

## Prerequisites

The five dev hosts (auth, management, devices, gateway-ws, engine), SQL Server with both databases
deployed, RabbitMQ and Redis must be running **before** executing these tests.

### One command

From the repo root:

```bash
Tests/Wbskt.E2E.FeatureTests/e2e-up.sh     # infrastructure, databases and hosts; returns when all answer
dotnet test Tests/Wbskt.E2E.FeatureTests
Tests/Wbskt.E2E.FeatureTests/e2e-up.sh down  # stop it all and drop the database
```

`e2e-up.sh` runs `deploy/compose/docker-compose.e2e.yml`: SQL Server, RabbitMQ, Redis and
[Mailpit](https://mailpit.axllent.org/), the migrator, which publishes both databases incrementally
on every start (so a local database always matches the checkout), and the five hosts built from
source with `deploy/docker/Dockerfile` and run as Development. The first start builds six images and
takes a few minutes; later ones reuse the cache. It replaces steps 1 to 3 below.

- **Host networking.** The hosts run with `network_mode: host`, so they bind the same localhost
  ports `start-hosts.sh` uses and reach the fakes the integration scenarios start on 127.0.0.1. That
  works on Linux as is; on Docker Desktop (macOS, Windows) turn on host networking in its settings.
  The ports must be free: stop any hosts, SQL Server, RabbitMQ or Redis you already run on them.
- **HTTPS.** The hosts serve their HTTPS ports with your own ASP.NET Core development certificate,
  which `e2e-up.sh` exports with `dotnet dev-certs https` on every start. It must be trusted on this
  machine, as for running the hosts from source (`dotnet dev-certs https --trust`; on Linux, see the
  "Trust the development certificate" step in `.github/workflows/build-images.yml`).
- **Mail.** The auth host sends its mail to Mailpit. Read it at http://localhost:8025.
- **Infrastructure only.** `e2e-up.sh infra` starts SQL Server (sa password `Welcome1234`, as
  `Config/connectionstrings.json` expects), RabbitMQ, Redis and Mailpit and deploys the databases,
  then stops, for running the hosts yourself (step 3).
- **Logs and the rest.** `e2e-up.sh logs -f engine`, `e2e-up.sh ps`: any other argument is passed
  to `docker compose`. `e2e-up.sh restart <host>` restarts one host and waits for it.

CI does not use it yet: the `e2e` job still starts the hosts with `start-hosts.sh` (see [CI](#ci)).

### By hand

The same, step by step. `e2e-up.sh infra` does steps 1 and 2.

### 1. Start infrastructure (Docker)

```bat
# Run SQL Edge, RabbitMQ and Redis containers
docker run -e "ACCEPT_EULA=1" -e "MSSQL_SA_PASSWORD=Welcome1234" -p 1433:1433 -d mcr.microsoft.com/azure-sql-edge
docker run -d --hostname wbskt-rabbit --name wbskt-rabbit -p 5672:5672 -p 15672:15672 rabbitmq:3-management
docker run -d --name wbskt-redis -p 6379:6379 redis:7-alpine
```

The auth host, the management host and the WebSocket gateway find Redis at `localhost:6379` through `ConnectionStrings:Redis` in their `appsettings.Development.json` (override it with `ConnectionStrings__Redis`). It carries access-token revocations between the auth and management hosts and device revocations from the management host to the WebSocket gateway, so without it the revocation scenarios (`AUTH_TK_10`, `AUTH_OUT_11`, `DEV_REV_01`) fail.

### 2. Deploy databases

```powershell
# First install the DACPAC tool if not already present:
dotnet tool install -g microsoft.sqlpackage

# Deploy (add -Fresh to drop and recreate):
pwsh ./Deploy-Databases.ps1
```

### 3. Start the hosts

Use the Rider compound run configuration **RunAll** (`.run/RunAll.run.xml`), or start each host manually:

```
dotnet run --project Hosts/Wbskt.Auth.Host           # https://localhost:7000
dotnet run --project Hosts/Wbskt.Management.Host     # https://localhost:7010
dotnet run --project Hosts/Wbskt.Gateway.WebSocket.Host  # https://localhost:7020 / ws://localhost:5020
dotnet run --project Hosts/Wbskt.Workflow.Engine.Host # https://localhost:7030
```

Or, from the repo root, build and start all five in the background as Development and wait until they answer (this is what CI does; logs land in `e2e-hosts/`):

```bash
Tests/Wbskt.E2E.FeatureTests/start-hosts.sh
```

The hosts must run as **Development**. `appsettings.Development.json` is what lets a freshly registered user sign in without confirming an address, and what raises the auth host's credential rate limit (`RateLimiting:Authentication:PermitLimit`) from the production default of 10 a minute to 100000, its refresh limit (`RateLimiting:TokenRefresh:PermitLimit`) from 120 to 100000, and its verification limit (`RateLimiting:EmailVerification:PermitLimit`) from 10 to 100000. That limit is per IP, every test shares one, so at 10 the suite starts failing with `429 Too Many Requests` within a few scenarios. At 1000 it failed too: the suite registers and signs in close to a thousand users, and a run that fits in one minute's window spends the whole budget before its last scenarios.

### 4. Run the E2E tests

```bash
dotnet test Tests/Wbskt.E2E.FeatureTests
```

When hosts are not reachable the tests **skip** automatically — they will never fail due to infrastructure being down.

#### Collections and parallelism

The suite is three xUnit collections, by folder (`Fixtures/E2ECollections.cs`):

| Collection | Folders |
|---|---|
| `E2E auth` (`AuthCollection`) | `Scenarios/Auth` |
| `E2E devices` (`DevicesCollection`) | `Scenarios/Devices` |
| `E2E engine` (`EngineCollection`) | every other folder under `Scenarios` |

`xunit.runner.json` runs the three at the same time (`maxParallelThreads: 3`, one per collection);
the classes inside a collection still run one after another. That is safe because every scenario
arranges a user and workspace of its own (see [Identities](#identities-a-fresh-user-and-workspace-per-scenario)),
and the Development rate limits sit far above what the three spend together. A new scenario class
goes in the collection of its folder: `[Collection(AuthCollection.Name)]`,
`[Collection(DevicesCollection.Name)]` or `[Collection(EngineCollection.Name)]`. Each collection gets
its own `ServicesFixture`, so the fixture must not hold anything that has to be process-wide.

To run one collection, filter by its folders' namespace:

```bash
dotnet test Tests/Wbskt.E2E.FeatureTests --filter "FullyQualifiedName~Scenarios.Auth"
dotnet test Tests/Wbskt.E2E.FeatureTests --filter "FullyQualifiedName~Scenarios.Devices"
```

The three opt-in passes below must still run **alone**, with `--filter`: the rate-limit scenarios spend
the per-IP budget the other collections are signing in with, the gateway restart drops every live
device connection, the engine collection's included, and the outage pass stops the broker, Redis and
the engine under everything else.

The integration scenarios (`Scenarios/Integrations`) stand in for Telegram, Discord, ntfy, mail relays and HTTP APIs with local fakes on 127.0.0.1. The engine's Development settings (`WorkflowEngine:Integrations`) give it a fixed master key, allow plain http and any SMTP port, and point Telegram at `http://127.0.0.1:5098/telegram`, so port 5098 must be free while they run. The other fakes take a free port each.

### 5. Rate-limit scenarios (opt-in)

`RateLimitingTests` exhaust the per-IP budget on purpose, so they skip unless `E2E_RATE_LIMIT_TESTS=1`. The budget one scenario spends is still spent when the next starts (it cannot even register its user), so run them one at a time, each against an auth host freshly restarted with the production limit. The limiter is in memory, so a restart is a fresh window:

```bash
RateLimiting__Authentication__PermitLimit=10 Tests/Wbskt.E2E.FeatureTests/start-hosts.sh auth
E2E_RATE_LIMIT_TESTS=1 E2E_AUTH_PERMIT_LIMIT=10 \
  dotnet test Tests/Wbskt.E2E.FeatureTests --filter FullyQualifiedName~RateLimitingTests.AUTH_RL_01
```

`e2e-up.sh` cannot restart its auth host with other limits. Against it, stop that one container and
run the auth host from source in its place, against the same infrastructure:
`Tests/Wbskt.E2E.FeatureTests/e2e-up.sh stop auth` and `dotnet build Hosts/Wbskt.Auth.Host`, then the
`start-hosts.sh auth` line above (with a host named, it does not build). `e2e-up.sh restart auth`
puts the container back.

The `RateLimited429` row of the error-shape contract theory (see [Contract tests](#contract-tests)) is gated the same way and runs the same way, against a freshly restarted auth host:

```bash
RateLimiting__Authentication__PermitLimit=10 Tests/Wbskt.E2E.FeatureTests/start-hosts.sh auth
E2E_RATE_LIMIT_TESTS=1 E2E_AUTH_PERMIT_LIMIT=10 \
  dotnet test Tests/Wbskt.E2E.FeatureTests --filter "FullyQualifiedName~ErrorShapeContractTests&DisplayName~RateLimited429"
```

`E2E_AUTH_PERMIT_LIMIT` must match the limit the host is running with. `AUTH_RL_04` exhausts the separate refresh bucket instead, so restart the auth host with `RateLimiting__TokenRefresh__PermitLimit=120` for it (`E2E_REFRESH_PERMIT_LIMIT` defaults to 120 and must match); `AUTH_RL_05` and `AUTH_RL_11` spend it too. `AUTH_RL_12` exhausts the verification bucket, so restart with `RateLimiting__EmailVerification__PermitLimit=10` for it (`E2E_VERIFICATION_PERMIT_LIMIT` defaults to 10 and must match).

### 6. WebSocket gateway restart scenario (opt-in)

`DeviceRevocationTests.DEV_REV_02` revokes, deletes and rotates the secret of devices, restarts the WebSocket gateway, and checks their old tokens are still refused: the WebSocket gateway forgets every revocation event it heard, so this proves the cutoffs the management host keeps in Redis. It skips unless `E2E_SOCKET_RESTART_COMMAND` holds a command that restarts the WebSocket gateway, and since the restart drops every live device connection, run it on its own:

```bash
E2E_SOCKET_RESTART_COMMAND="$PWD/Tests/Wbskt.E2E.FeatureTests/start-hosts.sh gateway-ws" \
  dotnet test Tests/Wbskt.E2E.FeatureTests --filter FullyQualifiedName~DeviceRevocationTests.DEV_REV_02
```

Against the hosts `e2e-up.sh` started, the restart command is
`E2E_SOCKET_RESTART_COMMAND="$PWD/Tests/Wbskt.E2E.FeatureTests/e2e-up.sh restart gateway-ws"`.

### 7. Outage pass (opt-in)

`Scenarios/Outage` takes the hosts' dependencies away under them and checks what the code promises
while they are gone and once they are back. They skip unless `E2E_OUTAGE_COMMAND` holds a command
that performs an outage action, with `{action}` standing for the action (appended when absent).
`outage.sh` is that command for hosts run with `start-hosts.sh`; it stops and starts the RabbitMQ
and Redis containers named by `E2E_RABBITMQ_CONTAINER` and `E2E_REDIS_CONTAINER` (an id or a name),
and runs a second engine from the same build on ports 5031/7031:

| Action | What it does |
|---|---|
| `broker-stop`, `broker-start` | `docker stop` / `docker start` the RabbitMQ container; start waits until it is healthy |
| `redis-stop`, `redis-start` | the same for Redis |
| `engine2-start` | a second engine beside the first, on the same database and broker; it waits as a standby |
| `engine-kill-leader` | `kill -9` whichever engine answers `/healthz/ready` (only the lease holder does), and print its port |
| `engine-restore` | stop the second engine, restart the original if it is gone, and wait until it holds the lease again |
| `restore-all` | the broker, Redis and the original engine, all back |

Every action waits until what it changed has settled, and bringing back something already up does
nothing, so each scenario restores what it took down whether it passed or not. Run the pass on its
own, as it stops what every other scenario is using:

```bash
E2E_OUTAGE_COMMAND="$PWD/Tests/Wbskt.E2E.FeatureTests/outage.sh {action}" \
E2E_RABBITMQ_CONTAINER=wbskt-rabbit E2E_REDIS_CONTAINER=wbskt-redis \
  dotnet test Tests/Wbskt.E2E.FeatureTests --filter "FullyQualifiedName~Scenarios.Outage"
```

| Class | What it asserts |
|---|---|
| `BrokerOutageE2ETests` | With RabbitMQ stopped, a command and a ping answer 503 `EVENT_BUS_UNAVAILABLE` with `Retry-After`, sign-in still works, and a webhook to the public callback answers 503 with `Retry-After`. Once it is back, pings and commands go through again and the retried webhook starts its run. The callback does not hold a delivery it could not queue: it is the sender's retry that drains it. |
| `RedisOutageE2ETests` | With Redis stopped, a device revoked before the outage and one revoked during it are refused at the socket (the WebSocket gateway falls back to the revocations it heard by event), and an untouched device still connects. A member removed from a workspace during the outage is refused by the auth host at once and by the management host within its access-cache lifetime (`WorkspaceAccessCache:Seconds`, 30 s). |
| `EngineFailoverE2ETests` | With a second engine running, the standby answers inbound calls 503 `ENGINE_NOT_LEADER` with `Retry-After` and is not ready. A run parks on a signal; the leader is killed; the standby keeps refusing until it holds the lease, then serves. The signal, sent to it, resumes the parked branch and the run succeeds, with one run, one `BranchStarted`, one `BranchParked` and one `BranchResumed`. |

The management host calls the one engine URL it is configured with, so after the kill its engine
calls fail until the original engine is restored; the failover scenario watches the takeover and
resumes the run on the second engine's own inbound route, which is what a proxy routing on
`/healthz/ready` would send to it. The engine actions assume the hosts `start-hosts.sh` runs (they
use its pid files in `e2e-hosts/`), not those `e2e-up.sh` runs in containers.

### CI

The `e2e` job in `.github/workflows/build-images.yml` runs on every pull request that can affect the hosts and on every push to master, as five matrix legs. Each leg is its own runner with its own SQL Server, RabbitMQ and Redis service containers and its own five hosts started with `start-hosts.sh`, so the passes that used to wait for each other run side by side:

| Leg | Runs |
|---|---|
| `lifecycle` | the main pass, `Scenarios/Lifecycle` |
| `triggers` | the main pass, `Scenarios/Triggers` and `Scenarios/Timeouts` |
| `rest` | the main pass, every other folder (its filter is the complement of the two above), plus the OpenAPI snapshot check |
| `rate-limit` | the rate-limit pass: the auth host restarted before each scenario, and once more for the 429 row of the error-shape contract theory |
| `outage` | the gateway restart scenario, then the outage pass, which gets the RabbitMQ and Redis service containers by id and runs `outage.sh restore-all` at the end whatever happened |

The engine collection runs its classes one after another, so it is what sets the main pass's length; the shards split it by folder. If one shard grows much longer than the others, move a folder between the first two filters. The `e2e-shards` job then checks that each shard ran tests and that no test ran in two shards. Image publishing waits for every leg and for that check.

The job does not use `e2e-up.sh` yet. Moving it over would build the five host images in the job, which costs build time and wants a cache of its own, so it is a separate decision; until then CI and the compose file are two ways of standing up the same thing, and a host setting one needs has to go in both.

---

## Contract tests

`Scenarios/Contract` checks the shapes every caller depends on, route by route, rather than what any
one feature does. Both are theories with a row per case, so a new route or status is one more row.

**`ListRoutesContractTests`**: one row per list route of the auth and management hosts, carrying the
paging the route is expected to answer with. Each row arranges just enough data (a workspace, a
policy and devices, published workflows and their runs, roles, groups, invitations, tenant members),
reads the list as raw JSON and checks:

| Paging | Routes | Shape |
|---|---|---|
| Offset | registration policies, clients, message templates, workflows; workspace members; tenant invitations, roles, groups, permissions, members | `items`, `nextCursor`, `totalCount`, and the same count in `X-Total-Count` |
| Keyset | event logs, a device's comms, a workflow's runs, the workspace's runs, a run's history | `items` and `nextCursor`; no count anywhere |
| Whole | client tags, a device's state, a workflow's versions | the page shape around the whole list: `nextCursor` null, `limit` ignored |
| Whole, counted | integrations | as Whole, with `totalCount` |
| Array | the caller's workspaces, sessions and tenants; a role's permissions, a group's roles, a member's roles, permissions and groups | a bare JSON array |

Where a row arranges two rows on a paged route, `limit=1` must give one item and a cursor, and the
cursor must lead to a different item. The readings series, the CSV exports, the event-log summary,
the deprecated `clients/policy/{policyRef}` alias and the internal routes are not lists and are left out.

**`ErrorShapeContractTests`**: one row per error status, each provoked on the running hosts, and
each must answer `application/problem+json` with `status`, `title`, the expected `code` and a
non-empty `traceId`:

| Row | Provoked by | Code |
|---|---|---|
| `Validation400` | creating a workspace with an empty name; also checks `errors` names the field | `VALIDATION_FAILED` |
| `Unauthenticated401` | no token on a management route | `AUTH_UNAUTHORIZED` |
| `Forbidden403` | another user's workspace | `WORKSPACE_FORBIDDEN` |
| `NotFound404` | an unknown device in one's own workspace | `CLIENT_NOT_FOUND` |
| `Conflict409` | a second role of the same name in a tenant | `AUTH_ROLE_CONFLICT` |
| `RateLimited429` | failed logins past the credential limit; also checks `Retry-After`. Opt-in: `E2E_RATE_LIMIT_TESTS=1`, run alone (see [5](#5-rate-limit-scenarios-opt-in)) | `RATE_LIMITED` |
| `Unavailable503` | always skipped: nothing answers 503 on demand against Development hosts (the 503s are an unreachable auth host or broker, a non-leader engine, and unset internal or engine API keys, which only Production refuses) | |

The per-user `Api` limiter cannot stand in for the 429: Development raises it to 100,000 a minute.

### OpenAPI snapshots

`openapi/auth.v1.json` and `openapi/management.v1.json` are the hosts' OpenAPI documents, checked in,
so a change to a route or a request or response shape is a diff someone reviews rather than
something `Docs/API.Endpoints.md` catches up with later. The hosts serve them at `/openapi/v1.json`
when API docs are on, which they are in Development. The engine host references the OpenAPI package
but maps no document, and the WebSocket gateway serves none, so neither has a snapshot.

CI's `e2e` job runs `openapi/refresh.sh --check` once the hosts are up: it fetches each document,
normalizes it (keys sorted with `jq -S`, `servers` dropped, since that is the address it was fetched
from) and fails with a diff when it differs from the snapshot. The documents it fetched are uploaded
as the `openapi-snapshots` artifact either way. After an intended change, refresh the snapshots
against hosts started as Development and commit them:

```bash
Tests/Wbskt.E2E.FeatureTests/start-hosts.sh        # or e2e-up.sh
Tests/Wbskt.E2E.FeatureTests/openapi/refresh.sh
```

or take the two files from the failed run's artifact. Until a snapshot is committed, `--check` writes
it, warns "snapshot created, commit it", and passes; see `openapi/README.md`.

---

## Environment variable overrides

All base URLs can be overridden so the tests can target non-default environments:

| Variable              | Default                    |
|-----------------------|----------------------------|
| `E2E_AUTH_URL`        | `https://localhost:7000`   |
| `E2E_MANAGEMENT_URL`  | `https://localhost:7010`   |
| `E2E_DEVICES_URL`     | `https://localhost:7015`   |
| `E2E_SOCKET_URL`      | `https://localhost:7020`   |
| `E2E_SOCKET_WS_URL`   | `ws://localhost:5020`      |
| `E2E_WORKFLOW_URL`    | `https://localhost:7030`   |
| `E2E_ENGINE2_URL`     | `http://localhost:5031`    |
| `E2E_ADMIN_EMAIL`     | `admin@wbskt.com`          |
| `E2E_ADMIN_PASSWORD`  | `Password123!`             |

---

## Test coverage

| Class | What it tests |
|---|---|
| `AuthAndRegistrationTests` | A new user can register + login; then the **seeded admin** creates an AutoApproval policy → client registration → client login |
| `WorkflowCommandLoopTests` | Full round-trip: publish `DeviceTrigger→action:clientMessage` workflow, connect WbsktClient, send telemetry, assert OpenVent command received and run completes |

---

## Identities: a fresh user and workspace per scenario

Every scenario that publishes workflows, registers devices, starts runs or reads a workspace's lists
and event feed arranges a **workspace of its own**:

```csharp
var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
```

`CreateIsolatedWorkspaceAsync()` registers a brand-new user and creates a workspace they own, and
returns a `WorkspaceHandle` (`Token`, `WorkspaceRef`, `User`). Registration provisions the user's own
tenant with them as its tenant-wide `Admin`, which holds every permission, so policy creation,
workflow publish, run start/cancel, integrations and event-log reads are all allowed. Nothing a
scenario creates is ever cleaned up, but because no two scenarios share a workspace, a scenario's
run list and event feed hold only its own rows and the per-workspace ceilings (100 workflows,
1000 devices) stay at their production defaults.

### The seeded admin

`LoginAsAdminAsync()` logs in as the **seeded administrator** provisioned by
`Databases/Wbskt.Database.Auth/Scripts/Script.PostDeployment.sql` and resolves its Default Workspace
`RefId` via `GET /api/workspaces`:

- Username `root`, email `admin@wbskt.com`, password `Password123!` (the development password
  `Deploy-Databases.ps1` passes by default; a database deployed without it has no usable root password)
- `Admin` role → **all** permissions
- Owner of the **Default Workspace** (internal `Id = 1`)

It is only for the scenarios under `Scenarios/Auth` that are about that administrator, its tenant or
the Default Workspace themselves. Those lists grow across runs, so assertions on them are "contains",
never "count equals". Override the credentials with `E2E_ADMIN_EMAIL` / `E2E_ADMIN_PASSWORD` if your
deployment seeds a different admin.

## Waiting

No bare `Task.Delay` for eventual consistency. A sleep long enough for a slow runner wastes time on a
fast one, and a sleep before a negative assertion ("no second run within 5 s") passes on a runner too
slow to have produced the second run yet. Wait for something observable instead, with the fixture's
helpers in `Fixtures/ServicesFixture.cs`:

- **Runs and history:** `WaitForFirstRunAsync`, `WaitForRunCountAsync`, `WaitForRunStatusAsync`,
  `WaitForRunTerminalAsync`, `WaitForHistoryEventAsync`, and `WaitForBranchParkedAsync` (a
  `BranchParked` event: the branch is parked on its bookmark).
- **Acting on a parked run:** the engine records the park just before it writes the bookmark, so
  `SendSignalUntilMatchedAsync` repeats a signal until it resumes one; repeat an http wake until its
  effect shows.
- **A trigger going live after publish:** nothing reports it, so `WaitForManualTriggerLiveAsync` starts
  manual runs until one is started, and `FireWebhookUntilRunAsync` fires a webhook until a run appears.
  Deliveries still queued can start more runs afterwards: count runs by a marker in their trigger
  data (`ListRunsCarryingAsync`, `WaitForRunsCarryingAsync`), not by total.
- **Anything else:** `ServicesFixture.PollUntilAsync(what, probe, timeout)`. The probe returns whether
  it is done and a description of what it saw, and a timeout throws "Timed out after … waiting for
  {what}; last state: {state}". The `WaitFor…` helpers throw the same way rather than returning
  null or `Guid.Empty`.

For a negative assertion, wait for the competing positive signal and then assert: a sentinel workflow
on the same device whose run can only start after the moment in question (see `ClientHoldTriggerE2ETests`,
`ClientPresenceTriggerE2ETests`, `OfflineBufferE2ETests`), a later delivery's run, or a finished run's
history, which is complete once the run is terminal (a command node that ran twice shows there even
while its second command is still in flight).

A `Task.Delay` that remains is either the scenario itself (a reading that has to be 30 s old, a hold
or blip of a set length, signals aimed at a TTL running out) and says so, or a bounded wait with no
signal to wait for, written as `ServicesFixture.WaitWithoutSignalAsync(duration, reason)`. Timeouts
are generous (at least twice what the wait usually takes): they cost nothing when the signal comes.

## Workflow definition published by Task 3

```
DeviceTrigger
  config: { clientRef: <clientRefId>, type: "telemetry", concurrencyPolicy: "AllowParallel" }
  ports:  [{ portId: "default", direction: "Output" }]
      |
      | edge from=(triggerNodeId,"default") to=(actionNodeId,"in")
      ▼
action:clientMessage (SendCommandActionNode)
  config: { clientRef: <clientRefId>, type: "OpenVent" }
  ports:  [{ portId: "in", direction: "Input" }, { portId: "out", direction: "Output" }]
```

The trigger port id is `"default"` to match what `PassthroughTriggerExecutor` emits (`Continue("default", …)`), which is the port the branch loop's `ContinueResultHandler` follows to the outgoing edge (`BranchRouting.ResolveNext`).
