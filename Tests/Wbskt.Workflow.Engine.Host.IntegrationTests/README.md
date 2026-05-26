# Wbskt.Workflow.Engine.Host.IntegrationTests

Integration test suite for the WBSKT workflow engine — validates SQL semantics that unit tests
with mocks cannot cover: TVP batch inserts, `UPDLOCK READPAST` lease concurrency, `OUTPUT`
atomic race, `CompareAndSet`, and a full happy-path end-to-end run.

## Prerequisites

### 1. SQL Edge (or SQL Server Express)

The tests require a running SQL Edge or SQL Server instance:

```bat
docker run -e "ACCEPT_EULA=1" -e "MSSQL_SA_PASSWORD=Welcome1234" ^
           -p 1433:1433 --name azuresqledge -d mcr.microsoft.com/azure-sql-edge
```

Default credentials: `sa` / `Welcome1234` on `localhost:1433`.  
Override with the environment variable:

```
WBSKT_INTEGRATION_CONNSTR=Server=myserver;Database=master;User Id=sa;Password=...;TrustServerCertificate=True;
```

### 2. DACPAC

The test fixture automatically builds and deploys the DACPAC. If you want to pre-build it:

```powershell
dotnet build Databases/Wbskt.Database/Wbskt.Database.sqlproj
```

The fixture expects the DACPAC at:
`Databases/Wbskt.Database/bin/Debug/Wbskt.Database.dacpac`

### 3. sqlpackage

Required for DACPAC deployment:

```
dotnet tool install -g microsoft.sqlpackage
```

## Running the tests

```powershell
dotnet test Tests/Wbskt.Workflow.Engine.Host.IntegrationTests
```

If SQL Edge is not reachable, **all tests will silently pass** (they skip their assertions and
return early), with a note written to the test output explaining the skip reason.

## Test tasks

| Task  | Class                                          | What it tests                                      |
|-------|------------------------------------------------|----------------------------------------------------|
| 13.1  | `SmokeTests`                                   | SQL Edge reachable + all workflow tables/sprocs    |
| 13.2  | `WorkflowDefinitionProviderIntegrationTests`   | Insert/retrieve/deprecate round-trips              |
| 13.3  | `RunCountersIntegrationTests`                  | Atomic `OUTPUT inserted.ActiveBranchCount` race    |
| 13.4  | `BookmarkLeaseIntegrationTests`                | `UPDLOCK READPAST` disjoint concurrent leases      |
| 13.5  | `HistoryEventTvpIntegrationTests`              | TVP batch insert, monotone `HistoryEventId`        |
| 13.6  | `SharedVariableCasIntegrationTests`            | CompareAndSet with stale value returns 0 rows      |
| 13.7  | `PendingTriggerEventQueueIntegrationTests`     | FIFO dequeue, concurrent dequeues are distinct     |
| 13.8  | `HappyPathIntegrationTests`                    | Publish → start run → BranchLoop → Succeeded      |

## Database isolation

Each test **collection** run creates a unique database named `WbsktIT_<timestamp>_<guid>` and
drops it in `DisposeAsync`. Tests within the collection share one database; they use unique
`RefId` GUIDs to avoid cross-test pollution.
