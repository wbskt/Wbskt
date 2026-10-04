# Wbskt.E2E.FeatureTests

Black-box multi-service E2E feature tests for the WBSKT platform.  
Tests exercise the **public HTTP APIs** and the real `WbsktClient` SDK end-to-end.

## Prerequisites

All four dev hosts, RabbitMQ, Redis and SQL Edge must be running **before** executing these tests.

### 1. Start infrastructure (Docker)

```bat
# From Scripts.bat — run SQL Edge and RabbitMQ containers
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

### 3. Start all four hosts

Use the Rider compound run configuration **RunAll** (`.run/RunAll.run.xml`), or start each host manually:

```
dotnet run --project Hosts/Wbskt.Auth.Host           # https://localhost:7000
dotnet run --project Hosts/Wbskt.Management.Host     # https://localhost:7010
dotnet run --project Hosts/Wbskt.Socket.Host         # https://localhost:7020 / ws://localhost:5020
dotnet run --project Hosts/Wbskt.Workflow.Engine.Host # https://localhost:7030
```

Or, from the repo root, build and start all four in the background as Development and wait until they answer (this is what CI does; logs land in `e2e-hosts/`):

```bash
Tests/Wbskt.E2E.FeatureTests/start-hosts.sh
```

The hosts must run as **Development**. `appsettings.Development.json` is what lets a freshly registered user sign in without confirming an address, and what raises the auth host's credential rate limit (`RateLimiting:Authentication:PermitLimit`) from the production default of 10 a minute to 1000, its refresh limit (`RateLimiting:TokenRefresh:PermitLimit`) from 120 to 1000, and its verification limit (`RateLimiting:EmailVerification:PermitLimit`) from 10 to 1000. That limit is per IP, every test shares one, so at 10 the suite starts failing with `429 Too Many Requests` within a few scenarios.

### 4. Run the E2E tests

```bash
dotnet test Tests/Wbskt.E2E.FeatureTests
```

When hosts are not reachable the tests **skip** automatically — they will never fail due to infrastructure being down.

### 5. Rate-limit scenarios (opt-in)

`RateLimitingTests` exhaust the per-IP budget on purpose, so they skip unless `E2E_RATE_LIMIT_TESTS=1`. The budget one scenario spends is still spent when the next starts (it cannot even register its user), so run them one at a time, each against an auth host freshly restarted with the production limit. The limiter is in memory, so a restart is a fresh window:

```bash
RateLimiting__Authentication__PermitLimit=10 Tests/Wbskt.E2E.FeatureTests/start-hosts.sh auth
E2E_RATE_LIMIT_TESTS=1 E2E_AUTH_PERMIT_LIMIT=10 \
  dotnet test Tests/Wbskt.E2E.FeatureTests --filter FullyQualifiedName~RateLimitingTests.AUTH_RL_01
```

`E2E_AUTH_PERMIT_LIMIT` must match the limit the host is running with. `AUTH_RL_04` exhausts the separate refresh bucket instead, so restart the auth host with `RateLimiting__TokenRefresh__PermitLimit=120` for it (`E2E_REFRESH_PERMIT_LIMIT` defaults to 120 and must match); `AUTH_RL_05` and `AUTH_RL_11` spend it too. `AUTH_RL_12` exhausts the verification bucket, so restart with `RateLimiting__EmailVerification__PermitLimit=10` for it (`E2E_VERIFICATION_PERMIT_LIMIT` defaults to 10 and must match).

### CI

The `e2e` job in `.github/workflows/build-images.yml` runs both passes on every pull request and push to master, against SQL Server, RabbitMQ and Redis service containers and the four hosts started with `start-hosts.sh`. The rate-limit pass restarts the auth host before each scenario, since the budget one exhausts outlives it. Image publishing does not wait on it yet.

---

## Environment variable overrides

All base URLs can be overridden so the tests can target non-default environments:

| Variable              | Default                    |
|-----------------------|----------------------------|
| `E2E_AUTH_URL`        | `https://localhost:7000`   |
| `E2E_MANAGEMENT_URL`  | `https://localhost:7010`   |
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

## Privileged identity (seeded admin)

Permission-gated operations (registration-policy creation, workflow publish) are performed as the
**seeded administrator** provisioned by `Databases/Wbskt.Database.Auth/Scripts/Script.PostDeployment.sql`:

- Username `root`, email `admin@wbskt.com`, password `Password123!`
- `Admin` role → **all** permissions
- Owner of the **Default Workspace** (internal `Id = 1`)

The fixture's `LoginAsAdminAsync()` logs in as this admin and resolves its Default Workspace `RefId`
via `GET /api/workspaces`. A freshly-registered user only gets the `User` role and would be `403`-rejected
on the policy/workspace-scoped endpoints, which is why the admin is used. Override the credentials with
`E2E_ADMIN_EMAIL` / `E2E_ADMIN_PASSWORD` if your deployment seeds a different admin.

Because the admin owns the workspace whose internal `Id = 1`, the published
`WorkflowDefinition.WorkspaceId = 1` (and `PublishedBy = 1`, the root user id) correspond to real seeded rows.

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

The trigger port id is `"default"` to match what `PassthroughTriggerExecutor` emits (`Continue("default", …)`), which is what `BranchLoop.ResolveNextNodeId` uses to follow the outgoing edge.
