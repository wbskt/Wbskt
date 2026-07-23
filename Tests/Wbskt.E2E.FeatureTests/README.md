# Wbskt.E2E.FeatureTests

Black-box multi-service E2E feature tests for the WBSKT platform.  
Tests exercise the **public HTTP APIs** and the real `WbsktClient` SDK end-to-end.

## Prerequisites

All four dev hosts, RabbitMQ, and SQL Edge must be running **before** executing these tests.

### 1. Start infrastructure (Docker)

```bat
# From Scripts.bat — run SQL Edge and RabbitMQ containers
docker run -e "ACCEPT_EULA=1" -e "MSSQL_SA_PASSWORD=Welcome1234" -p 1433:1433 -d mcr.microsoft.com/azure-sql-edge
docker run -d --hostname wbskt-rabbit --name wbskt-rabbit -p 5672:5672 -p 15672:15672 rabbitmq:3-management
```

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

### 4. Run the E2E tests

```bash
dotnet test Tests/Wbskt.E2E.FeatureTests
```

When hosts are not reachable the tests **skip** automatically — they will never fail due to infrastructure being down.

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
