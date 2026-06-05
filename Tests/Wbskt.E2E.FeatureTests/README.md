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

---

## Test coverage

| Class | What it tests |
|---|---|
| `AuthAndRegistrationTests` | Register user → login → create AutoApproval policy → device registration → client login |
| `WorkflowCommandLoopTests` | Full round-trip: publish `DeviceTrigger→action:command` workflow, connect WbsktClient, send telemetry, assert OpenVent command received and run completes |

---

## Workspace acquisition (design note)

After login the fixture calls `GET /api/workspaces` on the Auth host to list workspaces.  
If the user has none (fresh registration), it calls `POST /api/workspaces` to create one.  
The returned workspace `RefId` (GUID) is used for the registration-policy endpoint.

The `WorkflowDefinition.WorkspaceId` (internal `int`) is set to `1` as a placeholder — the Management host stores it as-is from the submitted JSON without cross-validating against the authenticated user's workspace.

## Workflow definition published by Task 3

```
DeviceTrigger
  config: { deviceRef: <clientRefId>, event: "telemetry", concurrencyPolicy: "AllowParallel" }
  ports:  [{ portId: "default", direction: "Output" }]
      |
      | edge from=(triggerNodeId,"default") to=(actionNodeId,"in")
      ▼
action:command (SendCommandActionNode)
  config: { deviceRef: <clientRefId>, command: "OpenVent" }
  ports:  [{ portId: "in", direction: "Input" }, { portId: "out", direction: "Output" }]
```

The trigger port id is `"default"` to match what `DeviceTriggerExecutor` emits (`Continue("default", …)`), which is what `BranchLoop.ResolveNextNodeId` uses to follow the outgoing edge.
