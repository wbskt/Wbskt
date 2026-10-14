# Wbskt.E2E.FeatureTests

Black-box multi-service E2E feature tests for the WBSKT platform.  
Tests exercise the **public HTTP APIs** and the real `WbsktClient` SDK end-to-end.

## Prerequisites

The five dev hosts (auth, management, devices, socket, engine), SQL Server with both databases
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

The hosts find Redis through `ConnectionStrings__Redis=localhost:6379`. It carries access-token revocations between the auth and management hosts, so without it the revocation scenarios (`AUTH_TK_10`, `AUTH_OUT_11`) fail.

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
dotnet run --project Hosts/Wbskt.Socket.Host         # https://localhost:7020 / ws://localhost:5020
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

The two opt-in passes below must still run **alone**, with `--filter`: the rate-limit scenarios spend
the per-IP budget the other collections are signing in with, and the socket restart drops every live
device connection, the engine collection's included.

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

`E2E_AUTH_PERMIT_LIMIT` must match the limit the host is running with. `AUTH_RL_04` exhausts the separate refresh bucket instead, so restart the auth host with `RateLimiting__TokenRefresh__PermitLimit=120` for it (`E2E_REFRESH_PERMIT_LIMIT` defaults to 120 and must match); `AUTH_RL_05` and `AUTH_RL_11` spend it too. `AUTH_RL_12` exhausts the verification bucket, so restart with `RateLimiting__EmailVerification__PermitLimit=10` for it (`E2E_VERIFICATION_PERMIT_LIMIT` defaults to 10 and must match).

### 6. Socket host restart scenario (opt-in)

`DeviceRevocationTests.DEV_REV_02` revokes, deletes and rotates the secret of devices, restarts the socket host, and checks their old tokens are still refused: the socket host forgets every revocation event it heard, so this proves the cutoffs the management host keeps in Redis. It skips unless `E2E_SOCKET_RESTART_COMMAND` holds a command that restarts the socket host, and since the restart drops every live device connection, run it on its own:

```bash
E2E_SOCKET_RESTART_COMMAND="$PWD/Tests/Wbskt.E2E.FeatureTests/start-hosts.sh socket" \
  dotnet test Tests/Wbskt.E2E.FeatureTests --filter FullyQualifiedName~DeviceRevocationTests.DEV_REV_02
```

Against the hosts `e2e-up.sh` started, the restart command is
`E2E_SOCKET_RESTART_COMMAND="$PWD/Tests/Wbskt.E2E.FeatureTests/e2e-up.sh restart socket"`.

### CI

The `e2e` job in `.github/workflows/build-images.yml` runs all three passes on every pull request and push to master, against SQL Server, RabbitMQ and Redis service containers and the five hosts started with `start-hosts.sh`. The main pass runs the three collections in parallel. The socket restart pass runs once after the main one. The rate-limit pass restarts the auth host before each scenario, since the budget one exhausts outlives it. Image publishing does not wait on it yet.

The job does not use `e2e-up.sh` yet. Moving it over would build the five host images in the job, which costs build time and wants a cache of its own, so it is a separate decision; until then CI and the compose file are two ways of standing up the same thing, and a host setting one needs has to go in both.

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
