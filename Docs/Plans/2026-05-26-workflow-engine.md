# Workflow Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a durable, IoT-flavoured workflow engine for WBSKT that supports
instance-per-trigger runs, fan-out/fan-in, bookmark-based long waits,
correlation-keyed concurrency, and crash-safe at-least-once execution with
idempotency keys — exactly as specified in
`Docs/Workflow.Engine.V3.Design.md`.

**Architecture:** Hybrid durability (per-branch snapshots + append-only history
events + per-attempt idempotency keys), single Engine Host with `IRunDispatcher`
and `ILeaseHolder` seams sized for future multi-host. Four-layer composition:
Providers (Scoped, `BaseSqlProvider`) → Engine Core (Singleton service layer)
→ Routing (Singleton: `InboundHub` / `BookmarkResumer` / `TriggerDispatcher`)
→ Adapters (`IHostedService` background workers). Five inbound adapters
(RabbitMQ, HTTP, Ticker, Signal, ChildRunCompleted) feed a single
`InboundHub`. Per-branch SQL UPSERT *is* the snapshot — no separate
`RunSnapshot` table.

**Tech Stack:** .NET 10, ASP.NET Core, SQL Server (SSDT `.sqlproj`),
MassTransit 8.x on RabbitMQ, `System.Threading.Channels` (in-process
dispatcher), `Microsoft.Data.SqlClient`, Serilog, xUnit + Moq, central
package management (`Directory.Packages.props`).

**Source spec:** `Docs/Workflow.Engine.V3.Design.md` (2,955 lines). Every task
in this plan cites a section. Do not deviate from the spec without amending
it first.

**Repo conventions reminders** (full list: `Docs/Coding.Conventions.md`):
- Public API exposes `RefId` (GUID); `Id` (int) is internal. Use the Reference
  Mapper Pattern (`AddKeyedScoped<IReferenceMapper, …>`) in controllers.
- `FindBy…` returns an internal `int`; `GetBy…` returns an entity.
- Async methods end in `Async`. No `null` returns — throw a descriptive
  exception.
- Collection returns use `IReadOnlyCollection<T>`.
- Providers inherit from `BaseSqlProvider`. **A provider never depends on
  another provider.** Cross-provider orchestration lives in the service layer.
- `await using var` for `IAsyncDisposable` (`SqlConnection`, `SqlDataReader`).
- Always braces on `if`/`for`/`while` — even single-line bodies.
- Blank line before any code comment.
- SSDT conventions: SQL keywords UPPERCASE; always `dbo.` prefix; one column
  per `SELECT` line; never `SELECT *`; timestamps are `DATETIME2(3)`; every
  new table gets `CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME()`.
- Stored proc naming: `Entity_FindBy_Criteria`, `Entity_GetBy_Criteria`,
  `Entity_Create`, `Entity_Update`, `Entity_Delete`.
- Do **not** upgrade MassTransit past v8.x (license change at v9).
- Add NuGet versions to `Directory.Packages.props`, not individual `.csproj`s.
- Every commit message ends with:
  `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`

**Working namespaces** (V3 suffix dropped; the design doc preserves the lineage):
- `Wbskt.Workflow.Abstraction` — authoring primitives (`WorkflowDefinition`, `Node`, `Edge`, `WakeCondition`, `Expression`).
- `Wbskt.Workflow` — storage providers, entities, mappers.
- `Wbskt.Workflow.Models` — DTOs crossing the host/controller boundary.
- `Wbskt.Workflow.Engine.Host` — engine runtime, routing, adapters, executors.

**Phases at a glance:**

| Phase | What | Approx. tasks |
|---|---|---|
| **0** | Clean slate: wipe old V1 + V2 source while keeping `.csproj` shells and host scaffolding | ~12 |
| **1** | Database schema: 11 new SSDT tables + supporting types | ~14 |
| **2** | Authoring layer (`Wbskt.Workflow.Abstraction`) — nodes, edges, expressions, schemas | ~12 |
| **3** | Storage providers (`Wbskt.Workflow`) — one provider per aggregate | ~14 |
| **4** | Engine Core primitives — `BranchContext`, `NodeExecutionResult`, `INodeExecutor`, clock/random/ID seams | ~8 |
| **5** | Branch Loop + `IRunDispatcher` (Channel impl) | ~12 |
| **6** | Bookmarks — `WakeCondition` family + `BookmarkScheduler` + `BookmarkResumer` + TTL race | ~14 |
| **7** | Triggers + `InboundHub` + `TriggerDispatcher` + correlation + concurrency policy | ~14 |
| **8** | Error model — `RetryPolicy`, `OnFailure`, `RunFinalizer`, compensation, cancellation | ~12 |
| **9** | Node executors (Http, Email, Telegram, Toast, Webhook, Delay, LogicGate, ForEach, ParallelForEach, Join, Variable, SendCommand, WaitForHttp, AwaitSignal, FailRun, SubWorkflow) | ~18 |
| **10** | Background services (BookmarkScheduler tick, Ticker, RunFinalizerSweeper, StartupRecovery, HistoryEventFlusher, DefinitionGC, RetentionPruner, MetricsExporter) + `ILeaseHolder` | ~10 |
| **11** | Inbound adapters (RabbitMQ, HTTP, Ticker, Signal, ChildRunCompleted) | ~10 |
| **12** | Management Host wiring — workflow CRUD + operator APIs (publish version, cancel run, list runs) | ~10 |
| **13** | Integration test suite (`Tests/Wbskt.Workflow.Engine.Host.IntegrationTests`) against SQL Edge docker | ~8 |

**Total:** ~168 tasks. Each task is one bite-sized action (red-green-refactor
step or a single commit-worthy edit). Strict TDD across the board.

**Self-test loop** (run from repo root after each task that touches C# code):
```powershell
dotnet build Wbskt.slnx
dotnet test Tests/Wbskt.Workflow.Engine.Host.Tests
```

**SSDT redeploy loop** (after any `.sql` change):
```powershell
pwsh ./Deploy-Databases.ps1
# add -Fresh to drop and recreate the database
```

---

## Phase 0 — Clean Slate

**Goal:** Delete every line of V1 and V2 workflow source. Leave the four
workflow `.csproj` shells, the Engine Host `Program.cs` reduced to a minimal
ASP.NET Core skeleton, the tests project shell, the workflow tables/sprocs
out of the database SSDT project, the workflow event contracts removed from
`Wbskt.Events`, and the workflow controller/service stripped from the
Management Host.

At the end of Phase 0:
- `dotnet build Wbskt.slnx` succeeds.
- `dotnet test Tests/Wbskt.Workflow.Engine.Host.Tests` succeeds (zero tests).
- `pwsh ./Deploy-Databases.ps1 -Fresh` succeeds against the dev SQL Edge
  container.
- All four hosts launch via the `.run/RunAll.run.xml` compound configuration
  without throwing on startup.
- `git grep -i "workflow"` returns only the design doc and (where present)
  unrelated incidental matches — no V1/V2 implementation source left.

---

### Task 0.1: Branch and worktree

**Files:**
- No source files; git state only.

- [ ] **Step 1: Create a feature branch**

```powershell
cd C:\dev\wbskt\Wbskt
git checkout -b feat/workflow-engine
git --no-pager status
```

Expected: `On branch feat/workflow-engine`, clean tree, design spec already
committed at `7d6938d`.

- [ ] **Step 2: Verify baseline build**

```powershell
dotnet build Wbskt.slnx
```

Expected: solution builds clean (or with the same warnings as `master`).
Capture the warning count to compare against later.

- [ ] **Step 3: Verify baseline tests**

```powershell
dotnet test Tests/Wbskt.Workflow.Engine.Host.Tests --nologo --verbosity minimal
```

Expected: existing V1 tests pass. Note the test count for reference.

---

### Task 0.2: Delete Engine Host V1 + V2 source

**Files:**
- Delete folder: `Hosts/Wbskt.Workflow.Engine.Host/Enums`
- Delete folder: `Hosts/Wbskt.Workflow.Engine.Host/Extensions`
- Delete folder: `Hosts/Wbskt.Workflow.Engine.Host/Handlers`
- Delete folder: `Hosts/Wbskt.Workflow.Engine.Host/Interfaces`
- Delete folder: `Hosts/Wbskt.Workflow.Engine.Host/Models`
- Delete folder: `Hosts/Wbskt.Workflow.Engine.Host/Services`
- Delete folder: `Hosts/Wbskt.Workflow.Engine.Host/V2`

- [ ] **Step 1: Delete the folders**

```powershell
Remove-Item -Recurse -Force Hosts\Wbskt.Workflow.Engine.Host\Enums,Hosts\Wbskt.Workflow.Engine.Host\Extensions,Hosts\Wbskt.Workflow.Engine.Host\Handlers,Hosts\Wbskt.Workflow.Engine.Host\Interfaces,Hosts\Wbskt.Workflow.Engine.Host\Models,Hosts\Wbskt.Workflow.Engine.Host\Services,Hosts\Wbskt.Workflow.Engine.Host\V2
```

- [ ] **Step 2: Verify only `Program.cs`, `Properties/`, `appsettings*.json`, and the `.csproj` remain**

```powershell
Get-ChildItem Hosts\Wbskt.Workflow.Engine.Host -Force | Where-Object { $_.Name -notmatch '^(obj|bin)$' } | ForEach-Object { $_.Name }
```

Expected output:
```
Properties
appsettings.Development.json
appsettings.json
Program.cs
Wbskt.Workflow.Engine.Host.csproj
```

- [ ] **Step 3: Do NOT commit yet** — Program.cs still references the deleted `Extensions` namespace and will not compile. Continue to Task 0.3.

---

### Task 0.3: Reduce Engine Host `Program.cs` to minimal skeleton

**Files:**
- Modify: `Hosts/Wbskt.Workflow.Engine.Host/Program.cs`

- [ ] **Step 1: Replace `Program.cs` with the minimal skeleton**

```csharp
using Serilog;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Configuration;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;

namespace Wbskt.Workflow.Engine.Host;

public static class Program
{
    private static readonly string ProgramDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Application.AppFolderName);

    public static async Task Main(string[] args)
    {
        Environment.SetEnvironmentVariable(Logging.LogPath, ProgramDataPath);
        Environment.SetEnvironmentVariable(Logging.LogName, typeof(Program).Namespace);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = Directory.GetCurrentDirectory()
        });

        builder.AddSharedConfiguration("serilog.json", "connectionstrings.json", "rabbitmq.json");

        builder.Host.UseSerilog(builder.CreateSerilog());

        builder.Services.AddRabbitMqEventBus(builder.Configuration);
        builder.Services.AddHttpClient();
        builder.Services.AddTransient<IStartupTask, FolderInitializationStartupTask>();
        builder.Services.AddAuthorization();
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            });
        });
        builder.Services.AddControllers();

        var app = builder.Build();

        await app.RunStartupTasksAsync();

        app.UseMiddleware<GlobalExceptionMiddleware>();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        await app.RunAsync();
    }
}
```

Notes:
- `AddWorkflowEngine()` extension is gone (it lived in the deleted `Extensions/` folder).
- OpenAPI/Scalar wiring is removed for now; Phase 12 reintroduces it when there is something to document.
- The host now boots empty — no engine, no executors, no controllers — but it boots.

- [ ] **Step 2: Build the host alone**

```powershell
dotnet build Hosts\Wbskt.Workflow.Engine.Host\Wbskt.Workflow.Engine.Host.csproj
```

Expected: succeeds.

- [ ] **Step 3: Smoke-run the host**

```powershell
dotnet run --project Hosts\Wbskt.Workflow.Engine.Host
```

Expected: starts, logs `Now listening on: https://localhost:7030`, no exceptions. `Ctrl+C` to stop.

---

### Task 0.4: Wipe the tests project source

**Files:**
- Delete folder: `Tests/Wbskt.Workflow.Engine.Host.Tests/Handlers`
- Delete folder: `Tests/Wbskt.Workflow.Engine.Host.Tests/Services`

- [ ] **Step 1: Delete the test source folders**

```powershell
Remove-Item -Recurse -Force Tests\Wbskt.Workflow.Engine.Host.Tests\Handlers,Tests\Wbskt.Workflow.Engine.Host.Tests\Services
```

- [ ] **Step 2: Confirm only the .csproj and obj/bin remain**

```powershell
Get-ChildItem Tests\Wbskt.Workflow.Engine.Host.Tests -Force | Where-Object { $_.Name -notmatch '^(obj|bin)$' } | ForEach-Object { $_.Name }
```

Expected:
```
Wbskt.Workflow.Engine.Host.Tests.csproj
```

- [ ] **Step 3: Run the empty test suite**

```powershell
dotnet test Tests\Wbskt.Workflow.Engine.Host.Tests --nologo --verbosity minimal
```

Expected: `Passed: 0, Failed: 0, Skipped: 0`. Build succeeds.

---

### Task 0.5: Wipe `Wbskt.Workflow` source

**Files:**
- Delete folder: `Wbskt.Workflow/Entities`
- Delete folder: `Wbskt.Workflow/Mappers`
- Delete folder: `Wbskt.Workflow/Providers`

- [ ] **Step 1: Delete folders**

```powershell
Remove-Item -Recurse -Force Wbskt.Workflow\Entities,Wbskt.Workflow\Mappers,Wbskt.Workflow\Providers
```

- [ ] **Step 2: Build the project**

```powershell
dotnet build Wbskt.Workflow\Wbskt.Workflow.csproj
```

Expected: succeeds (empty class library compiles fine).

---

### Task 0.6: Wipe `Wbskt.Workflow.Abstraction` source

**Files:**
- Delete folder: `Wbskt.Workflow.Abstraction/Enums`
- Delete folder: `Wbskt.Workflow.Abstraction/Models`

- [ ] **Step 1: Delete folders**

```powershell
Remove-Item -Recurse -Force Wbskt.Workflow.Abstraction\Enums,Wbskt.Workflow.Abstraction\Models
```

- [ ] **Step 2: Build the project**

```powershell
dotnet build Wbskt.Workflow.Abstraction\Wbskt.Workflow.Abstraction.csproj
```

Expected: succeeds.

---

### Task 0.7: Wipe `Wbskt.Workflow.Models` source

**Files:**
- Inspect: `Models/Wbskt.Workflow.Models/` (the earlier inventory pass found
  no `.cs` files; this task is defensive in case something exists locally).

- [ ] **Step 1: List existing source files**

```powershell
Get-ChildItem Models\Wbskt.Workflow.Models -Recurse -File -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } | ForEach-Object { $_.FullName }
```

- [ ] **Step 2: Delete any listed files**

If the previous step listed files, delete them (excluding `obj/`, `bin/`, and the `.csproj`):

```powershell
Get-ChildItem Models\Wbskt.Workflow.Models -Recurse -File -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } | Remove-Item -Force
```

- [ ] **Step 3: Build the project**

```powershell
dotnet build Models\Wbskt.Workflow.Models\Wbskt.Workflow.Models.csproj
```

Expected: succeeds.

---

### Task 0.8: Strip workflow contracts out of `Wbskt.Events`

**Files:**
- Delete: `Events/Wbskt.Events/Abstractions/IWorkflowContext.cs`
- Delete: `Events/Wbskt.Events/Management/WorkflowCreatedEvent.cs`
- Delete: `Events/Wbskt.Events/Management/WorkflowDeletedEvent.cs`
- Delete: `Events/Wbskt.Events/Management/WorkflowUpdatedEvent.cs`
- Delete folder: `Events/Wbskt.Events/WorkflowEngine`

- [ ] **Step 1: Delete the workflow-related event source files**

```powershell
Remove-Item -Force Events\Wbskt.Events\Abstractions\IWorkflowContext.cs
Remove-Item -Force Events\Wbskt.Events\Management\WorkflowCreatedEvent.cs,Events\Wbskt.Events\Management\WorkflowDeletedEvent.cs,Events\Wbskt.Events\Management\WorkflowUpdatedEvent.cs
Remove-Item -Recurse -Force Events\Wbskt.Events\WorkflowEngine
```

- [ ] **Step 2: Build `Wbskt.Events`**

```powershell
dotnet build Events\Wbskt.Events\Wbskt.Events.csproj
```

Expected: succeeds. If anything in `Wbskt.Events` references the deleted
types, fix the reference inline (most likely none — these were leaf event
contracts).

- [ ] **Step 3: Full solution build**

```powershell
dotnet build Wbskt.slnx
```

Expected: build either succeeds, OR fails only in `Wbskt.Management.Host` due to references to `WorkflowCreatedEvent` etc. Those references are removed in Task 0.9.

---

### Task 0.9: Remove workflow surface from Management Host

**Files:**
- Delete: `Hosts/Wbskt.Management.Host/Controllers/WorkflowsController.cs`
- Delete: `Hosts/Wbskt.Management.Host/Models/WorkflowModels.cs`
- Delete: `Hosts/Wbskt.Management.Host/Services/IWorkflowService.cs`
- Delete: `Hosts/Wbskt.Management.Host/Services/WorkflowService.cs`
- Modify: `Hosts/Wbskt.Management.Host/Program.cs` (remove `IWorkflowService` DI registration if present)
- Modify: any keyed `IReferenceMapper` registrations for `"Workflow"` in Program.cs

- [ ] **Step 1: Delete the four workflow files**

```powershell
Remove-Item -Force Hosts\Wbskt.Management.Host\Controllers\WorkflowsController.cs,Hosts\Wbskt.Management.Host\Models\WorkflowModels.cs,Hosts\Wbskt.Management.Host\Services\IWorkflowService.cs,Hosts\Wbskt.Management.Host\Services\WorkflowService.cs
```

- [ ] **Step 2: Find and remove DI registrations**

Search for residual `Workflow` references in Management Host:

```powershell
Select-String -Path Hosts\Wbskt.Management.Host\**\*.cs -Pattern 'Workflow' -SimpleMatch
```

For each hit in `Program.cs`, remove the line. Typical lines:
- `builder.Services.AddScoped<IWorkflowService, WorkflowService>();`
- `builder.Services.AddKeyedScoped<IReferenceMapper, ReferenceMapper<IWorkflowProvider>>("Workflow");`
- Any `using Wbskt.Workflow…` directives that are now orphaned.

- [ ] **Step 3: Build the Management Host**

```powershell
dotnet build Hosts\Wbskt.Management.Host\Wbskt.Management.Host.csproj
```

Expected: succeeds.

- [ ] **Step 4: Smoke-run Management Host**

```powershell
dotnet run --project Hosts\Wbskt.Management.Host
```

Expected: starts on https://localhost:7010 with no exceptions. `Ctrl+C` to stop.

---

### Task 0.10: Strip workflow SSDT objects from `Wbskt.Database`

**Files:**
- Delete: `Databases/Wbskt.Database/Tables/Workflows.sql`
- Delete: `Databases/Wbskt.Database/StoredProcedures/Workflow_Create.sql`
- Delete: `Databases/Wbskt.Database/StoredProcedures/Workflow_Delete.sql`
- Delete: `Databases/Wbskt.Database/StoredProcedures/Workflow_FindBy_RefId.sql`
- Delete: `Databases/Wbskt.Database/StoredProcedures/Workflow_GetAllBy_Workspace.sql`
- Delete: `Databases/Wbskt.Database/StoredProcedures/Workflow_GetAllEnabled.sql`
- Delete: `Databases/Wbskt.Database/StoredProcedures/Workflow_GetBy_Id.sql`
- Delete: `Databases/Wbskt.Database/StoredProcedures/Workflow_GetBy_RefId.sql`
- Delete: `Databases/Wbskt.Database/StoredProcedures/Workflow_Update.sql`

- [ ] **Step 1: Delete the SQL files**

```powershell
Remove-Item -Force Databases\Wbskt.Database\Tables\Workflows.sql
Remove-Item -Force Databases\Wbskt.Database\StoredProcedures\Workflow_*.sql
```

- [ ] **Step 2: Verify no other `.sql` references the dropped table**

```powershell
Select-String -Path Databases\Wbskt.Database\**\*.sql -Pattern 'Workflows' -SimpleMatch
```

Expected: no matches. If any pre/post deployment script seeds Workflow rows, remove that block.

- [ ] **Step 3: Build the SSDT project**

```powershell
dotnet build Databases\Wbskt.Database\Wbskt.Database.sqlproj
```

Expected: succeeds (DACPAC produced without the workflow objects).

- [ ] **Step 4: Re-deploy databases fresh against the dev SQL Edge container**

Ensure SQL Edge + RabbitMQ are running (see `Scripts.bat`). Then:

```powershell
pwsh ./Deploy-Databases.ps1 -Fresh
```

Expected: completes successfully; the resulting `Wbskt` database contains no `Workflows` table and no `Workflow_*` sprocs.

---

### Task 0.11: Full solution build + smoke-run all hosts

**Files:**
- No edits. Final verification only.

- [ ] **Step 1: Full solution build from a clean state**

```powershell
dotnet build Wbskt.slnx --no-incremental
```

Expected: success across all projects. If any project still references a deleted type, fix the dangling reference in the smallest possible edit.

- [ ] **Step 2: Run the empty tests project**

```powershell
dotnet test Tests\Wbskt.Workflow.Engine.Host.Tests --nologo --verbosity minimal
```

Expected: `Passed: 0, Failed: 0`.

- [ ] **Step 3: Boot all four hosts via the compound config**

Use Rider's compound configuration `.run/RunAll.run.xml`, or run each host in a separate terminal:

```powershell
# Terminal 1
dotnet run --project Hosts\Wbskt.Auth.Host
# Terminal 2
dotnet run --project Hosts\Wbskt.Management.Host
# Terminal 3
dotnet run --project Hosts\Wbskt.Socket.Host
# Terminal 4
dotnet run --project Hosts\Wbskt.Workflow.Engine.Host
```

Expected: all four hosts start without exceptions and bind to their dev ports
(7000, 7010, 7020, 7030).

- [ ] **Step 4: Sanity grep for orphaned workflow references**

```powershell
git --no-pager grep -i -l 'WorkflowService\|WorkflowProvider\|IWorkflowEngine\|WorkflowRuntime\|WorkflowExpressionEvaluator\|ExecutionPointer\|WorkflowInstance' -- '*.cs'
```

Expected: no hits. Anything that returns is dead V1/V2 code that the previous tasks missed — delete it.

---

### Task 0.12: Commit the clean slate

**Files:**
- No edits. Single commit gathering Tasks 0.2 – 0.11.

- [ ] **Step 1: Stage and review the diff**

```powershell
git add -A
git --no-pager status
git --no-pager diff --stat --cached
```

Expected diff: a large `-` line count across the seven wiped folders, the four
Management Host files, the nine workflow SQL files, the workflow event
contracts, and the rewritten `Hosts/Wbskt.Workflow.Engine.Host/Program.cs`.
Only `+` lines should be in the rewritten `Program.cs`.

- [ ] **Step 2: Commit**

```powershell
git commit -m "(chore): wipe V1 and V2 workflow implementation

Phase 0 of the workflow engine rebuild (Docs/Plans/2026-05-26-workflow-engine.md).
Deletes the V1 engine host source, the V2 scaffolding folder, the
Wbskt.Workflow provider library, the Wbskt.Workflow.Abstraction model layer,
the workflow tests project source, the workflow tables and stored procedures
in Wbskt.Database, the workflow event contracts in Wbskt.Events, and the
workflow controller/service in Wbskt.Management.Host. Reduces the engine host
Program.cs to a minimal ASP.NET Core skeleton. Project shells, tests project
shell, and database project shell are retained for the rebuild that follows
in Phases 1+.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

- [ ] **Step 3: Verify clean tree and final build**

```powershell
git --no-pager status
dotnet build Wbskt.slnx
```

Expected: working tree clean; solution builds.

---

**Phase 0 acceptance:**

- [ ] `dotnet build Wbskt.slnx` succeeds.
- [ ] `dotnet test Tests/Wbskt.Workflow.Engine.Host.Tests` runs with zero tests, zero failures.
- [ ] `pwsh ./Deploy-Databases.ps1 -Fresh` succeeds; `Wbskt` database contains no workflow-related objects.
- [ ] All four hosts boot.
- [ ] `git --no-pager grep -i -l 'WorkflowProvider\|IWorkflowEngine\|WorkflowRuntime'` returns no source matches (matches in `Docs/` are fine).
- [ ] One commit on `feat/workflow-engine` representing the clean slate.

---

<!-- PHASES 1 — 13 TO BE DRAFTED -->

## Phase 1 — Database Schema

**Goal:** Add the eleven new SSDT tables (spec §6.1) plus their stored
procedures to `Databases/Wbskt.Database`. Each task creates the table file
*first*, then the sprocs that consume it, then redeploys with
`Deploy-Databases.ps1 -Fresh` to prove the SSDT publish is clean.

Reference: spec §6.1 (table inventory), §6.2 (Branch row shape), §6.4
(IdempotencyKey shape), §3.1 (Bookmark shape), §4.4–4.6 (TriggerRegistration
/ PendingTriggerEvents), §4.3 (ScheduledFires).

Conventions reminder: SQL keywords UPPERCASE; always `dbo.`; one column per
`SELECT` line; never `SELECT *`; `CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME()`
on every new table. Stored proc naming: `Entity_Action_Modifier`.

After every task in this phase:

```powershell
dotnet build Databases\Wbskt.Database\Wbskt.Database.sqlproj
pwsh ./Deploy-Databases.ps1 -Fresh
```

Expected: both succeed and the new objects exist in the deployed DB.

---

### Task 1.1: `WorkflowDefinitions` table

**Files:**
- Create: `Databases/Wbskt.Database/Tables/WorkflowDefinitions.sql`

- [ ] **Step 1: Create the table file**

```sql
CREATE TABLE dbo.WorkflowDefinitions
(
    Id              INT             NOT NULL IDENTITY(1,1) PRIMARY KEY,
    RefId           UNIQUEIDENTIFIER NOT NULL,
    Version         INT             NOT NULL,
    WorkspaceId     INT             NOT NULL,
    Name            NVARCHAR(200)   NOT NULL,
    Description     NVARCHAR(2000)  NULL,
    IsEnabled       BIT             NOT NULL DEFAULT 1,
    DefinitionJson  NVARCHAR(MAX)   NOT NULL,
    PublishedBy     INT             NOT NULL,
    CreatedAt       DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_WorkflowDefinitions_RefId_Version UNIQUE (RefId, Version)
);
GO

CREATE INDEX IX_WorkflowDefinitions_WorkspaceId_Enabled
    ON dbo.WorkflowDefinitions (WorkspaceId, IsEnabled);
GO
```

- [ ] **Step 2: Build SSDT + deploy fresh**

```powershell
dotnet build Databases\Wbskt.Database\Wbskt.Database.sqlproj
pwsh ./Deploy-Databases.ps1 -Fresh
```

- [ ] **Step 3: Verify table created**

```powershell
sqlcmd -S localhost,14330 -U sa -P 'YourStrong!Passw0rd' -d Wbskt -Q "SELECT TOP 1 name FROM sys.tables WHERE name = 'WorkflowDefinitions';"
```

Expected: returns `WorkflowDefinitions`.

- [ ] **Step 4: Commit**

```powershell
git add Databases\Wbskt.Database\Tables\WorkflowDefinitions.sql
git commit -m "(db): add WorkflowDefinitions table

Spec §6.1 / §1.5.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 1.2: `WorkflowDefinitions` stored procedures

**Files:**
- Create: `Databases/Wbskt.Database/StoredProcedures/WorkflowDefinition_Publish.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/WorkflowDefinition_GetBy_RefId_Version.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/WorkflowDefinition_GetLatestVersion_By_RefId.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/WorkflowDefinition_GetAllEnabled.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/WorkflowDefinition_FindBy_RefId.sql`

- [ ] **Step 1: Create `WorkflowDefinition_Publish.sql`**

Inserts a new row computing the next version. Inputs:
`@RefId, @WorkspaceId, @Name, @Description, @IsEnabled, @DefinitionJson, @PublishedBy`.
Output: `SELECT` of the inserted row (entire columns, one per line).

```sql
CREATE PROCEDURE dbo.WorkflowDefinition_Publish
    @RefId          UNIQUEIDENTIFIER,
    @WorkspaceId    INT,
    @Name           NVARCHAR(200),
    @Description    NVARCHAR(2000),
    @IsEnabled      BIT,
    @DefinitionJson NVARCHAR(MAX),
    @PublishedBy    INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NextVersion INT = ISNULL((SELECT MAX(Version) FROM dbo.WorkflowDefinitions WHERE RefId = @RefId), 0) + 1;

    INSERT INTO dbo.WorkflowDefinitions (RefId, Version, WorkspaceId, Name, Description, IsEnabled, DefinitionJson, PublishedBy)
    VALUES (@RefId, @NextVersion, @WorkspaceId, @Name, @Description, @IsEnabled, @DefinitionJson, @PublishedBy);

    SELECT
        Id,
        RefId,
        Version,
        WorkspaceId,
        Name,
        Description,
        IsEnabled,
        DefinitionJson,
        PublishedBy,
        CreatedAt
    FROM dbo.WorkflowDefinitions
    WHERE RefId = @RefId AND Version = @NextVersion;
END;
GO
```

- [ ] **Step 2: Create the four read procs**

`WorkflowDefinition_GetBy_RefId_Version` — returns the single row for `(RefId, Version)`.
`WorkflowDefinition_GetLatestVersion_By_RefId` — returns the row with the highest `Version` for `RefId`.
`WorkflowDefinition_GetAllEnabled` — returns the latest enabled version per `RefId`, scoped to a `@WorkspaceId`.
`WorkflowDefinition_FindBy_RefId` — returns `SELECT TOP 1 Id` for the latest version (per Reference Mapper Pattern §"FindBy returns int").

Each follows the per-column SELECT convention. (See spec §6.1 table for the column list, identical to the table definition above.)

- [ ] **Step 3: Build + deploy + verify**

```powershell
dotnet build Databases\Wbskt.Database\Wbskt.Database.sqlproj
pwsh ./Deploy-Databases.ps1 -Fresh
sqlcmd -S localhost,14330 -U sa -P 'YourStrong!Passw0rd' -d Wbskt -Q "SELECT name FROM sys.procedures WHERE name LIKE 'WorkflowDefinition_%' ORDER BY name;"
```

Expected: five proc names returned.

- [ ] **Step 4: Commit** (`(db): add WorkflowDefinition stored procedures`)

---

### Task 1.3: `TriggerRegistrations` table

**Files:**
- Create: `Databases/Wbskt.Database/Tables/TriggerRegistrations.sql`

Spec §4.1 / §4.2: one row per *trigger node* per *published version*, indexed by `TriggerKey` for the hot lookup path.

- [ ] **Step 1: Create the table**

```sql
CREATE TABLE dbo.TriggerRegistrations
(
    Id                      INT             NOT NULL IDENTITY(1,1) PRIMARY KEY,
    WorkflowDefinitionId    INT             NOT NULL,
    WorkflowRefId           UNIQUEIDENTIFIER NOT NULL,
    WorkflowVersion         INT             NOT NULL,
    TriggerNodeId           UNIQUEIDENTIFIER NOT NULL,
    TriggerKind             NVARCHAR(64)    NOT NULL,
    TriggerKey              NVARCHAR(400)   NOT NULL,
    CorrelationExpression   NVARCHAR(1000)  NULL,
    ConcurrencyPolicy       NVARCHAR(32)    NOT NULL DEFAULT 'Queue',
    FilterExpression        NVARCHAR(2000)  NULL,
    CreatedAt               DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_TriggerRegistrations_WorkflowDefinitions
        FOREIGN KEY (WorkflowDefinitionId) REFERENCES dbo.WorkflowDefinitions (Id)
);
GO

CREATE INDEX IX_TriggerRegistrations_TriggerKey
    ON dbo.TriggerRegistrations (TriggerKey)
    INCLUDE (WorkflowDefinitionId, TriggerNodeId, ConcurrencyPolicy);
GO
CREATE INDEX IX_TriggerRegistrations_WorkflowDefinitionId
    ON dbo.TriggerRegistrations (WorkflowDefinitionId);
GO
```

- [ ] **Step 2: Build + deploy + commit** (`(db): add TriggerRegistrations table`)

---

### Task 1.4: `TriggerRegistrations` stored procedures

**Files:**
- Create: `Databases/Wbskt.Database/StoredProcedures/TriggerRegistration_Insert.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/TriggerRegistration_GetBy_TriggerKey.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/TriggerRegistration_GetAllBy_WorkflowDefinitionId.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/TriggerRegistration_DeleteAllBy_WorkflowDefinitionId.sql`

- [ ] **Step 1: Write all four sprocs** with the standard skeleton.

`TriggerRegistration_Insert` takes the full input row; returns the inserted row.
`TriggerRegistration_GetBy_TriggerKey` returns 0..N rows for a `@TriggerKey` (multiple workflows can share a key).
`TriggerRegistration_GetAllBy_WorkflowDefinitionId` returns all trigger registrations for a definition.
`TriggerRegistration_DeleteAllBy_WorkflowDefinitionId` removes registrations when a definition is unpublished/superseded (spec §6.5).

- [ ] **Step 2: Build + deploy + verify + commit** (`(db): add TriggerRegistration stored procedures`)

---

### Task 1.5: `Runs` and `RunCounters` tables + sprocs

**Files:**
- Create: `Databases/Wbskt.Database/Tables/Runs.sql`
- Create: `Databases/Wbskt.Database/Tables/RunCounters.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Run_Create.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Run_GetBy_RefId.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Run_FindBy_RefId.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Run_GetActiveBy_WorkflowRefId_CorrelationKey.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Run_UpdateStatus.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/RunCounters_IncrementActiveBranches.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/RunCounters_DecrementActiveBranches.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/RunCounters_AddCreditsConsumed.sql`

Spec §6.1: split rationale — hot counter increments must not contend with cold status updates.

- [ ] **Step 1: Create `Runs` table**

Columns: `Id`, `RefId UNIQUEIDENTIFIER`, `WorkflowDefinitionId`, `WorkflowRefId`, `WorkflowVersion`, `TriggerNodeId`, `CorrelationKey NVARCHAR(400) NULL`, `Status NVARCHAR(32) NOT NULL` (initial = `Running`), `StartedAt`, `CompletedAt NULL`, `CancellationRequestedAt NULL`, `CancellationReason NVARCHAR(500) NULL`, `CreditBudget DECIMAL(18,4)`, `CreatedAt`. Index on `(WorkflowRefId, CorrelationKey)` with filter `Status IN ('Running','Failing','Cancelling')`.

- [ ] **Step 2: Create `RunCounters` table**

Columns: `RunId INT NOT NULL PRIMARY KEY`, `ActiveBranchCount INT NOT NULL DEFAULT 0`, `CreditsConsumed DECIMAL(18,4) NOT NULL DEFAULT 0`, `UpdatedAt`. FK to `Runs(Id)`. No `CreatedAt` here — owned by `Runs`.

- [ ] **Step 3: `RunCounters_IncrementActiveBranches`**

```sql
CREATE PROCEDURE dbo.RunCounters_IncrementActiveBranches
    @RunId INT,
    @Delta INT = 1
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.RunCounters
       SET ActiveBranchCount = ActiveBranchCount + @Delta,
           UpdatedAt = SYSUTCDATETIME()
    OUTPUT inserted.ActiveBranchCount
     WHERE RunId = @RunId;
END;
GO
```

`RunCounters_DecrementActiveBranches` mirrors the above with `@Delta` defaulted to `1` and subtraction. The atomic OUTPUT clause decides the "who completes last" race per spec §2.15.

`RunCounters_AddCreditsConsumed` adds `@Cost DECIMAL(18,4)` and returns the new running total so the caller can check budget overflow per spec §5.2.

- [ ] **Step 4: `Run_Create`, `Run_GetBy_RefId`, `Run_FindBy_RefId`, `Run_GetActiveBy_WorkflowRefId_CorrelationKey`, `Run_UpdateStatus`** — standard skeletons. `Run_Create` inserts both the `Runs` row and the matching `RunCounters` row inside a single transaction.

- [ ] **Step 5: Build + deploy + commit** (`(db): add Runs and RunCounters tables with sprocs`)

---

### Task 1.6: `Branches` table + sprocs

**Files:**
- Create: `Databases/Wbskt.Database/Tables/Branches.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Branch_Upsert.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Branch_GetBy_RefId.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Branch_GetAllBy_RunId.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Branch_GetActiveBy_RunId.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Branch_GetAllActive.sql`

Spec §6.2: per-branch snapshot row. Schema matches the spec verbatim.

- [ ] **Step 1: Create `Branches` table**

```sql
CREATE TABLE dbo.Branches
(
    Id                  INT             NOT NULL IDENTITY(1,1) PRIMARY KEY,
    RefId               UNIQUEIDENTIFIER NOT NULL,
    RunId               INT             NOT NULL,
    ParentBranchId      UNIQUEIDENTIFIER NULL,
    ForkCohortId        UNIQUEIDENTIFIER NULL,
    NodeId              UNIQUEIDENTIFIER NOT NULL,
    Status              NVARCHAR(32)    NOT NULL,
    PendingTakePort     NVARCHAR(128)   NULL,
    LocalJson           NVARCHAR(MAX)   NOT NULL DEFAULT N'{}',
    LastOutputJson      NVARCHAR(MAX)   NULL,
    CompensationStackJson NVARCHAR(MAX) NULL,
    CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    RowVersion          ROWVERSION      NOT NULL,
    CONSTRAINT UQ_Branches_RefId UNIQUE (RefId),
    CONSTRAINT FK_Branches_Runs FOREIGN KEY (RunId) REFERENCES dbo.Runs (Id)
);
GO
CREATE INDEX IX_Branches_RunId          ON dbo.Branches (RunId);
GO
CREATE INDEX IX_Branches_RunId_Status   ON dbo.Branches (RunId, Status);
GO
CREATE INDEX IX_Branches_Status_Active  ON dbo.Branches (Status) WHERE Status = 'Active';
GO
```

- [ ] **Step 2: `Branch_Upsert`** — MERGE on `RefId`, sets all snapshot columns, updates `UpdatedAt`. This is the hot path called after every node returns (spec §2.10).

- [ ] **Step 3: Read sprocs** — by branch RefId, by RunId, by RunId + active status, and a global active list used by `StartupRecoveryService` (spec §7.6).

- [ ] **Step 4: Build + deploy + commit** (`(db): add Branches table and snapshot sprocs`)

---

### Task 1.7: `Bookmarks` table + sprocs

**Files:**
- Create: `Databases/Wbskt.Database/Tables/Bookmarks.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Bookmark_Create.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Bookmark_GetBy_RefId.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Bookmark_GetAllBy_MatchKey.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Bookmark_GetAllBy_RunId.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Bookmark_GetDue.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Bookmark_Delete.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/Bookmark_DeleteAllBy_RunId.sql`

Spec §3.1: bookmark = `(RefId, RunId, BranchId, NodeId, WakeConditionKind, MatchKey, WakeConditionJson, ExpiresAt?, TtlPort?, CreatedAt)`. `MatchKey` is the index column for `MatchInbound` (spec §3.4.1).

- [ ] **Step 1: Create table** with columns above. Indexes:
  - `IX_Bookmarks_MatchKey (MatchKey) INCLUDE (RunId, BranchId, NodeId, WakeConditionKind)`
  - `IX_Bookmarks_ExpiresAt (ExpiresAt) WHERE ExpiresAt IS NOT NULL`
  - `IX_Bookmarks_RunId (RunId)`

- [ ] **Step 2: Sprocs**. `Bookmark_GetDue` returns the next batch of bookmarks where `ExpiresAt <= @Now`, used by `BookmarkScheduler` (§3.3) with `READPAST` + `UPDLOCK` hints inside the SELECT for lease behavior.

- [ ] **Step 3: Build + deploy + commit** (`(db): add Bookmarks table and sprocs`)

---

### Task 1.8: `HistoryEvents` table + sprocs

**Files:**
- Create: `Databases/Wbskt.Database/Tables/HistoryEvents.sql`
- Create: `Databases/Wbskt.Database/Types/UserDefinedTableTypes/HistoryEventTableType.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/HistoryEvent_InsertBatch.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/HistoryEvent_GetBy_RunId.sql`

Spec §6.3: append-only, clustered on `(RunId, HistoryEventId)` for per-run locality. `HistoryEventId` is an identity column.

- [ ] **Step 1: Create table**

```sql
CREATE TABLE dbo.HistoryEvents
(
    HistoryEventId  BIGINT          NOT NULL IDENTITY(1,1),
    RunId           INT             NOT NULL,
    BranchRefId     UNIQUEIDENTIFIER NULL,
    NodeId          UNIQUEIDENTIFIER NULL,
    EventKind       NVARCHAR(64)    NOT NULL,
    Severity        NVARCHAR(16)    NOT NULL,
    PayloadJson     NVARCHAR(MAX)   NULL,
    Timestamp       DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_HistoryEvents PRIMARY KEY CLUSTERED (RunId, HistoryEventId)
);
GO
CREATE INDEX IX_HistoryEvents_HistoryEventId ON dbo.HistoryEvents (HistoryEventId);
GO
CREATE INDEX IX_HistoryEvents_Timestamp_Severity ON dbo.HistoryEvents (Timestamp, Severity);
GO
```

- [ ] **Step 2: Create TVP `HistoryEventTableType`** — mirrors the row shape without `HistoryEventId`. Used for batched inserts by the `HistoryEventFlusher` (spec §7.3).

- [ ] **Step 3: `HistoryEvent_InsertBatch`** — takes a `@Events HistoryEventTableType READONLY` and inserts via `INSERT…SELECT FROM @Events`.

- [ ] **Step 4: `HistoryEvent_GetBy_RunId`** — paged by `@AfterEventId` for cursor-style reads (spec §6.7 replay reader).

- [ ] **Step 5: Build + deploy + commit** (`(db): add HistoryEvents table, TVP and sprocs`)

---

### Task 1.9: `IdempotencyKeys` table + sprocs

**Files:**
- Create: `Databases/Wbskt.Database/Tables/IdempotencyKeys.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/IdempotencyKey_Upsert_Pending.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/IdempotencyKey_MarkSucceeded.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/IdempotencyKey_MarkFailed.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/IdempotencyKey_GetBy_Key.sql`

Spec §6.4: one row per `(RunId, BranchRefId, NodeId, Attempt)`. Statuses `Pending | Succeeded | Failed`. `Succeeded` rows cache the result for fast skip-on-replay.

- [ ] **Step 1: Create table**

```sql
CREATE TABLE dbo.IdempotencyKeys
(
    Id                  INT             NOT NULL IDENTITY(1,1) PRIMARY KEY,
    KeyValue            NVARCHAR(200)   NOT NULL,
    RunId               INT             NOT NULL,
    BranchRefId         UNIQUEIDENTIFIER NOT NULL,
    NodeId              UNIQUEIDENTIFIER NOT NULL,
    Attempt             INT             NOT NULL,
    Status              NVARCHAR(16)    NOT NULL,
    ResultJson          NVARCHAR(MAX)   NULL,
    ErrorJson           NVARCHAR(MAX)   NULL,
    CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CompletedAt         DATETIME2(3)    NULL,
    CONSTRAINT UQ_IdempotencyKeys_KeyValue UNIQUE (KeyValue)
);
GO
CREATE INDEX IX_IdempotencyKeys_RunId ON dbo.IdempotencyKeys (RunId);
GO
```

- [ ] **Step 2: `IdempotencyKey_Upsert_Pending`** — INSERTs a Pending row, or returns the existing row if `KeyValue` already exists (spec §6.4 "before-invoke for side-effecting").

- [ ] **Step 3: Mark sprocs** — `MarkSucceeded(@KeyValue, @ResultJson)` and `MarkFailed(@KeyValue, @ErrorJson)`.

- [ ] **Step 4: Build + deploy + commit** (`(db): add IdempotencyKeys table and sprocs`)

---

### Task 1.10: `SharedVariables` table + sprocs

**Files:**
- Create: `Databases/Wbskt.Database/Tables/SharedVariables.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/SharedVariable_Initialize.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/SharedVariable_Increment.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/SharedVariable_Decrement.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/SharedVariable_GetBy_WorkflowRefId_Name.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/SharedVariable_Set.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/SharedVariable_CompareAndSet.sql`

Spec §2.14 / §1.7: workflow-scoped, one row per `(WorkflowRefId, VarName)`. Atomic ops translate to single SQL `UPDATE` statements.

- [ ] **Step 1: Create table** with columns `Id`, `WorkflowRefId`, `VarName NVARCHAR(100)`, `VarType NVARCHAR(16)`, `ValueJson NVARCHAR(MAX)`, `UpdatedAt`, `CreatedAt`. Unique on `(WorkflowRefId, VarName)`.

- [ ] **Step 2: `SharedVariable_Increment`** — `UPDATE … SET ValueJson = CAST(CAST(ValueJson AS BIGINT) + @Delta AS NVARCHAR(MAX)) OUTPUT inserted.ValueJson WHERE WorkflowRefId = @W AND VarName = @V AND VarType = 'Counter'`. Returns the new value.

- [ ] **Step 3: `SharedVariable_CompareAndSet`** — conditional UPDATE with `WHERE … AND ValueJson = @Expected`; returns rowcount via `OUTPUT` or `@@ROWCOUNT` to signal swap-success.

- [ ] **Step 4: remaining sprocs** + build + deploy + commit (`(db): add SharedVariables table and atomic sprocs`)

---

### Task 1.11: `PendingTriggerEvents` table + sprocs

**Files:**
- Create: `Databases/Wbskt.Database/Tables/PendingTriggerEvents.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/PendingTriggerEvent_Enqueue.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/PendingTriggerEvent_DequeueNext.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/PendingTriggerEvent_DeleteAllBy_RunKey.sql`

Spec §4.6: `Queue`-policy backlog. One row = one inbound event waiting for a running Run with the same correlation key to terminate.

- [ ] **Step 1: Create table** with columns `Id`, `WorkflowRefId`, `TriggerNodeId`, `CorrelationKey NVARCHAR(400)`, `InboundEventJson NVARCHAR(MAX)`, `EnqueuedAt`, `CreatedAt`. Index `(WorkflowRefId, TriggerNodeId, CorrelationKey, EnqueuedAt)`.

- [ ] **Step 2: `PendingTriggerEvent_DequeueNext`** — `SELECT TOP 1 …` ordered by `EnqueuedAt` filtered by run-key with `READPAST, UPDLOCK` hint, then `DELETE OUTPUT` so the dispatcher leases-and-removes atomically.

- [ ] **Step 3: Build + deploy + commit** (`(db): add PendingTriggerEvents table and sprocs`)

---

### Task 1.12: `ScheduledFires` table + sprocs

**Files:**
- Create: `Databases/Wbskt.Database/Tables/ScheduledFires.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/ScheduledFire_Insert.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/ScheduledFire_LeaseDue.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/ScheduledFire_AdvanceNext.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/ScheduledFire_DeleteAllBy_WorkflowDefinitionId.sql`

Spec §4.3: backing for `trigger:schedule` and `wait-for-timer` bookmarks Ticker adapter.

- [ ] **Step 1: Create table** with columns `Id`, `TriggerNodeId UNIQUEIDENTIFIER`, `WorkflowDefinitionId`, `WorkflowRefId`, `CronOrInterval NVARCHAR(200)`, `NextFireAt DATETIME2(3)`, `LeasedUntil DATETIME2(3) NULL`, `CreatedAt`. Index on `NextFireAt`.

- [ ] **Step 2: `ScheduledFire_LeaseDue`** — `UPDATE TOP (@Batch) … WITH (UPDLOCK, READPAST) SET LeasedUntil = DATEADD(SECOND, @LeaseSec, SYSUTCDATETIME()) OUTPUT inserted.* WHERE NextFireAt <= SYSUTCDATETIME() AND (LeasedUntil IS NULL OR LeasedUntil < SYSUTCDATETIME())`.

- [ ] **Step 3: `ScheduledFire_AdvanceNext`** — clears `LeasedUntil` and sets `NextFireAt` to the next computed-by-caller value.

- [ ] **Step 4: Build + deploy + commit** (`(db): add ScheduledFires table and sprocs`)

---

### Task 1.13: Verify the full schema + final smoke deploy

**Files:**
- No edits.

- [ ] **Step 1: Fresh deploy from a clean container**

```powershell
docker restart wbskt-sql # if needed
pwsh ./Deploy-Databases.ps1 -Fresh
```

- [ ] **Step 2: Verify all 11 tables exist**

```powershell
sqlcmd -S localhost,14330 -U sa -P 'YourStrong!Passw0rd' -d Wbskt -Q "SELECT name FROM sys.tables WHERE name IN ('WorkflowDefinitions','TriggerRegistrations','Runs','RunCounters','Branches','Bookmarks','HistoryEvents','IdempotencyKeys','SharedVariables','PendingTriggerEvents','ScheduledFires') ORDER BY name;"
```

Expected: 11 rows.

- [ ] **Step 3: Verify sproc counts per entity**

```powershell
sqlcmd -S localhost,14330 -U sa -P 'YourStrong!Passw0rd' -d Wbskt -Q "SELECT LEFT(name, CHARINDEX('_', name) - 1) AS Entity, COUNT(*) AS ProcCount FROM sys.procedures WHERE name LIKE '%[_]%' AND name NOT LIKE 'Client[_]%' AND name NOT LIKE 'RegistrationPolicy[_]%' AND name NOT LIKE 'Event%' GROUP BY LEFT(name, CHARINDEX('_', name) - 1) ORDER BY Entity;"
```

Expected: counts roughly matching tasks 1.2 / 1.4 / 1.5 / 1.6 / 1.7 / 1.8 / 1.9 / 1.10 / 1.11 / 1.12.

- [ ] **Step 4: No commit** (acceptance step only).

---

**Phase 1 acceptance:**

- [ ] All eleven tables and their indexes exist after `Deploy-Databases.ps1 -Fresh`.
- [ ] All stored procedures from Tasks 1.2 through 1.12 deploy without errors.
- [ ] `dotnet build Wbskt.slnx` still passes.
- [ ] Twelve commits on `feat/workflow-engine` covering the schema (one per task except 1.5 which combines `Runs`+`RunCounters`, and 1.13 which is verify-only).

---

## Phase 2 — Authoring Layer

**Goal:** Recreate `Wbskt.Workflow.Abstraction` as a pure data layer matching
spec §1. No behavior, no DB access — just records, enums, and a single
`WorkflowValidator` that enforces the §1.8 hard errors.

Every task in this phase is strict TDD:
1. Write the failing xUnit test in `Tests/Wbskt.Workflow.Engine.Host.Tests`.
2. Run, watch it fail.
3. Implement the minimum to pass.
4. Run, watch it pass.
5. Commit.

The tests project will accumulate authoring tests under
`Tests/Wbskt.Workflow.Engine.Host.Tests/Abstraction/`. Add a project
reference from the tests project to `Wbskt.Workflow.Abstraction` in Task 2.1
if one doesn't already exist.

JSON polymorphism (used by `BaseNode`, `WorkflowExpression`, `WakeCondition`):
use `System.Text.Json` `[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]` +
`[JsonDerivedType(typeof(Foo), "foo")]` (built into .NET 10). No third-party
serializer.

---

### Task 2.1: `PortDirection`, `PortDefinition`, project reference

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Enums/PortDirection.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/PortDefinition.cs`
- Create: `Tests/Wbskt.Workflow.Engine.Host.Tests/Abstraction/PortDefinitionTests.cs`
- Modify: `Tests/Wbskt.Workflow.Engine.Host.Tests/Wbskt.Workflow.Engine.Host.Tests.csproj` (add project reference to `Wbskt.Workflow.Abstraction`)

- [ ] **Step 1: Write failing test**

```csharp
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

public class PortDefinitionTests
{
    [Fact]
    public void Port_serialises_to_json_with_id_direction_and_label()
    {
        var port = new PortDefinition("true", PortDirection.Output, "True branch");

        var json = System.Text.Json.JsonSerializer.Serialize(port);

        Assert.Contains("\"portId\":\"true\"", json);
        Assert.Contains("\"direction\":\"Output\"", json);
        Assert.Contains("\"label\":\"True branch\"", json);
    }
}
```

- [ ] **Step 2: Run** — expect FAIL (`PortDefinition` and `PortDirection` don't exist).

```powershell
dotnet test Tests\Wbskt.Workflow.Engine.Host.Tests --nologo --verbosity minimal
```

- [ ] **Step 3: Implement**

`PortDirection.cs`:
```csharp
namespace Wbskt.Workflow.Abstraction.Enums;

public enum PortDirection
{
    Input,
    Output
}
```

`PortDefinition.cs`:
```csharp
using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models;

public sealed record PortDefinition(
    [property: JsonPropertyName("portId")] string PortId,
    [property: JsonPropertyName("direction")][property: JsonConverter(typeof(JsonStringEnumConverter))] PortDirection Direction,
    [property: JsonPropertyName("label")] string Label
);
```

- [ ] **Step 4: Run** — expect PASS.

- [ ] **Step 5: Commit** (`(feat): authoring layer PortDefinition + PortDirection`)

---

### Task 2.2: `Edge`

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Models/Edge.cs`
- Create: `Tests/Wbskt.Workflow.Engine.Host.Tests/Abstraction/EdgeTests.cs`

Spec §1.3: `(sourceNodeId, sourcePortId) → (targetNodeId, targetPortId)`.

- [ ] **Step 1: Test** — round-trip a sample JSON `{"from":["A","out"],"to":["B","in"]}` through `JsonSerializer.Deserialize<Edge>()` and back; assert equality.

- [ ] **Step 2–5:** Implement as `sealed record Edge((Guid NodeId, string PortId) From, (Guid NodeId, string PortId) To)` with a custom converter that maps the two-element JSON arrays to/from the tuples. Run, commit (`(feat): authoring layer Edge`).

---

### Task 2.3: `BaseNode` + polymorphic subtypes

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/BaseNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/NodeKind.cs`
- Create: `Tests/Wbskt.Workflow.Engine.Host.Tests/Abstraction/BaseNodeTests.cs`

Spec §1.1 / §1.6. `BaseNode` carries `NodeId`, `Kind`, `Name`, `Ports`; subtypes add `Config`. Use `[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]` on `BaseNode`.

- [ ] **Step 1: Test** — deserialize a JSON document with `"kind":"control:logic"` and assert it round-trips to a `LogicGateNode` subtype. The test will fail until both `BaseNode` and at least one subtype exist; add a stub `LogicGateNode` placeholder in this task to satisfy the polymorphism wiring (full config types come in later tasks).

- [ ] **Step 2–5:** Implement. `NodeKind` is a `static class` with `public const string` strings for each kind from spec §1.6 table (`Trigger.Device`, `Trigger.Schedule`, `Trigger.Webhook`, `Trigger.Manual`, `Control.Logic`, `Control.ForEach`, `Control.ParallelForEach`, `Control.Join`, `Control.Delay`, `Control.Variable`, `Control.SubWorkflow`, `Control.WaitForHttp`, `Control.AwaitSignal`, `Action.Command`, `Action.Email`, `Action.Webhook`, `Action.Telegram`, `Action.Toast`, `Control.FailRun`). Run, commit (`(feat): authoring layer BaseNode polymorphic root + NodeKind catalog`).

---

### Task 2.4: `WorkflowExpression` polymorphic hierarchy

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Models/Expressions/WorkflowExpression.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Expressions/LiteralExpression.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Expressions/MemberAccessExpression.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Expressions/FunctionExpression.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Expressions/BinaryExpression.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Expressions/UnaryExpression.cs`
- Create: `Wbskt.Workflow.Abstraction/Enums/BinaryOperator.cs`
- Create: `Wbskt.Workflow.Abstraction/Enums/UnaryOperator.cs`
- Create: `Wbskt.Workflow.Abstraction/Enums/FunctionName.cs`
- Create: `Tests/Wbskt.Workflow.Engine.Host.Tests/Abstraction/WorkflowExpressionTests.cs`

Used by §1.10 `condition` and §4.5 `correlationKey`.

- [ ] **Step 1: Test** — round-trip `"$trigger.temperature > 35"` parsed as a nested `BinaryExpression(MemberAccess("$trigger","temperature"), GreaterThan, Literal(35))`.

- [ ] **Step 2–5:** Implement records + JsonPolymorphic on `WorkflowExpression`. Run, commit.

---

### Task 2.5: `RetryPolicy`, `RetryStrategy`, `RetryFilter`

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Models/RetryPolicy.cs`
- Create: `Wbskt.Workflow.Abstraction/Enums/RetryStrategy.cs`
- Create: `Tests/Wbskt.Workflow.Engine.Host.Tests/Abstraction/RetryPolicyTests.cs`

Spec §5.2.

- [ ] **Step 1: Test** — deserialize `{"strategy":"Exponential","initialDelay":"00:00:01","factor":2.0,"maxDelay":"00:00:30","maxAttempts":5,"jitterPct":10,"retryOn":["Timeout","ServiceUnavailable"]}` and assert each field.

- [ ] **Step 2–5:** Implement. `RetryStrategy: None | Linear | Exponential`. `RetryPolicy` is a record with `Strategy`, `InitialDelay TimeSpan`, `Factor double?`, `MaxDelay TimeSpan?`, `MaxAttempts int`, `JitterPct int`, `RetryOn IReadOnlyCollection<string>`. Commit.

---

### Task 2.6: `OnFailureConfig`, `ErrorOutcome`, `CompensationDeclaration`

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Enums/ErrorOutcome.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/OnFailureConfig.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/CompensationDeclaration.cs`
- Create: `Tests/Wbskt.Workflow.Engine.Host.Tests/Abstraction/OnFailureConfigTests.cs`

Spec §5.3 / §5.5.

- [ ] **Step 1: Test** — round-trip `{"outcome":"FailRun","compensate":{"steps":[{"nodeId":"...","kind":"action:command",…}]}}`.

- [ ] **Step 2–5:** Implement. `ErrorOutcome: ContinueOnError | FailBranch | FailRun | Compensate`. Commit.

---

### Task 2.7: `WakeCondition` hierarchy

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Models/Bookmarks/WakeCondition.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Bookmarks/InboundWakeCondition.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Bookmarks/TimerWakeCondition.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Bookmarks/ChildRunCompletedWakeCondition.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Bookmarks/SignalWakeCondition.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Bookmarks/HttpWakeCondition.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Bookmarks/AnyOfWakeCondition.cs`
- Create: `Tests/Wbskt.Workflow.Engine.Host.Tests/Abstraction/WakeConditionTests.cs`

Spec §3.2 — six variants.

- [ ] **Step 1: Test** — round-trip each variant by JSON. Verify `AnyOfWakeCondition.Conditions` deserialises as `IReadOnlyCollection<WakeCondition>` polymorphically (i.e., a list of mixed kinds inside one Any).

- [ ] **Step 2–5:** Implement. `WakeCondition` is an abstract record with `[JsonPolymorphic(TypeDiscriminatorPropertyName="kind")]`. Each subtype carries its own match data per spec §3.2. Commit.

---

### Task 2.8: `WorkflowConcurrencyPolicy`, `CorrelationConfig`, trigger-config types

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Enums/WorkflowConcurrencyPolicy.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Triggers/CorrelationConfig.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Triggers/DeviceTriggerNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Triggers/ScheduleTriggerNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Triggers/WebhookTriggerNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Triggers/ManualTriggerNode.cs`
- Create: `Tests/Wbskt.Workflow.Engine.Host.Tests/Abstraction/TriggerNodeTests.cs`

Spec §4.1, §4.5, §4.6, §4.7.

- [ ] **Step 1: Test** — for each of the four trigger node kinds, deserialize a JSON example (per spec §1.10 device trigger and §4.7 catalog) and assert the typed fields are populated. Verify `WorkflowConcurrencyPolicy.Queue` is the default when omitted.

- [ ] **Step 2–5:** Implement. Each trigger subtype is a `record` deriving from `BaseNode` with a typed `Config` property. Commit.

---

### Task 2.9: Control-node config types

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Controls/LogicGateNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Controls/DelayNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Controls/ForEachNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Controls/ParallelForEachNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Controls/JoinNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Controls/VariableNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Controls/SubWorkflowNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Controls/WaitForHttpNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Controls/AwaitSignalNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Controls/FailRunNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Enums/JoinMode.cs`
- Create: `Wbskt.Workflow.Abstraction/Enums/VariableOperation.cs`
- Create: `Wbskt.Workflow.Abstraction/Enums/VariableScope.cs`
- Create: `Tests/Wbskt.Workflow.Engine.Host.Tests/Abstraction/ControlNodeTests.cs`

Spec §1.6 / §2.8 (ForEach) / §2.9 (ParallelForEach + Join) / §3.10 (WaitForHttp, AwaitSignal).

- [ ] **Step 1: Test** — one round-trip test per node kind, asserting the configuration knobs from spec.

- [ ] **Step 2–5:** Implement. `JoinMode: All | Any | Quorum(n)`. `VariableScope: Local | Shared`. `VariableOperation: Set | Increment | Decrement | CompareAndSet`. Commit (`(feat): authoring layer control node configs`).

---

### Task 2.10: Action-node config types

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Actions/SendCommandActionNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Actions/EmailNotificationNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Actions/WebhookNotificationNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Actions/TelegramNotificationNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Actions/ToastNotificationNode.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/Nodes/Actions/BaseActionNode.cs`
- Create: `Tests/Wbskt.Workflow.Engine.Host.Tests/Abstraction/ActionNodeTests.cs`

`BaseActionNode` is an abstract record extending `BaseNode` adding `RetryPolicy?` and `OnFailureConfig?` per spec §1.6 ("Action carries RetryPolicy and OnFailure").

- [ ] **Step 1: Test** — for each action node, deserialize a JSON example including `retry` and `onFailure`; assert the policy attaches correctly.

- [ ] **Step 2–5:** Implement. Commit (`(feat): authoring layer action node configs`).

---

### Task 2.11: `SharedVariableSchema` and `WorkflowDefinition`

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Models/SharedVariables/SharedVariableDeclaration.cs`
- Create: `Wbskt.Workflow.Abstraction/Enums/SharedVariableType.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/SharedVariables/ResetPolicy.cs`
- Create: `Wbskt.Workflow.Abstraction/Models/WorkflowDefinition.cs`
- Create: `Tests/Wbskt.Workflow.Engine.Host.Tests/Abstraction/WorkflowDefinitionTests.cs`

Spec §1.5 / §1.7.

- [ ] **Step 1: Test** — round-trip the spec §1.10 greenhouse example end-to-end through `JsonSerializer.Deserialize<WorkflowDefinition>()`, assert node count, edge count, shared-variable count.

- [ ] **Step 2–5:** Implement.

```csharp
public sealed record WorkflowDefinition(
    Guid WorkflowRefId,
    int Version,
    int WorkspaceId,
    string Name,
    string? Description,
    bool IsEnabled,
    IReadOnlyCollection<BaseNode> Nodes,
    IReadOnlyCollection<Edge> Edges,
    IReadOnlyCollection<SharedVariableDeclaration> SharedVariableSchema,
    DateTime CreatedAt,
    int PublishedBy
);
```

`ResetPolicy` is itself polymorphic (`None | DailyAtUtc(TimeSpan) | OnDefinitionPublish`). Commit (`(feat): authoring layer WorkflowDefinition + SharedVariableSchema`).

---

### Task 2.12: `WorkflowValidator`

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Validation/WorkflowValidator.cs`
- Create: `Wbskt.Workflow.Abstraction/Validation/ValidationResult.cs`
- Create: `Wbskt.Workflow.Abstraction/Validation/ValidationIssue.cs`
- Create: `Wbskt.Workflow.Abstraction/Enums/ValidationSeverity.cs`
- Create: `Tests/Wbskt.Workflow.Engine.Host.Tests/Abstraction/WorkflowValidatorTests.cs`

Spec §1.8: hard errors block publish; soft warnings surface to the designer.

- [ ] **Step 1: Test** — six scenarios:
  1. Edge references non-existent NodeId → Error.
  2. Edge references non-existent PortId on existing node → Error.
  3. `WorkflowRefId == Guid.Empty` → Error.
  4. `Version < 1` → Error.
  5. Zero trigger nodes → Warning, no error.
  6. Orphan node (no edges) → Warning, no error.

```csharp
[Fact]
public void Validate_returns_error_when_edge_references_unknown_node()
{
    var def = ValidWorkflowBuilder.Build() with { Edges = [ new Edge((Guid.NewGuid(),"out"), (Guid.NewGuid(),"in")) ] };
    var result = new WorkflowValidator().Validate(def);
    Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "EDGE_UNKNOWN_NODE");
}
```

(Build a `ValidWorkflowBuilder` test helper in this task to keep all six tests concise.)

- [ ] **Step 2–5:** Implement.

```csharp
public sealed class WorkflowValidator
{
    public ValidationResult Validate(WorkflowDefinition definition)
    {
        var issues = new List<ValidationIssue>();
        ValidateShape(definition, issues);
        ValidateEdges(definition, issues);
        WarnIfNoTriggers(definition, issues);
        WarnIfOrphans(definition, issues);
        return new ValidationResult(issues);
    }

    // Private helpers per spec §1.8.
}
```

Commit (`(feat): authoring layer WorkflowValidator with hard-error + warning split`).

---

**Phase 2 acceptance:**

- [ ] `dotnet test Tests/Wbskt.Workflow.Engine.Host.Tests` runs at least one test per task above and all pass.
- [ ] `JsonSerializer.Deserialize<WorkflowDefinition>(greenhouseJson)` round-trips the spec §1.10 sample without loss.
- [ ] `WorkflowValidator.Validate` produces zero errors for the §1.10 sample.
- [ ] Twelve commits on `feat/workflow-engine` for this phase.

---

## Phase 3 — Storage Providers

**Goal:** One SQL-backed provider per aggregate. All inherit `BaseSqlProvider`. No cross-provider dependencies (orchestration lives in the engine services layer). Strict TDD with Moq for `SqlConnection`/`SqlDataReader` boundaries — we wrap data-reader access through small mapper helpers and unit-test the mappers + the parameter shapes; the actual SQL execution path is covered by Phase 13 integration tests.

**Pattern (applies to every provider):**
- Interface in `Wbskt.Workflow.Abstraction/Providers/I<Aggregate>Provider.cs` — public surface uses `RefId` (Guid) on inputs; `FindBy*` returns `int?`, `GetBy*` returns an entity.
- Implementation in `Wbskt.Workflow/Providers/<Aggregate>Provider.cs` — inherits `BaseSqlProvider`, opens an `await using var connection = await OpenConnectionAsync(...)`, calls a sproc via `SqlCommand`, maps the reader to the entity via a private static `Map(SqlDataReader)`.
- Each entity record lives in `Wbskt.Workflow/Entities/<Aggregate>Row.cs` — DB-shape DTO with internal `int Id`, public `Guid RefId`, and the columns from the spec §6.1 table.
- Tests in `Tests/Wbskt.Workflow.Engine.Host.Tests/Providers/<Aggregate>ProviderTests.cs` — exercise the mapper with a faked `SqlDataReader` via Moq; assert column ordinals and parameter names.

**Reusable test helper (Task 3.1):** `FakeSqlDataReader` — a Moq-driven `DbDataReader` test double the rest of the phase reuses.

---

### Task 3.1: FakeSqlDataReader test helper

**Files:**
- Create: `Tests/Wbskt.Workflow.Engine.Host.Tests/Infrastructure/FakeSqlDataReader.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Infrastructure/FakeSqlDataReaderTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
[Fact]
public void FakeSqlDataReader_returns_rows_in_order()
{
    var reader = FakeSqlDataReader.From(new[]
    {
        new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "a" },
        new Dictionary<string, object?> { ["Id"] = 2, ["Name"] = "b" },
    });

    reader.Read().Should().BeTrue();
    reader.GetInt32(reader.GetOrdinal("Id")).Should().Be(1);
    reader.GetString(reader.GetOrdinal("Name")).Should().Be("a");
    reader.Read().Should().BeTrue();
    reader.GetInt32(reader.GetOrdinal("Id")).Should().Be(2);
    reader.Read().Should().BeFalse();
}
```

- [ ] **Step 2:** Run `dotnet test ... --filter FakeSqlDataReader` — expect FAIL.

- [ ] **Step 3:** Implement `FakeSqlDataReader : DbDataReader` with an in-memory `IReadOnlyList<IReadOnlyDictionary<string, object?>>` backing store; only the methods used by our mappers need real impls (others can throw `NotImplementedException`).

- [ ] **Step 4:** Re-run; expect PASS.

- [ ] **Step 5: Commit**

```bash
git add Tests/Wbskt.Workflow.Engine.Host.Tests/Infrastructure/
git commit -m "(test): add FakeSqlDataReader helper

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 3.2: WorkflowDefinitionProvider

**Spec:** §6.1 (table), §1 (authoring layer is what gets stored).

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Providers/IWorkflowDefinitionProvider.cs`
- Create: `Wbskt.Workflow/Entities/WorkflowDefinitionRow.cs`
- Create: `Wbskt.Workflow/Providers/WorkflowDefinitionProvider.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Providers/WorkflowDefinitionProviderTests.cs`

**Interface surface:**
```csharp
public interface IWorkflowDefinitionProvider
{
    Task<int?> FindByRefIdVersionAsync(Guid refId, int version, CancellationToken ct);
    Task<WorkflowDefinitionRow> GetByIdAsync(int id, CancellationToken ct);
    Task<WorkflowDefinitionRow> GetByRefIdVersionAsync(Guid refId, int version, CancellationToken ct);
    Task<WorkflowDefinitionRow> GetCurrentByRefIdAsync(Guid refId, CancellationToken ct);
    Task<int> InsertAsync(WorkflowDefinitionRow row, CancellationToken ct);
    Task PublishAsync(int id, CancellationToken ct);
    Task DeprecateAsync(int id, CancellationToken ct);
}
```

- [ ] **Step 1:** Write failing test `Map_reads_all_columns_from_reader` exercising the private `Map` helper via `internal` visibility + `InternalsVisibleTo`. Assert `Id`, `RefId`, `Version`, `Status`, `Definition` (JSON), `CreatedAt` map correctly.

- [ ] **Step 2:** Run — expect FAIL.

- [ ] **Step 3:** Implement `WorkflowDefinitionRow` record + provider with sproc calls (`WorkflowDefinition_FindBy_RefId_Version`, `WorkflowDefinition_GetById`, `WorkflowDefinition_GetBy_RefId_Version`, `WorkflowDefinition_GetCurrentBy_RefId`, `WorkflowDefinition_Insert`, `WorkflowDefinition_Publish`, `WorkflowDefinition_Deprecate`).

- [ ] **Step 4:** Write parameter-shape test:
```csharp
[Fact]
public void Insert_sets_expected_parameters()
{
    var captured = new List<SqlParameter>();
    var provider = new WorkflowDefinitionProvider(/* connection factory that captures cmd */);
    // assert cmd.CommandText, cmd.CommandType=StoredProcedure, parameter names match sproc signature
}
```

- [ ] **Step 5:** Run all tests — expect PASS.

- [ ] **Step 6: Commit**
```bash
git commit -m "(workflow): add WorkflowDefinitionProvider

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 3.3: TriggerRegistrationProvider

**Spec:** §3, §4.4 (correlation key resolution reads from this).

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Providers/ITriggerRegistrationProvider.cs`
- Create: `Wbskt.Workflow/Entities/TriggerRegistrationRow.cs`
- Create: `Wbskt.Workflow/Providers/TriggerRegistrationProvider.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Providers/TriggerRegistrationProviderTests.cs`

**Interface surface:**
```csharp
public interface ITriggerRegistrationProvider
{
    Task<IReadOnlyCollection<TriggerRegistrationRow>> GetActiveByChannelAsync(string channelKind, string channelKey, CancellationToken ct);
    Task<int> InsertAsync(TriggerRegistrationRow row, CancellationToken ct);
    Task DeactivateAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct);
}
```

`channelKind` ∈ `{ "device", "schedule", "webhook", "manual", "signal", "child-completed", "http-wake" }`. `channelKey` = composite key the inbound side computes (e.g., device serial + payload type for device inbound; cron-bucket key for schedule).

- [ ] **Step 1:** Write failing tests `Map_reads_all_columns`, `GetActiveByChannel_passes_channelKind_and_channelKey_parameters`.
- [ ] **Step 2:** Run — FAIL.
- [ ] **Step 3:** Implement row + provider with sprocs `TriggerRegistration_GetActiveBy_Channel`, `TriggerRegistration_Insert`, `TriggerRegistration_DeactivateAllBy_WorkflowDefinitionId`.
- [ ] **Step 4:** Run — PASS.
- [ ] **Step 5: Commit** `(workflow): add TriggerRegistrationProvider`.

---

### Task 3.4: RunProvider

**Spec:** §2.1 Run lifecycle, §2.2 status enum.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Providers/IRunProvider.cs`
- Create: `Wbskt.Workflow/Entities/RunRow.cs`
- Create: `Wbskt.Workflow/Providers/RunProvider.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Providers/RunProviderTests.cs`

**Interface surface:**
```csharp
public interface IRunProvider
{
    Task<int> InsertAsync(RunRow row, CancellationToken ct);
    Task<RunRow> GetByIdAsync(long runId, CancellationToken ct);
    Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct);
    Task TransitionStatusAsync(long runId, RunStatus from, RunStatus to, CancellationToken ct);
    Task SetTerminalAsync(long runId, RunStatus terminal, DateTime completedAt, CancellationToken ct);
    Task<IReadOnlyCollection<RunRow>> GetRunningOnHostAsync(string hostId, CancellationToken ct);
}
```

- [ ] **Step 1:** Write failing tests for `Map` + each method's parameter shape. Include a test for `TransitionStatusAsync_throws_when_optimistic_concurrency_check_fails` — the sproc returns 0 affected rows when the `from` status doesn't match; provider throws `OptimisticConcurrencyException` (define this exception in `Wbskt.Workflow.Abstraction/Exceptions/OptimisticConcurrencyException.cs`).
- [ ] **Step 2:** Run — FAIL.
- [ ] **Step 3:** Implement row + provider + exception. Sprocs: `Run_Insert`, `Run_GetById`, `Run_GetBy_RefId`, `Run_TransitionStatus`, `Run_SetTerminal`, `Run_GetRunningBy_HostId`.
- [ ] **Step 4:** Run — PASS.
- [ ] **Step 5: Commit** `(workflow): add RunProvider`.

---

### Task 3.5: RunCountersProvider

**Spec:** §2.15 "who completes last" race, §2.10 active-branch tracking.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Providers/IRunCountersProvider.cs`
- Create: `Wbskt.Workflow/Entities/RunCountersRow.cs`
- Create: `Wbskt.Workflow/Providers/RunCountersProvider.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Providers/RunCountersProviderTests.cs`

**Interface surface:**
```csharp
public interface IRunCountersProvider
{
    Task<int> IncrementActiveBranchesAsync(long runId, int delta, CancellationToken ct); // returns new count
    Task<int> IncrementCompletedBranchesAsync(long runId, int delta, CancellationToken ct);
    Task<int> IncrementFailedBranchesAsync(long runId, int delta, CancellationToken ct);
    Task<RunCountersRow> GetByRunIdAsync(long runId, CancellationToken ct);
}
```

The Increment methods MUST surface the post-update count atomically (sproc uses `OUTPUT inserted.<col>`). Engine code uses the returned value to decide "is this the last branch?".

- [ ] **Step 1:** Write failing test `Increment_returns_post_update_value`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. Sprocs: `RunCounters_IncrementActiveBranches`, `RunCounters_IncrementCompletedBranches`, `RunCounters_IncrementFailedBranches`, `RunCounters_GetBy_RunId`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add RunCountersProvider`.

---

### Task 3.6: BranchProvider

**Spec:** §2.6 Branches are THE snapshot; §2.10 branch-pointer updates.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Providers/IBranchProvider.cs`
- Create: `Wbskt.Workflow/Entities/BranchRow.cs`
- Create: `Wbskt.Workflow/Providers/BranchProvider.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Providers/BranchProviderTests.cs`

**Interface surface:**
```csharp
public interface IBranchProvider
{
    Task<long> InsertAsync(BranchRow row, CancellationToken ct);
    Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct);
    Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(long runId, CancellationToken ct);
    Task UpdatePointerAsync(long branchId, string currentNodeId, byte[] localState, int attempt, CancellationToken ct);
    Task SetCompletedAsync(long branchId, DateTime completedAt, CancellationToken ct);
    Task SetFailedAsync(long branchId, string errorJson, DateTime completedAt, CancellationToken ct);
}
```

`BranchRow.LocalState` is `byte[]` (we store the JSON payload as `VARBINARY(MAX)` per spec §6.1).

- [ ] **Step 1:** Write failing `Map_reads_all_columns_including_local_state_bytes` test.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. Sprocs: `Branch_Insert`, `Branch_GetById`, `Branch_GetActiveBy_RunId`, `Branch_UpdatePointer`, `Branch_SetCompleted`, `Branch_SetFailed`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add BranchProvider`.

---

### Task 3.7: BookmarkProvider

**Spec:** §3.3 bookmark layout, §3.4 due-lease semantics.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Providers/IBookmarkProvider.cs`
- Create: `Wbskt.Workflow/Entities/BookmarkRow.cs`
- Create: `Wbskt.Workflow/Providers/BookmarkProvider.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Providers/BookmarkProviderTests.cs`

**Interface surface:**
```csharp
public interface IBookmarkProvider
{
    Task<long> InsertAsync(BookmarkRow row, CancellationToken ct);
    Task<BookmarkRow?> GetByCorrelationAsync(string channelKind, string correlationKey, CancellationToken ct);
    Task<IReadOnlyCollection<BookmarkRow>> LeaseDueAsync(DateTime nowUtc, int batchSize, string leaseOwner, TimeSpan leaseDuration, CancellationToken ct);
    Task DeleteByIdAsync(long bookmarkId, CancellationToken ct);
    Task DeleteByBranchIdAsync(long branchId, CancellationToken ct);
}
```

`LeaseDueAsync` calls a sproc that uses `UPDLOCK, READPAST` + `OUTPUT` to atomically claim + return due bookmarks. Per spec §7.2 v1 ships with `AlwaysHoldsLeaseHolder`, but the sproc is multi-host-safe from day one.

- [ ] **Step 1:** Write failing tests for `Map`, `GetByCorrelation_returns_null_when_no_row`, `LeaseDue_passes_correct_parameters`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. Sprocs: `Bookmark_Insert`, `Bookmark_GetBy_Correlation`, `Bookmark_LeaseDue`, `Bookmark_DeleteById`, `Bookmark_DeleteBy_BranchId`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add BookmarkProvider`.

---

### Task 3.8: HistoryEventProvider

**Spec:** §2.11 history-event taxonomy, §6.1 clustering on `(RunId, HistoryEventId)`, TVP `HistoryEventTableType`.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Providers/IHistoryEventProvider.cs`
- Create: `Wbskt.Workflow/Entities/HistoryEventRow.cs`
- Create: `Wbskt.Workflow/Providers/HistoryEventProvider.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Providers/HistoryEventProviderTests.cs`

**Interface surface:**
```csharp
public interface IHistoryEventProvider
{
    Task AppendBatchAsync(long runId, IReadOnlyCollection<HistoryEventRow> events, CancellationToken ct);
    Task<IReadOnlyCollection<HistoryEventRow>> GetByRunIdAsync(long runId, long fromEventId, int maxRows, CancellationToken ct);
}
```

`AppendBatchAsync` constructs a `DataTable` matching the `HistoryEventTableType` TVP and passes it to `HistoryEvent_AppendBatch`. The sproc assigns monotonically increasing `HistoryEventId` per `RunId` inside a single transaction.

- [ ] **Step 1:** Write failing test `AppendBatch_builds_TVP_with_correct_column_order`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. The DataTable column order MUST match the TVP definition from Phase 1 Task 1.7 exactly.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add HistoryEventProvider`.

---

### Task 3.9: IdempotencyKeyProvider

**Spec:** §2.13 per `(RunId, BranchId, NodeId, Attempt)` idempotency.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Providers/IIdempotencyKeyProvider.cs`
- Create: `Wbskt.Workflow/Entities/IdempotencyKeyRow.cs`
- Create: `Wbskt.Workflow/Providers/IdempotencyKeyProvider.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Providers/IdempotencyKeyProviderTests.cs`

**Interface surface:**
```csharp
public interface IIdempotencyKeyProvider
{
    /// Returns true when the key was newly inserted; false when it already existed (duplicate side effect prevented).
    Task<bool> TryClaimAsync(long runId, long branchId, string nodeId, int attempt, string sideEffectKey, CancellationToken ct);
    Task<bool> ExistsAsync(long runId, long branchId, string nodeId, int attempt, CancellationToken ct);
}
```

- [ ] **Step 1:** Write failing test `TryClaim_returns_true_on_first_call_false_on_duplicate`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. Sproc `IdempotencyKey_TryClaim` uses `INSERT ... WHERE NOT EXISTS` + returns `@@ROWCOUNT`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add IdempotencyKeyProvider`.

---

### Task 3.10: SharedVariableProvider

**Spec:** §5.2 shared variable read/write/CAS.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Providers/ISharedVariableProvider.cs`
- Create: `Wbskt.Workflow/Entities/SharedVariableRow.cs`
- Create: `Wbskt.Workflow/Providers/SharedVariableProvider.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Providers/SharedVariableProviderTests.cs`

**Interface surface:**
```csharp
public interface ISharedVariableProvider
{
    Task<SharedVariableRow?> GetAsync(int workflowDefinitionId, string name, CancellationToken ct);
    /// Returns new RowVersion bytes on success; throws OptimisticConcurrencyException if expectedRowVersion mismatches.
    Task<byte[]> SetWithCasAsync(int workflowDefinitionId, string name, byte[] valueJson, byte[]? expectedRowVersion, CancellationToken ct);
}
```

`RowVersion` is SQL Server `ROWVERSION` (8 bytes). Spec §5.2 mandates compare-and-swap; engine retries on `OptimisticConcurrencyException`.

- [ ] **Step 1:** Write failing tests `Get_returns_null_when_missing`, `SetWithCas_returns_new_RowVersion`, `SetWithCas_throws_OptimisticConcurrencyException_when_expected_mismatches`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. Sprocs: `SharedVariable_Get`, `SharedVariable_SetWithCas`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add SharedVariableProvider`.

---

### Task 3.11: PendingTriggerEventProvider

**Spec:** §4.5 Queue-policy backlog.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Providers/IPendingTriggerEventProvider.cs`
- Create: `Wbskt.Workflow/Entities/PendingTriggerEventRow.cs`
- Create: `Wbskt.Workflow/Providers/PendingTriggerEventProvider.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Providers/PendingTriggerEventProviderTests.cs`

**Interface surface:**
```csharp
public interface IPendingTriggerEventProvider
{
    Task<long> EnqueueAsync(PendingTriggerEventRow row, CancellationToken ct);
    Task<PendingTriggerEventRow?> DequeueOldestByCorrelationAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct);
    Task<int> CountByCorrelationAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct);
}
```

- [ ] **Step 1:** Write failing tests for `Map`, `DequeueOldest_returns_null_when_empty`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. Sprocs: `PendingTriggerEvent_Enqueue`, `PendingTriggerEvent_DequeueOldestBy_Correlation` (uses `UPDLOCK, READPAST, ROWLOCK` + `TOP 1 ORDER BY EnqueuedAt`), `PendingTriggerEvent_CountBy_Correlation`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add PendingTriggerEventProvider`.

---

### Task 3.12: ScheduledFireProvider

**Spec:** §3.5 schedule trigger / §3.6 timer infrastructure.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Providers/IScheduledFireProvider.cs`
- Create: `Wbskt.Workflow/Entities/ScheduledFireRow.cs`
- Create: `Wbskt.Workflow/Providers/ScheduledFireProvider.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Providers/ScheduledFireProviderTests.cs`

**Interface surface:**
```csharp
public interface IScheduledFireProvider
{
    Task<long> InsertAsync(ScheduledFireRow row, CancellationToken ct);
    Task<IReadOnlyCollection<ScheduledFireRow>> LeaseDueAsync(DateTime nowUtc, int batchSize, string leaseOwner, TimeSpan leaseDuration, CancellationToken ct);
    Task AdvanceNextAsync(long scheduledFireId, DateTime nextFireAt, CancellationToken ct);
    Task DeleteAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct);
}
```

- [ ] **Step 1:** Write failing tests for `Map`, `LeaseDue_parameters`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. Sprocs from Phase 1 Task 1.10.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add ScheduledFireProvider`.

---

### Task 3.13: DI registration smoke test

**Files:**
- Create: `Wbskt.Workflow/Extensions/WorkflowServiceCollectionExtensions.cs`
- Modify: `Hosts/Wbskt.Workflow.Engine.Host/Program.cs` (add `builder.Services.AddWorkflowEngine(builder.Configuration);`)
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Extensions/WorkflowServiceCollectionExtensionsTests.cs`

- [ ] **Step 1: Write failing test**
```csharp
[Fact]
public void AddWorkflowEngine_registers_all_providers()
{
    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    services.AddWorkflowEngine(/* config */);
    var sp = services.BuildServiceProvider();

    sp.GetService<IWorkflowDefinitionProvider>().Should().NotBeNull();
    sp.GetService<ITriggerRegistrationProvider>().Should().NotBeNull();
    sp.GetService<IRunProvider>().Should().NotBeNull();
    sp.GetService<IRunCountersProvider>().Should().NotBeNull();
    sp.GetService<IBranchProvider>().Should().NotBeNull();
    sp.GetService<IBookmarkProvider>().Should().NotBeNull();
    sp.GetService<IHistoryEventProvider>().Should().NotBeNull();
    sp.GetService<IIdempotencyKeyProvider>().Should().NotBeNull();
    sp.GetService<ISharedVariableProvider>().Should().NotBeNull();
    sp.GetService<IPendingTriggerEventProvider>().Should().NotBeNull();
    sp.GetService<IScheduledFireProvider>().Should().NotBeNull();
}
```
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement `AddWorkflowEngine(this IServiceCollection, IConfiguration)` registering all 11 providers as `Scoped` (matches `BaseSqlProvider` convention in the repo).
- [ ] **Step 4:** PASS. Also run `dotnet build Wbskt.slnx` to confirm Engine Host still builds.
- [ ] **Step 5: Commit** `(workflow): wire up storage providers in DI`.

---

**Phase 3 acceptance:**
- [ ] 11 providers + interfaces + entity rows live in `Wbskt.Workflow.Abstraction` / `Wbskt.Workflow`.
- [ ] Every provider has at least one mapper test and one parameter-shape test.
- [ ] DI smoke test passes.
- [ ] 13 commits on `feat/workflow-engine`.

---

## Phase 4 — Engine Core Primitives

**Goal:** Small, pure types that the Branch Loop, Bookmarks, and Node Executors all depend on. No I/O. All trivially unit-testable.

**Files all live under** `Wbskt.Workflow.Abstraction/Engine/` (interfaces + DTOs) or `Wbskt.Workflow/Engine/` (default implementations).

---

### Task 4.1: BranchContext

**Spec:** §2.5 BranchContext fields, §2.10 BranchLoop consumes BranchContext.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/BranchContext.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/BranchContextTests.cs`

`BranchContext` is a record with: `long RunId`, `long BranchId`, `int WorkflowDefinitionId`, `Guid WorkflowDefinitionRefId`, `int Version`, `string CurrentNodeId`, `int Attempt`, `IReadOnlyDictionary<string, JsonElement> LocalState`, `IReadOnlyDictionary<string, JsonElement> TriggerPayload`, `string CorrelationKey`, `DateTime StartedAt`.

- [ ] **Step 1:** Write failing test `BranchContext_with_local_state_replaces_local_state_only` exercising the record's `with` semantics.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement record.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add BranchContext primitive`.

---

### Task 4.2: BranchPointer

**Spec:** §2.10 pointer is what the engine persists per branch step.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/BranchPointer.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/BranchPointerTests.cs`

`BranchPointer` is a record: `string NodeId`, `int Attempt`, `IReadOnlyDictionary<string, JsonElement> LocalState`. Helper static `From(BranchContext)` constructs a pointer from a context.

- [ ] **Step 1:** Write failing test `From_extracts_pointer_fields`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add BranchPointer primitive`.

---

### Task 4.3: NodeExecutionResult discriminated union

**Spec:** §2.9 node executor return values — the engine branches on this.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/NodeExecutionResult.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/NodeExecutionResultTests.cs`

Implement as abstract record + nested records (closed hierarchy using `[JsonPolymorphic]` not required because this never crosses the JSON boundary):

```csharp
public abstract record NodeExecutionResult
{
    /// Branch continues to the named outbound port (engine resolves to the next node).
    public sealed record Continue(string OutboundPort, IReadOnlyDictionary<string, JsonElement> LocalStatePatch) : NodeExecutionResult;
    /// Spawn N additional child branches at the given nodes; current branch follows ContinueNodeId (if non-null) or terminates.
    public sealed record Fork(IReadOnlyCollection<ForkSpec> Children, string? ContinueNodeId, IReadOnlyDictionary<string, JsonElement> LocalStatePatch) : NodeExecutionResult;
    /// Park the branch waiting for a wake signal; engine persists a Bookmark.
    public sealed record WaitForBookmark(WakeCondition Condition, IReadOnlyDictionary<string, JsonElement> LocalStatePatch) : NodeExecutionResult;
    /// Node failed — engine consults RetryPolicy + OnFailureConfig.
    public sealed record Fail(string ErrorCode, string Message, bool Retryable, Exception? Cause) : NodeExecutionResult;
    /// Branch terminates successfully (e.g., end node, FailRun success path, FailRun error path with Status=Succeeded).
    public sealed record Terminal(BranchTerminalReason Reason) : NodeExecutionResult;
}

public sealed record ForkSpec(string NodeId, IReadOnlyDictionary<string, JsonElement> LocalState);

public enum BranchTerminalReason { Completed, Cancelled, Failed }
```

- [ ] **Step 1:** Write failing tests `Continue_carries_outbound_port`, `Fork_with_two_children`, `WaitForBookmark_carries_condition`, `Fail_with_retryable_flag`, `Terminal_with_reason`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add NodeExecutionResult discriminated union`.

---

### Task 4.4: INodeExecutor

**Spec:** §2.9 executor contract.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/INodeExecutor.cs`
- Test: deferred (interface is exercised by Phase 9 executor tests).

```csharp
public interface INodeExecutor
{
    /// The NodeKind this executor handles. Engine looks up by this key.
    NodeKind Handles { get; }
    Task<NodeExecutionResult> ExecuteAsync(BaseNode node, BranchContext context, INodeExecutionServices services, CancellationToken ct);
}

public interface INodeExecutionServices
{
    IClock Clock { get; }
    IIdGenerator IdGenerator { get; }
    ISharedVariableProvider SharedVariables { get; }
    IIdempotencyKeyProvider IdempotencyKeys { get; }
    IHistoryEventProvider History { get; }
    /// For nodes that publish to the inbound layer (e.g., SubWorkflow start).
    IRunDispatcher RunDispatcher { get; }
}
```

`IRunDispatcher` is defined in Phase 5 Task 5.1; reference it forward.

- [ ] **Step 1:** Create interfaces (no test — interface-only step has no behavior to assert).
- [ ] **Step 2:** Run `dotnet build` — expect missing `IRunDispatcher`. Add a temporary marker interface `public interface IRunDispatcher { }` in `Wbskt.Workflow.Abstraction/Engine/IRunDispatcher.cs` (Phase 5 fleshes it out). Document with `// Filled in by Phase 5 Task 5.1`.
- [ ] **Step 3:** Build passes.
- [ ] **Step 4: Commit** `(workflow): add INodeExecutor + INodeExecutionServices`.

---

### Task 4.5: IClock + SystemClock

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/IClock.cs`
- Create: `Wbskt.Workflow/Engine/SystemClock.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/SystemClockTests.cs`

```csharp
public interface IClock { DateTime UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTime UtcNow => DateTime.UtcNow; }
```

- [ ] **Step 1:** Write failing test `SystemClock_returns_utc_now_within_1_second`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add IClock + SystemClock`.

---

### Task 4.6: IIdGenerator + GuidV7IdGenerator

**Spec:** §2.4 ID strategy — Run/Branch use `long` from DB IDENTITY; History uses per-Run monotone IDs; correlation keys + RefIds use Guid v7 for time-orderable uniqueness.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/IIdGenerator.cs`
- Create: `Wbskt.Workflow/Engine/GuidV7IdGenerator.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/GuidV7IdGeneratorTests.cs`

```csharp
public interface IIdGenerator { Guid NewGuidV7(); string NewCorrelationKey(string prefix); }
```

Use `Guid.CreateVersion7()` from .NET 10. `NewCorrelationKey("device:abc:")` returns `"device:abc:" + Guid v7 base64url`.

- [ ] **Step 1:** Write failing tests `NewGuidV7_produces_version_7_guid`, `NewCorrelationKey_starts_with_prefix`, `NewGuidV7_calls_are_time_ordered`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add GuidV7IdGenerator`.

---

### Task 4.7: ICreditCostCalculator + DefaultCreditCostCalculator

**Spec:** §5.4 credit accounting per node-kind.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/ICreditCostCalculator.cs`
- Create: `Wbskt.Workflow/Engine/DefaultCreditCostCalculator.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/DefaultCreditCostCalculatorTests.cs`

```csharp
public interface ICreditCostCalculator
{
    int Cost(BaseNode node, BranchContext context);
}
```

Default cost table from spec §5.4 (cite verbatim in implementation): Trigger=0, Logic/Variable=1, ForEach/Join=1, Delay=0, Command/Webhook/Email/Telegram/Toast=5, SubWorkflow=10, WaitForHttp/AwaitSignal=1, FailRun=0.

- [ ] **Step 1:** Write failing tests covering each node-kind from spec §5.4 (one assertion per kind).
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement as switch on `NodeKind`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add credit cost calculator`.

---

### Task 4.8: DI registration for Phase 4 primitives

**Files:**
- Modify: `Wbskt.Workflow/Extensions/WorkflowServiceCollectionExtensions.cs`
- Test: extend `WorkflowServiceCollectionExtensionsTests` from Task 3.13

- [ ] **Step 1:** Add failing assertions `sp.GetService<IClock>().Should().BeOfType<SystemClock>()`, same for `IIdGenerator`, `ICreditCostCalculator`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Register `IClock`, `IIdGenerator`, `ICreditCostCalculator` as Singletons.
- [ ] **Step 4:** PASS + build.
- [ ] **Step 5: Commit** `(workflow): register engine core primitives in DI`.

---

**Phase 4 acceptance:**
- [ ] All primitives compile and pass unit tests.
- [ ] DI smoke test now covers Phase 3 + Phase 4 types.
- [ ] 8 commits on `feat/workflow-engine`.

---

## Phase 5 — Branch Loop + IRunDispatcher

**Goal:** Implement the heart of the engine — the per-branch execution loop that drives a `BranchContext` through `INodeExecutor`s, handles `Continue`/`Fork`/`WaitForBookmark`/`Fail`/`Terminal`, and persists branch pointers + history after every step. Plus the `IRunDispatcher` that hands off branches between the inbound layer, the inline-first fan-out path, and the bookmark resumer.

**Per spec §7.2:** v1 ships `ChannelRunDispatcher` (in-process `Channel<BranchExecutionRequest>`) and `AlwaysHoldsLeaseHolder`. `SqlRunDispatcher` / `SqlLeaseHolder` are out-of-scope.

**Per spec §2.10:** the Branch Loop is the single canonical execution path. Inline fan-out (a `Fork` with one child + no continue) recurses into the loop directly; multi-child forks dispatch each child to `IRunDispatcher` and return.

---

### Task 5.1: IRunDispatcher interface

**Spec:** §7.2.

**Files:**
- Modify: `Wbskt.Workflow.Abstraction/Engine/IRunDispatcher.cs` (replace the placeholder from Task 4.4)
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/IRunDispatcherTests.cs` (test the contract via a stub)

```csharp
public interface IRunDispatcher
{
    /// Hand off a branch for asynchronous execution. Used by inbound triggers, multi-child fork, and bookmark resumer.
    ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct);
}

public sealed record BranchExecutionRequest(long RunId, long BranchId, BranchExecutionReason Reason);

public enum BranchExecutionReason { TriggerStarted, ForkChild, BookmarkResumed, Retry, Cancellation }
```

- [ ] **Step 1:** Write failing test `Stub_dispatcher_records_requests` using a trivial in-memory `IRunDispatcher` for assertion.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Define types.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): define IRunDispatcher contract`.

---

### Task 5.2: ChannelRunDispatcher implementation

**Spec:** §7.2.1.

**Files:**
- Create: `Wbskt.Workflow/Engine/ChannelRunDispatcher.cs`
- Create: `Wbskt.Workflow.Engine.Host/HostedServices/BranchExecutionPump.cs` *(consumes the Channel and invokes BranchLoop; introduced in Phase 10 but the dispatcher's Channel is created here)*
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/ChannelRunDispatcherTests.cs`

`ChannelRunDispatcher` wraps an unbounded `Channel<BranchExecutionRequest>`; `DispatchAsync` writes; the host's `BranchExecutionPump` (Phase 10) reads.

- [ ] **Step 1:** Write failing tests `DispatchAsync_writes_to_channel`, `Reader_returns_requests_in_order`, `Multiple_concurrent_dispatchers_all_succeed`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. Expose `ChannelReader<BranchExecutionRequest> Reader` for the pump.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add ChannelRunDispatcher`.

---

### Task 5.3: INodeExecutorRegistry

**Spec:** §2.9 engine looks up executor by `NodeKind`.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/INodeExecutorRegistry.cs`
- Create: `Wbskt.Workflow/Engine/NodeExecutorRegistry.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/NodeExecutorRegistryTests.cs`

```csharp
public interface INodeExecutorRegistry
{
    INodeExecutor For(NodeKind kind);
}
```

Implementation receives `IEnumerable<INodeExecutor>` via DI and builds a `FrozenDictionary<NodeKind, INodeExecutor>` at construction. Throws `InvalidOperationException("no executor for {kind}")` on miss. Duplicate registration throws at construction time.

- [ ] **Step 1:** Write failing tests `For_returns_registered_executor`, `For_throws_when_unknown`, `Constructor_throws_on_duplicate_NodeKind`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add NodeExecutorRegistry`.

---

### Task 5.4: IWorkflowDefinitionCache

**Spec:** §6.2 hot path reads a parsed `WorkflowDefinition` per `(RefId, Version)`.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/IWorkflowDefinitionCache.cs`
- Create: `Wbskt.Workflow/Engine/WorkflowDefinitionCache.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/WorkflowDefinitionCacheTests.cs`

```csharp
public interface IWorkflowDefinitionCache
{
    Task<WorkflowDefinition> GetAsync(int workflowDefinitionId, CancellationToken ct);
    void Invalidate(int workflowDefinitionId);
}
```

Implementation uses `Microsoft.Extensions.Caching.Memory.IMemoryCache` with sliding 10-min expiration; on miss, loads via `IWorkflowDefinitionProvider.GetByIdAsync` and deserializes the JSON `Definition` blob.

- [ ] **Step 1:** Write failing tests `Get_loads_from_provider_on_miss`, `Get_returns_cached_on_hit`, `Invalidate_forces_reload`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add WorkflowDefinitionCache`.

---

### Task 5.5: BranchLoop — skeleton + step persistence

**Spec:** §2.10 BranchLoop pseudocode (reference verbatim in implementation).

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/IBranchLoop.cs`
- Create: `Wbskt.Workflow/Engine/BranchLoop.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/BranchLoopTests.cs`

```csharp
public interface IBranchLoop
{
    Task RunAsync(long runId, long branchId, BranchExecutionReason reason, CancellationToken ct);
}
```

This task implements only the **happy-path Continue loop**: load context → invoke executor → on `Continue`, resolve next node, persist pointer + history append, loop. Fork/Wait/Fail/Terminal handling are added in Tasks 5.6–5.9.

- [ ] **Step 1:** Write failing test `RunAsync_executes_two_Continue_nodes_then_Terminal`. Use stubs for all providers and a fake `INodeExecutorRegistry` that returns a scripted sequence of `Continue("next", patch)`, `Continue("next", patch)`, `Terminal(Completed)`. Assert `IBranchProvider.UpdatePointerAsync` was called twice with the expected node IDs, and `Branch_SetCompleted` was called once.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement the loop following spec §2.10. Use `IWorkflowDefinitionCache` to resolve the next node via the workflow's edge list (helper: `WorkflowDefinition.NextNode(currentNodeId, outboundPort)`).
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): branch loop happy path`.

---

### Task 5.6: BranchLoop — Fork (inline single, dispatched multi)

**Spec:** §2.10 fan-out rules.

**Files:**
- Modify: `Wbskt.Workflow/Engine/BranchLoop.cs`
- Test: extend `BranchLoopTests`

- [ ] **Step 1:** Write failing tests:
  - `RunAsync_inline_single_child_fork_recurses_without_dispatch` — fork with 1 child + null ContinueNodeId. Assert dispatcher was NOT called and the loop continued into the child synchronously.
  - `RunAsync_multi_child_fork_dispatches_each_and_returns` — fork with 3 children. Assert `IRunDispatcher.DispatchAsync` called 3 times; current branch is set to terminal status `Completed` if `ContinueNodeId` is null; otherwise current branch continues with `ContinueNodeId` inline.
  - `RunAsync_fork_increments_active_branches_counter_atomically` — assert `IRunCountersProvider.IncrementActiveBranchesAsync(delta=children)` is called BEFORE any DispatchAsync (prevents lost-update on completion race per spec §2.15).
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): branch loop fan-out`.

---

### Task 5.7: BranchLoop — WaitForBookmark

**Spec:** §3.3 bookmark creation on Wait.

**Files:**
- Modify: `Wbskt.Workflow/Engine/BranchLoop.cs`
- Test: extend `BranchLoopTests`

- [ ] **Step 1:** Write failing test `RunAsync_WaitForBookmark_persists_bookmark_and_returns`. Assert: branch pointer updated, history event `BranchParked` appended, `IBookmarkProvider.InsertAsync` called with the `WakeCondition` from the executor result, branch is NOT marked terminal, loop returns.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): branch loop wait-for-bookmark`.

---

### Task 5.8: BranchLoop — Fail (stub error path, full impl in Phase 8)

**Spec:** §2.10 + §8 (Phase 8 owns retry + on-failure).

**Files:**
- Modify: `Wbskt.Workflow/Engine/BranchLoop.cs`
- Test: extend `BranchLoopTests`

In Phase 5 we wire up the `Fail` outcome to: append `NodeFailed` history event, call `Branch_SetFailed`, decrement active branch count via `RunCountersProvider`. Retry policy / on-failure dispatching is a TODO until Phase 8 — the implementation throws `NotImplementedException("Retry handled in Phase 8")` when `Fail.Retryable == true`. For `Fail.Retryable == false` the branch fails terminally.

- [ ] **Step 1:** Write failing tests `Fail_non_retryable_marks_branch_failed_and_decrements_counter`, `Fail_retryable_throws_NotImplementedException`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement (TODO comment cites Phase 8 Task 8.4).
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): branch loop fail outcome (retry deferred)`.

---

### Task 5.9: BranchLoop — Terminal + run-completion race

**Spec:** §2.15 "who completes last" wins finalization.

**Files:**
- Modify: `Wbskt.Workflow/Engine/BranchLoop.cs`
- Test: extend `BranchLoopTests`

- [ ] **Step 1:** Write failing tests:
  - `Terminal_calls_Branch_SetCompleted_and_decrements_counter`
  - `Terminal_when_post_decrement_count_is_zero_signals_run_finalizer` — assert an `IRunFinalizer.FinalizeAsync(runId)` was called (introduce `IRunFinalizer` as a marker interface in `Wbskt.Workflow.Abstraction/Engine/IRunFinalizer.cs`; Phase 8 fleshes it out).
  - `Terminal_when_post_decrement_count_is_nonzero_does_not_call_finalizer`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement using the atomic post-decrement value returned from `IncrementActiveBranchesAsync(runId, delta: -1)`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): branch loop terminal + completion race`.

---

### Task 5.10: BranchExecutionPump hosted service skeleton

**Spec:** §7.3 lists `BranchExecutionPump` as one of the 8 background services.

**Files:**
- Create: `Hosts/Wbskt.Workflow.Engine.Host/HostedServices/BranchExecutionPump.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/HostedServices/BranchExecutionPumpTests.cs`

Reads from `ChannelRunDispatcher.Reader`; for each `BranchExecutionRequest`, opens a DI scope and invokes `IBranchLoop.RunAsync(runId, branchId, reason, ct)`. Catches all exceptions, logs, and continues (per spec §7.3 the pump never dies on a single bad branch — the branch itself is marked failed by the loop's exception handler).

- [ ] **Step 1:** Write failing tests `Pump_drains_channel_and_invokes_branch_loop`, `Pump_continues_after_loop_throws`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement as `BackgroundService`; use `IServiceScopeFactory.CreateAsyncScope()`.
- [ ] **Step 4:** PASS + `dotnet build` green.
- [ ] **Step 5: Commit** `(workflow): add BranchExecutionPump hosted service`.

---

### Task 5.11: DI registration for Phase 5

**Files:**
- Modify: `Wbskt.Workflow/Extensions/WorkflowServiceCollectionExtensions.cs`
- Modify: `Hosts/Wbskt.Workflow.Engine.Host/Program.cs` (register hosted service)
- Test: extend the DI smoke test

- [ ] **Step 1:** Add failing assertions: `IRunDispatcher` resolves to `ChannelRunDispatcher` (Singleton), `INodeExecutorRegistry` as Singleton, `IWorkflowDefinitionCache` as Singleton, `IBranchLoop` as Scoped.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Register. Add `builder.Services.AddHostedService<BranchExecutionPump>();` in `Program.cs`.
- [ ] **Step 4:** PASS + build.
- [ ] **Step 5: Commit** `(workflow): wire up branch loop + dispatcher in DI`.

---

### Task 5.12: End-to-end happy-path integration-style unit test

**Files:**
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/BranchLoopEndToEndTests.cs`

Drive a tiny 3-node workflow `[start: Manual trigger] -> [logic: pass-through] -> [end]` through `BranchLoop` with stubbed providers (capture into in-memory dictionaries). Assert: history events emitted in expected order (`BranchStarted`, `NodeStarted` × 2, `NodeCompleted` × 2, `BranchCompleted`), final branch state Completed, run finalizer signalled.

- [ ] **Step 1:** Write failing E2E test.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Add small fixture helpers (`InMemoryProviders`) as needed.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): branch loop end-to-end happy-path test`.

---

**Phase 5 acceptance:**
- [ ] `BranchLoop` handles all five `NodeExecutionResult` variants (Fail-retry is a stub TODO for Phase 8).
- [ ] `ChannelRunDispatcher` + `BranchExecutionPump` deliver branches end-to-end in-process.
- [ ] DI smoke test, unit tests, and the new E2E test all pass.
- [ ] 12 commits on `feat/workflow-engine`.

---

## Phase 6 — Bookmarks

**Goal:** Persist branch parking, resume on wake (time, inbound match, signal, child completion), enforce TTLs, and guarantee inbound idempotency. The Bookmark subsystem is the linchpin of long-running workflows.

**Spec sections:** §3.3 bookmark shape & lifecycle, §3.4 BookmarkScheduler + BookmarkResumer, §3.4.2 MatchInbound vs ResumeViaBookmark paths, §3.6 TTL companion timers + unified race, §3.7 inbound idempotency.

**Concept refresher (engineer-friendly):**
- A `Bookmark` is a row that says "branch X is parked at node Y, waiting for condition Z". The engine inserts one when `BranchLoop` sees `NodeExecutionResult.WaitForBookmark`.
- `WakeCondition` is polymorphic: `TimerWake(fireAt)`, `InboundWake(channelKind, correlationKey, payloadPredicate)`, `SignalWake(signalName)`, `ChildRunWake(childRunRefId)`. Each has its own resumer path; the unified race ensures only one wake "wins" when multiple conditions race (e.g., timer-out vs inbound arrival).
- `BookmarkScheduler` polls for *due* timer-wakes (and TTLs); `BookmarkResumer` consumes wake events from inbound adapters (`MatchInbound`) and signals (`ResumeViaBookmark`).

---

### Task 6.1: BookmarkLeaseOwner identity

**Spec:** §7.2 multi-host readiness, §3.4 lease ownership.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/IHostIdentity.cs`
- Create: `Wbskt.Workflow/Engine/HostIdentity.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/HostIdentityTests.cs`

```csharp
public interface IHostIdentity { string HostId { get; } }
public sealed class HostIdentity : IHostIdentity
{
    public string HostId { get; } = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.CreateVersion7():N}";
}
```

- [ ] **Step 1:** Write failing test `HostId_includes_machine_and_process` + `Two_instances_differ`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement as Singleton.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add HostIdentity for lease ownership`.

---

### Task 6.2: BookmarkScheduler hosted service — due-timer leasing

**Spec:** §3.4 BookmarkScheduler, §3.6 TTL companion timers.

**Files:**
- Create: `Hosts/Wbskt.Workflow.Engine.Host/HostedServices/BookmarkScheduler.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/HostedServices/BookmarkSchedulerTests.cs`

Polls `IBookmarkProvider.LeaseDueAsync(nowUtc, batchSize=64, hostId, lease=2min)` every 1 second (configurable via `WorkflowEngineOptions.BookmarkPollInterval`). For each leased bookmark, enqueues `BranchExecutionRequest(runId, branchId, Reason: BookmarkResumed)` onto `IRunDispatcher`. Deletes the bookmark *only* after dispatch succeeds (so a crash retries the wake).

- [ ] **Step 1:** Write failing tests:
  - `Tick_leases_due_bookmarks_and_dispatches_each`
  - `Tick_deletes_bookmark_after_successful_dispatch`
  - `Tick_does_not_delete_when_dispatch_throws` (so the next lease cycle retries).
  - `Empty_lease_result_does_not_call_dispatcher`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement as `BackgroundService` with `PeriodicTimer`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add BookmarkScheduler hosted service`.

---

### Task 6.3: BookmarkResumer — InboundWake match path

**Spec:** §3.4.2 MatchInbound, §3.7 inbound idempotency.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/IBookmarkResumer.cs`
- Create: `Wbskt.Workflow/Engine/BookmarkResumer.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/BookmarkResumerTests.cs`

```csharp
public interface IBookmarkResumer
{
    /// Match an arriving inbound event against any parked bookmarks. Idempotent on (channelKind, correlationKey, inboundEventId).
    Task<BookmarkMatchResult> MatchInboundAsync(InboundEvent evt, CancellationToken ct);
    /// Resume by an explicit bookmark id (used by SignalNode, ChildRunCompleted, /wake/{token}).
    Task ResumeViaBookmarkAsync(long bookmarkId, IReadOnlyDictionary<string, JsonElement> wakePayload, CancellationToken ct);
}

public sealed record InboundEvent(string ChannelKind, string CorrelationKey, string InboundEventId, IReadOnlyDictionary<string, JsonElement> Payload, DateTime ReceivedAt);
public sealed record BookmarkMatchResult(bool Matched, long? BookmarkId, bool Idempotent);
```

`MatchInbound` semantics (verbatim from spec §3.4.2):
1. Compute idempotency key `($"{ChannelKind}:{CorrelationKey}:{InboundEventId}")`.
2. Try-claim via `IIdempotencyKeyProvider.TryClaimAsync` — if already claimed, return `(false, null, Idempotent: true)`.
3. Look up bookmark via `BookmarkProvider.GetByCorrelationAsync`. If none, return `(false, null, false)`.
4. Evaluate the bookmark's `payloadPredicate` against `evt.Payload`. If no match, return `(false, null, false)`.
5. Delete the bookmark (claim the wake), dispatch the branch via `IRunDispatcher`, return `(true, bookmarkId, false)`.

- [ ] **Step 1:** Write failing tests covering each of steps 1–5 as separate test cases (one assertion each).
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): bookmark resumer MatchInbound path`.

---

### Task 6.4: BookmarkResumer — ResumeViaBookmark path

**Spec:** §3.4.2 ResumeViaBookmark path (Signal, ChildRun, explicit wake-token).

**Files:**
- Modify: `Wbskt.Workflow/Engine/BookmarkResumer.cs`
- Test: extend `BookmarkResumerTests`

`ResumeViaBookmarkAsync` semantics:
1. Look up bookmark by id; if missing or already deleted, return silently (idempotent for retries).
2. Merge `wakePayload` into the branch's `LocalState` patch (engine attaches it as `wakePayload` key for the executor to read).
3. Delete the bookmark.
4. Dispatch the branch via `IRunDispatcher`.

- [ ] **Step 1:** Write failing tests `ResumeByBookmarkId_dispatches_branch`, `ResumeByBookmarkId_is_idempotent_when_bookmark_already_deleted`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): bookmark resumer ResumeViaBookmark path`.

---

### Task 6.5: WakeCondition serialization — JsonPolymorphic wiring

**Spec:** §3.3 WakeCondition hierarchy is persisted as JSON inside `Bookmark.WakeConditionJson`.

**Files:**
- Modify: `Wbskt.Workflow.Abstraction/Models/WakeCondition.cs` (already created in Phase 2 Task 2.7)
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Models/WakeConditionSerializationTests.cs`

This task verifies that `WakeCondition` polymorphic JSON round-trips. If Phase 2 Task 2.7 already provided this test, this task only audits — but per writing-plans we still run it explicitly.

- [ ] **Step 1:** Write failing test `Each_wake_condition_subtype_round_trips`. One assertion per subtype: `TimerWake`, `InboundWake`, `SignalWake`, `ChildRunWake`.
- [ ] **Step 2:** Run — if already passing from Phase 2, mark task complete and skip to commit. Otherwise FAIL.
- [ ] **Step 3:** If FAILing, add `[JsonDerivedType]` attributes per Phase 2's pattern.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): verify WakeCondition polymorphic JSON round-trips`.

---

### Task 6.6: BookmarkTtlCompanionTimer — unified race

**Spec:** §3.6 TTL companion timers — when a bookmark has a non-null TTL (e.g., WaitForHttp with timeout, AwaitSignal with deadline), the engine inserts a *companion* `TimerWake` bookmark; whichever wakes first wins, the other is deleted via cooperative cancellation.

**Files:**
- Modify: `Wbskt.Workflow/Engine/BranchLoop.cs` (the `WaitForBookmark` branch in Task 5.7 now also inserts a companion if `WakeCondition.Ttl != null`).
- Modify: `Wbskt.Workflow/Engine/BookmarkResumer.cs` (when wake wins, also delete the *sibling* bookmark by `(runId, branchId)` lookup).
- Modify: `Wbskt.Workflow.Abstraction/Models/WakeCondition.cs` (add `TimeSpan? Ttl` to the base record).
- Modify: `Wbskt.Workflow.Abstraction/Providers/IBookmarkProvider.cs` (add `Task DeleteSiblingsAsync(long runId, long branchId, long excludeBookmarkId, CancellationToken ct);`).
- Modify: `Wbskt.Workflow/Providers/BookmarkProvider.cs` + add sproc `Bookmark_DeleteSiblings.sql` to `Databases/Wbskt.Database/`.
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/BookmarkCompanionTimerTests.cs`

- [ ] **Step 1:** Write failing tests:
  - `WaitForBookmark_with_Ttl_inserts_two_bookmarks_primary_and_companion`
  - `Inbound_match_wins_deletes_companion_timer`
  - `Timer_wakes_first_deletes_primary_inbound_bookmark`
  - `Sibling_delete_excludes_the_winner_bookmark_id`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement: in `BranchLoop.WaitForBookmark` handler, after inserting primary, if `Ttl.HasValue` insert a `TimerWake(nowUtc + Ttl.Value)` companion sharing the same `(runId, branchId)`. In `BookmarkResumer`, after a successful wake (either path), call `DeleteSiblingsAsync(runId, branchId, excludeBookmarkId: winnerId)`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): bookmark TTL companion timers + unified race`.

---

### Task 6.7: BookmarkScheduler GC pass — orphaned-bookmark cleanup

**Spec:** §3.4 + §6.3 retention.

**Files:**
- Modify: `Hosts/Wbskt.Workflow.Engine.Host/HostedServices/BookmarkScheduler.cs`
- Modify: `Wbskt.Workflow.Abstraction/Providers/IBookmarkProvider.cs` (add `Task<int> DeleteOrphansAsync(CancellationToken ct);` — deletes bookmarks whose `RunId` is terminal).
- Add sproc: `Databases/Wbskt.Database/StoredProcedures/Bookmark_DeleteOrphans.sql`.
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/HostedServices/BookmarkSchedulerOrphanGcTests.cs`

`BookmarkScheduler` runs `DeleteOrphansAsync` every 5 minutes (configurable). The sproc:
```sql
DELETE TOP (1000) b FROM dbo.Bookmarks b
INNER JOIN dbo.Runs r ON r.Id = b.RunId
WHERE r.Status IN ('Succeeded','Failed','Cancelled','PartiallyFailed');
```

- [ ] **Step 1:** Write failing test `Scheduler_runs_orphan_gc_on_configured_interval`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): bookmark orphan GC`.

---

### Task 6.8: BookmarkResumer ↔ BranchLoop integration test

**Files:**
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/BookmarkResumerBranchLoopIntegrationTests.cs`

End-to-end (in-process, stubbed providers): branch hits `WaitForBookmark(InboundWake)` → inbound event arrives → resumer matches → dispatcher fires → branch loop resumes from parked node → completes.

- [ ] **Step 1:** Write failing E2E test.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Stitch fixtures; ensure all wired up.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): bookmark + branch loop end-to-end test`.

---

### Task 6.9: DI registration for Phase 6

**Files:**
- Modify: `Wbskt.Workflow/Extensions/WorkflowServiceCollectionExtensions.cs`
- Modify: `Hosts/Wbskt.Workflow.Engine.Host/Program.cs` (register `BookmarkScheduler` hosted service)
- Test: extend DI smoke test

- [ ] **Step 1:** Failing assertions: `IHostIdentity` (Singleton), `IBookmarkResumer` (Scoped).
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Register + add hosted service.
- [ ] **Step 4:** PASS + `dotnet build`.
- [ ] **Step 5: Commit** `(workflow): wire bookmark services in DI`.

---

**Phase 6 acceptance:**
- [ ] BookmarkScheduler leases timer-wakes + runs orphan GC.
- [ ] BookmarkResumer handles MatchInbound (with idempotency) + ResumeViaBookmark.
- [ ] TTL companion timers race correctly; the loser is deleted.
- [ ] All Phase 6 tests + DI smoke test pass.
- [ ] 9 commits on `feat/workflow-engine`.

---

## Phase 7 — Triggers + InboundHub

**Goal:** Convert raw inbound signals (device payloads, schedule ticks, webhook calls, manual starts, child completions, signals) into either *new Run starts* or *bookmark wakes*. Enforce trigger-level concurrency policies (Queue/Cancel/Drop) per spec §4.5.

**Spec sections:** §4 Triggers, §4.3 InboundHub, §4.4 TriggerDispatcher + correlation key resolution, §4.5 concurrency policies, §4.6 Pending event backlog.

**Architectural reminder:** Inbound *adapters* (RabbitMQ subscribers, HTTP controllers, ticker) are Phase 11. This phase builds the **core engine surface** that adapters call into:
- `IInboundHub.HandleAsync(inboundEvent, ct)` — the single funnel.
- `ITriggerDispatcher` — resolves the inbound event to *interested* `TriggerRegistration`s and either starts a Run or matches a Bookmark.

---

### Task 7.1: InboundEvent + InboundEventKind taxonomy

**Spec:** §4.1 inbound event shape.

**Files:**
- Modify: `Wbskt.Workflow.Abstraction/Engine/IBookmarkResumer.cs` — the `InboundEvent` record from Phase 6 lives here; relocate it to its own file `Wbskt.Workflow.Abstraction/Engine/InboundEvent.cs`. Re-export to avoid breaking imports.
- Create: `Wbskt.Workflow.Abstraction/Engine/InboundEvent.cs`
- Test: ensure existing tests still pass.

- [ ] **Step 1:** Move `InboundEvent` record into its own file; no test changes — existing tests are the regression net.
- [ ] **Step 2:** Run all tests — PASS.
- [ ] **Step 3: Commit** `(workflow): relocate InboundEvent into its own file`.

---

### Task 7.2: ICorrelationKeyResolver

**Spec:** §4.4 correlation key resolution per channel kind.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/ICorrelationKeyResolver.cs`
- Create: `Wbskt.Workflow/Engine/CorrelationKeyResolver.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/CorrelationKeyResolverTests.cs`

```csharp
public interface ICorrelationKeyResolver
{
    /// Returns the canonical correlation key for an inbound event. The key is matched against TriggerRegistrations and Bookmarks.
    string Resolve(InboundEvent evt);
}
```

Resolution table (spec §4.4):
- `device` → `$"device:{evt.Payload["deviceSerial"]}:{evt.Payload["payloadType"]}"`
- `schedule` → `$"schedule:{evt.Payload["scheduledFireId"]}"`
- `webhook` → `$"webhook:{evt.Payload["webhookPath"]}"`
- `manual` → `$"manual:{evt.Payload["workflowDefinitionRefId"]}"`
- `signal` → `$"signal:{evt.Payload["signalName"]}:{evt.Payload["scopeRunRefId"]}"`
- `child-completed` → `$"child-completed:{evt.Payload["childRunRefId"]}"`
- `http-wake` → `$"http-wake:{evt.Payload["wakeToken"]}"`

- [ ] **Step 1:** Write failing test with one assertion per channel kind (7 cases). Include a `Resolve_throws_when_required_payload_field_is_missing` case.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement as `switch` on `ChannelKind`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add CorrelationKeyResolver`.

---

### Task 7.3: TriggerDispatcher — bookmark-match-first dispatch

**Spec:** §4.4 dispatch precedence (bookmark match wins over new Run start when an existing Run is parked on the same correlation key).

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/ITriggerDispatcher.cs`
- Create: `Wbskt.Workflow/Engine/TriggerDispatcher.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/TriggerDispatcherTests.cs`

```csharp
public interface ITriggerDispatcher
{
    Task<TriggerDispatchResult> DispatchAsync(InboundEvent evt, CancellationToken ct);
}

public sealed record TriggerDispatchResult(TriggerDispatchOutcome Outcome, long? RunId, long? BookmarkId, string Reason);
public enum TriggerDispatchOutcome { ResumedBookmark, StartedRun, Queued, Dropped, Cancelled, NoRegistration, Idempotent }
```

Algorithm (spec §4.4, verbatim):
1. Compute `correlationKey` via `ICorrelationKeyResolver`.
2. Call `IBookmarkResumer.MatchInboundAsync(evt)`. If `Matched == true`, return `ResumedBookmark`. If `Idempotent == true`, return `Idempotent`.
3. Look up active `TriggerRegistration`s via `ITriggerRegistrationProvider.GetActiveByChannelAsync(channelKind, correlationKey)`.
4. If none, return `NoRegistration`. (Inbound event is logged + dropped.)
5. For each registration, consult its `ConcurrencyPolicy` (Queue/Cancel/Drop) — defer to `ITriggerConcurrencyEnforcer` (Task 7.4).
6. If enforcer says proceed, start a new Run + a fresh branch at the trigger's node; dispatch via `IRunDispatcher`.

- [ ] **Step 1:** Write failing tests:
  - `Dispatch_resumes_bookmark_when_match_exists`
  - `Dispatch_returns_Idempotent_when_event_already_processed`
  - `Dispatch_returns_NoRegistration_when_no_trigger_matches`
  - `Dispatch_starts_new_run_when_registration_exists_and_policy_permits`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement skeleton (concurrency enforcer is a stub returning `Proceed` until Task 7.4).
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): trigger dispatcher core dispatch path`.

---

### Task 7.4: ITriggerConcurrencyEnforcer — Queue/Cancel/Drop

**Spec:** §4.5.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/ITriggerConcurrencyEnforcer.cs`
- Create: `Wbskt.Workflow/Engine/TriggerConcurrencyEnforcer.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/TriggerConcurrencyEnforcerTests.cs`

```csharp
public interface ITriggerConcurrencyEnforcer
{
    Task<TriggerConcurrencyDecision> EvaluateAsync(TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct);
}
public sealed record TriggerConcurrencyDecision(TriggerConcurrencyOutcome Outcome, long? RunIdToCancel);
public enum TriggerConcurrencyOutcome { Proceed, Queued, Dropped, ProceedAfterCancellingActive }
```

Policy semantics (spec §4.5):
- **Drop:** If any active Run exists for this `(workflowDefinitionId, correlationKey)`, drop the inbound (`Outcome = Dropped`).
- **Queue:** If any active Run exists, persist via `IPendingTriggerEventProvider.EnqueueAsync` and return `Queued`. When the active Run completes, `RunFinalizer` (Phase 8) calls `DrainPendingAsync` to dispatch the next.
- **Cancel:** If any active Run exists, return `ProceedAfterCancellingActive` with the active Run's id. `TriggerDispatcher` then issues a cancel request *and* starts the new Run.

"Active Run" = `IRunProvider` query for status ∈ `{Running, Cancelling, Failing}` matching the `(workflowDefinitionId, correlationKey)`. Add a provider method `IRunProvider.GetActiveByCorrelationAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct)` (+ sproc `Run_GetActiveBy_Correlation`).

- [ ] **Step 1:** Write failing tests — one per policy outcome (Drop, Queue, Cancel), and one per "no active Run → Proceed".
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement enforcer + the new `RunProvider` method + sproc.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): trigger concurrency enforcer (Queue/Cancel/Drop)`.

---

### Task 7.5: TriggerDispatcher — wire enforcer outcomes

**Files:**
- Modify: `Wbskt.Workflow/Engine/TriggerDispatcher.cs`
- Test: extend `TriggerDispatcherTests`

- [ ] **Step 1:** Write failing tests:
  - `Dispatch_returns_Dropped_when_enforcer_returns_Dropped`
  - `Dispatch_returns_Queued_when_enforcer_returns_Queued`
  - `Dispatch_cancels_active_run_and_starts_new_when_enforcer_returns_ProceedAfterCancellingActive`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Replace the Task-7.3 stub with the real enforcer call; for `ProceedAfterCancellingActive`, call a new `IRunCancellationService.RequestCancellationAsync(runId, reason: "Trigger-cancel-policy")` — define `IRunCancellationService` as a forward-declared interface here; Phase 8 Task 8.6 implements it.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): trigger dispatcher honors concurrency policies`.

---

### Task 7.6: InboundHub

**Spec:** §4.3.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/IInboundHub.cs`
- Create: `Wbskt.Workflow/Engine/InboundHub.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/InboundHubTests.cs`

```csharp
public interface IInboundHub
{
    Task<TriggerDispatchResult> HandleAsync(InboundEvent evt, CancellationToken ct);
}
```

`InboundHub` is the public surface that all adapters call. Internally it:
1. Stamps `evt.ReceivedAt = clock.UtcNow` if zero.
2. Appends a `History.InboundReceived` event tagged with `ChannelKind` + `CorrelationKey` (but no `RunId` yet — store under a synthetic "engine-host" run-scope; see spec §6.1 note about engine-level history events).
3. Delegates to `ITriggerDispatcher.DispatchAsync`.
4. Wraps the dispatcher result + exceptions into a uniform `TriggerDispatchResult`.

- [ ] **Step 1:** Write failing tests:
  - `Handle_stamps_received_at_when_default`
  - `Handle_delegates_to_dispatcher`
  - `Handle_returns_dispatcher_result`
  - `Handle_logs_and_rethrows_on_unexpected_exception`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. For now skip the engine-host history append (spec §6.1 carve-out is deferred; cite TODO comment referencing spec).
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add InboundHub`.

---

### Task 7.7: Trigger node executor — DeviceTrigger, ScheduleTrigger, WebhookTrigger, ManualTrigger

**Spec:** §4.2 trigger nodes are just no-op pass-through executors that emit the trigger payload as branch-local state.

**Files:**
- Create: `Wbskt.Workflow/NodeExecutors/Triggers/DeviceTriggerExecutor.cs`
- Create: `Wbskt.Workflow/NodeExecutors/Triggers/ScheduleTriggerExecutor.cs`
- Create: `Wbskt.Workflow/NodeExecutors/Triggers/WebhookTriggerExecutor.cs`
- Create: `Wbskt.Workflow/NodeExecutors/Triggers/ManualTriggerExecutor.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Triggers/TriggerExecutorTests.cs`

Each executor:
- Returns `NodeExecutionResult.Continue(OutboundPort: "default", LocalStatePatch: { "trigger": evt.Payload, "triggeredAt": clock.UtcNow })`.
- Has `Handles = NodeKind.<Specific>Trigger`.

- [ ] **Step 1:** Write failing tests — one per executor — asserting outbound port + patch keys.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement all four (one commit).
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add trigger node executors`.

---

### Task 7.8: Run startup helper — IRunStarter

**Spec:** §2.3 Run creation, §4.4 dispatcher calls into this.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/IRunStarter.cs`
- Create: `Wbskt.Workflow/Engine/RunStarter.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/RunStarterTests.cs`

```csharp
public interface IRunStarter
{
    Task<(long RunId, long BranchId)> StartAsync(int workflowDefinitionId, string triggerNodeId, InboundEvent triggerEvent, CancellationToken ct);
}
```

Algorithm:
1. Insert `Run` row with `Status=Running, StartedAt=clock.UtcNow, CorrelationKey=resolver.Resolve(evt)`.
2. Insert `RunCounters` row with `ActiveBranchCount=1`.
3. Insert initial `Branch` row with `CurrentNodeId = triggerNodeId, LocalState = { "trigger": evt.Payload, ... }`.
4. Append `History.RunStarted` event.
5. Return `(runId, branchId)`.

- [ ] **Step 1:** Write failing test `Start_creates_run_counters_branch_and_history_in_correct_order`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add RunStarter`.

---

### Task 7.9: TriggerDispatcher — actually start runs via RunStarter

**Files:**
- Modify: `Wbskt.Workflow/Engine/TriggerDispatcher.cs`
- Test: extend `TriggerDispatcherTests`

- [ ] **Step 1:** Write failing test `Dispatch_with_StartedRun_outcome_invokes_RunStarter_and_RunDispatcher`. Assert exact call order: `RunStarter.StartAsync` → `RunDispatcher.DispatchAsync`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Inject `IRunStarter`; replace the placeholder run-start path from Task 7.3.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): trigger dispatcher starts runs via RunStarter`.

---

### Task 7.10: PendingTriggerEvent draining stub (full impl in Phase 8)

**Spec:** §4.6 draining happens at Run finalization.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/IPendingTriggerEventDrainer.cs`
- Create: `Wbskt.Workflow/Engine/PendingTriggerEventDrainer.cs` (stub)
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/PendingTriggerEventDrainerTests.cs`

```csharp
public interface IPendingTriggerEventDrainer
{
    Task DrainAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct);
}
```

This task adds the interface + a stub implementation that calls `IPendingTriggerEventProvider.DequeueOldestByCorrelationAsync` and (if present) re-injects into `IInboundHub.HandleAsync`. Full Phase 8 wiring will have `RunFinalizer` call this on terminal transitions.

- [ ] **Step 1:** Write failing test `Drain_dequeues_one_and_replays_via_InboundHub`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement (dequeue + replay loop until queue empty for that correlation key).
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add PendingTriggerEventDrainer`.

---

### Task 7.11: TriggerRegistrationService — publish hooks

**Spec:** §1.7 — when a `WorkflowDefinition` is published, the engine derives `TriggerRegistration` rows from its trigger nodes.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/ITriggerRegistrationService.cs`
- Create: `Wbskt.Workflow/Engine/TriggerRegistrationService.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/TriggerRegistrationServiceTests.cs`

```csharp
public interface ITriggerRegistrationService
{
    Task OnPublishedAsync(int workflowDefinitionId, CancellationToken ct);
    Task OnDeprecatedAsync(int workflowDefinitionId, CancellationToken ct);
}
```

`OnPublishedAsync`:
1. Load the published definition.
2. For each trigger node, compute its expected `(channelKind, channelKey)` (cite spec §4.4 mapping):
   - DeviceTrigger → `("device", $"device:{deviceSerial}:{payloadType}")`
   - ScheduleTrigger → `("schedule", $"schedule:{scheduledFireId}")` *(and also insert a `ScheduledFire` row for the first fire — cron handling)*
   - WebhookTrigger → `("webhook", $"webhook:{webhookPath}")`
   - ManualTrigger → `("manual", $"manual:{workflowDefinitionRefId}")`
3. Insert a `TriggerRegistration` row pointing to `(workflowDefinitionId, triggerNodeId, channelKind, channelKey, concurrencyPolicy)`.

`OnDeprecatedAsync` calls `ITriggerRegistrationProvider.DeactivateAllByWorkflowDefinitionIdAsync` and `IScheduledFireProvider.DeleteAllByWorkflowDefinitionIdAsync`.

- [ ] **Step 1:** Write failing tests:
  - `OnPublished_inserts_one_registration_per_trigger_node`
  - `OnPublished_with_schedule_trigger_inserts_scheduled_fire`
  - `OnDeprecated_deactivates_registrations_and_deletes_scheduled_fires`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. For cron parsing, use built-in `System.Threading.PeriodicTimer` won't work — pull in `Cronos` NuGet (already used in repo or add to `Directory.Packages.props` if not). Compute first fire as `cron.GetNextOccurrence(nowUtc)`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add TriggerRegistrationService`.

---

### Task 7.12: DI registration for Phase 7

**Files:**
- Modify: `Wbskt.Workflow/Extensions/WorkflowServiceCollectionExtensions.cs`
- Test: extend DI smoke test

- [ ] **Step 1:** Failing assertions for: `ICorrelationKeyResolver` (Singleton), `ITriggerConcurrencyEnforcer` (Scoped), `ITriggerDispatcher` (Scoped), `IInboundHub` (Scoped), `IRunStarter` (Scoped), `IPendingTriggerEventDrainer` (Scoped), `ITriggerRegistrationService` (Scoped). Register four trigger executors as `INodeExecutor` (Scoped).
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Register.
- [ ] **Step 4:** PASS + build.
- [ ] **Step 5: Commit** `(workflow): wire trigger services in DI`.

---

### Task 7.13: End-to-end test — inbound event starts a run

**Files:**
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/InboundEventToRunStartE2ETests.cs`

End-to-end (in-process): `InboundHub.HandleAsync(deviceEvent)` → `TriggerDispatcher` → `RunStarter` → `BranchLoop` runs the trigger node + downstream Logic node + Terminal. Assert: `Run` row Succeeded, history events in correct order, no bookmarks left, no pending events.

- [ ] **Step 1:** Write failing E2E test.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Wire fixtures (reuse `InMemoryProviders` from Phase 5 Task 5.12).
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): trigger E2E test`.

---

**Phase 7 acceptance:**
- [ ] InboundHub funnels events → TriggerDispatcher → either bookmark match or new Run.
- [ ] Queue/Cancel/Drop policies enforced.
- [ ] Trigger registrations auto-populated on publish, removed on deprecate.
- [ ] 13 commits on `feat/workflow-engine`.

---

## Phase 8 — Error Model

**Goal:** Implement the spec's full error story — retry policies, on-failure outcomes, cooperative cancellation, run finalization (with PartiallyFailed handling), and compensation.

**Spec sections:** §8 Error Model end-to-end, §2.15 finalization, §5.5 compensation (only `sequential` order ships in v1; `reverseSequential` is v2).

---

### Task 8.1: RetryPolicy execution wrapper

**Spec:** §8.1 retry execution lives inside the executor invocation, NOT inside the node.

**Files:**
- Create: `Wbskt.Workflow/Engine/RetryExecutor.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/RetryExecutorTests.cs`

```csharp
internal static class RetryExecutor
{
    public static async Task<NodeExecutionResult> RunWithRetryAsync(
        BaseNode node, BranchContext context, INodeExecutor executor,
        INodeExecutionServices services, IClock clock, CancellationToken ct);
}
```

Algorithm (spec §8.1):
1. Read `node.RetryPolicy` (defaults: `MaxAttempts=1, Backoff=Constant(0)` if null).
2. Loop attempts: call `executor.ExecuteAsync`.
3. If result is `Fail(Retryable: true)` and `attempt < MaxAttempts`, compute delay (`Constant`, `Linear`, `Exponential` per `BackoffStrategy`), `await Task.Delay(delay, ct)`, loop.
4. If `Fail(Retryable: false)` or exhausted, return the `Fail` (engine handles on-failure next).
5. Any other result returns immediately.

- [ ] **Step 1:** Write failing tests:
  - `Retries_on_retryable_fail_until_success`
  - `Stops_retrying_after_max_attempts`
  - `Does_not_retry_on_non_retryable_fail`
  - `Exponential_backoff_delays_grow`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add RetryExecutor`.

---

### Task 8.2: BranchLoop wires RetryExecutor

**Files:**
- Modify: `Wbskt.Workflow/Engine/BranchLoop.cs` — replace direct `executor.ExecuteAsync` call with `RetryExecutor.RunWithRetryAsync`.
- Test: extend `BranchLoopTests`

- [ ] **Step 1:** Write failing test `Loop_invokes_retry_executor_on_each_step`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Replace the direct call site.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): branch loop uses RetryExecutor`.

---

### Task 8.3: OnFailureConfig outcomes — FailBranch, ContinueAsSucceeded, JumpToNode

**Spec:** §8.2 on-failure outcomes.

**Files:**
- Create: `Wbskt.Workflow/Engine/OnFailureHandler.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/OnFailureHandlerTests.cs`

```csharp
internal sealed class OnFailureHandler
{
    public NodeExecutionResult Apply(NodeExecutionResult.Fail fail, BaseNode node, BranchContext context);
}
```

Algorithm:
- Read `node.OnFailure` (default: `OnFailureConfig.FailBranch`).
- `FailBranch` → return the `Fail` as-is (branch loop marks branch failed).
- `ContinueAsSucceeded` → return `Continue("default", LocalStatePatch: { "lastError": ... })`.
- `JumpToNode(targetNodeId)` → return `Continue(targetNodeId, ...)` — branch loop's edge resolution short-circuits to the target.

The branch loop needs a new outbound resolution rule: if the previous step returned a `Continue` whose `OutboundPort` matches a known node id (not an edge), jump directly. Capture that as a new helper `NodeExecutionResult.JumpTo(nodeId, patch)` and treat it distinctly in `BranchLoop.ResolveNext`.

- [ ] **Step 1:** Write failing tests covering each outcome.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement + add `JumpTo` factory + branch-loop resolution rule.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add OnFailureHandler`.

---

### Task 8.4: BranchLoop wires OnFailureHandler — replaces Phase-5 stub

**Files:**
- Modify: `Wbskt.Workflow/Engine/BranchLoop.cs` — remove the `NotImplementedException` from Task 5.8.
- Test: extend `BranchLoopTests`

- [ ] **Step 1:** Write failing tests `Loop_applies_on_failure_continue_as_succeeded`, `Loop_applies_on_failure_jump_to_node`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Pipe `Fail` results through `OnFailureHandler.Apply`, then route the transformed result back through the loop's switch.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): branch loop applies on-failure policy`.

---

### Task 8.5: IRunFinalizer — terminal transitions + PartiallyFailed

**Spec:** §2.15 finalization, §8.3 PartiallyFailed when some branches succeeded and some failed.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/IRunFinalizer.cs` (replace placeholder from Phase 5)
- Create: `Wbskt.Workflow/Engine/RunFinalizer.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/RunFinalizerTests.cs`

```csharp
public interface IRunFinalizer
{
    Task FinalizeAsync(long runId, CancellationToken ct);
}
```

Algorithm (spec §2.15):
1. Load `Run` + `RunCounters`.
2. Determine terminal status:
   - If counters show `FailedBranches > 0 && CompletedBranches > 0` → `PartiallyFailed`.
   - If counters show `FailedBranches > 0 && CompletedBranches == 0` → `Failed`.
   - Else → `Succeeded`.
   - If current status is `Cancelling` → terminal is `Cancelled` (overrides above).
   - If current status is `Failing` → terminal is `Failed` (overrides above).
3. `IRunProvider.SetTerminalAsync(runId, terminal, clock.UtcNow)`.
4. Append `History.RunFinalized` event with the terminal status.
5. Call `IPendingTriggerEventDrainer.DrainAsync(runRow.WorkflowDefinitionId, runRow.CorrelationKey)`.
6. Publish a `RunCompleted` integration event via `IBus` (defined in spec §7.4 — message bus contract for external consumers; deferred wiring at Phase 11 inbound-adapters).
7. Delete remaining bookmarks for this Run via `IBookmarkProvider.DeleteByBranchIdAsync` (per-branch loop).

- [ ] **Step 1:** Write failing tests — one per terminal outcome (Succeeded, Failed, PartiallyFailed, Cancelled). Plus `Finalize_drains_pending_events`, `Finalize_deletes_remaining_bookmarks`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. For the integration-event publish, define `IRunCompletedPublisher` as a forward interface (impl in Phase 11).
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add RunFinalizer with PartiallyFailed handling`.

---

### Task 8.6: IRunCancellationService — cooperative cancellation

**Spec:** §8.4 cooperative cancellation — `Cancelling` is an intermediate state; branches observe the flag at the next step boundary.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/IRunCancellationService.cs`
- Create: `Wbskt.Workflow/Engine/RunCancellationService.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/RunCancellationServiceTests.cs`

```csharp
public interface IRunCancellationService
{
    Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct); // returns true if state transitioned
    Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct);
}
```

`RequestCancellationAsync`:
1. Attempt `IRunProvider.TransitionStatusAsync(runId, from: Running, to: Cancelling)`. If transition succeeds, append `History.CancellationRequested(reason)`.
2. Return `true` on success, `false` if Run was already terminal.

`IsCancellationRequestedAsync`:
1. Read Run status; return `true` if status ∈ `{Cancelling, Cancelled}`.

`BranchLoop` (modify): at the top of each iteration *before* invoking the executor, call `IsCancellationRequestedAsync(runId)`; if true, short-circuit to `Terminal(Cancelled)` and let the completion-race signal `RunFinalizer`.

Add a small in-memory cache around `IsCancellationRequestedAsync` (10-second TTL) to avoid per-step DB reads. Define `IRunStatusCache : IRunCancellationService` — keep the public interface clean, hide caching internally.

- [ ] **Step 1:** Write failing tests:
  - `RequestCancellation_transitions_status_and_logs_history`
  - `RequestCancellation_returns_false_when_already_terminal`
  - `IsCancellationRequested_returns_true_for_Cancelling`
  - `BranchLoop_short_circuits_to_Cancelled_when_cancellation_requested`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement service + cache + branch-loop integration.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add RunCancellationService + cooperative cancellation`.

---

### Task 8.7: Compensation — sequential order (v1 only)

**Spec:** §5.5 compensation Q10 — only `sequential` ships in v1.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/ICompensationOrchestrator.cs`
- Create: `Wbskt.Workflow/Engine/CompensationOrchestrator.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/CompensationOrchestratorTests.cs`

```csharp
public interface ICompensationOrchestrator
{
    /// Runs compensation actions for a failed branch's completed predecessors, in the order they originally executed.
    Task RunAsync(long runId, long branchId, CancellationToken ct);
}
```

Algorithm:
1. Read history for `(RunId, BranchId)` filtered by `EventKind == NodeCompleted` and `Node.CompensationAction != null`.
2. For each, in original execution order (ascending `HistoryEventId`), invoke a *new* synthetic node execution of the compensation action with the original node's local state (loaded from the history event).
3. Append `History.CompensationExecuted` per step.
4. Failures during compensation are logged but do NOT halt the orchestrator (best-effort, spec §5.5 explicit).

- [ ] **Step 1:** Write failing tests:
  - `Run_invokes_compensations_in_original_order`
  - `Run_continues_on_individual_compensation_failure`
  - `Run_skips_nodes_without_compensation_action`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add sequential CompensationOrchestrator (v1)`.

---

### Task 8.8: BranchLoop wires compensation on branch failure (when configured)

**Spec:** §5.5 — workflow definition opts in via `WorkflowDefinition.RunCompensationOnFailure: true`.

**Files:**
- Modify: `Wbskt.Workflow/Engine/BranchLoop.cs`
- Test: extend `BranchLoopTests`

- [ ] **Step 1:** Write failing test `Branch_failure_invokes_compensation_when_definition_opts_in`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** After `Branch_SetFailed` (Task 5.8) but before counter decrement, check `definition.RunCompensationOnFailure`; if true, `await compensationOrchestrator.RunAsync(runId, branchId)`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): branch loop runs compensation on opt-in`.

---

### Task 8.9: RunFailingEscalation — first branch-fail flips Run to Failing

**Spec:** §8.3 — when any branch fails AND the workflow is configured `failFast: true`, the Run transitions to `Failing`, which cancels remaining branches cooperatively.

**Files:**
- Modify: `Wbskt.Workflow/Engine/BranchLoop.cs` (on `Branch_SetFailed`, check `definition.FailFast`; if true, call `IRunProvider.TransitionStatusAsync(from: Running, to: Failing)` — best-effort, ignore if already terminal).
- Test: extend `BranchLoopTests`

- [ ] **Step 1:** Write failing tests:
  - `Branch_fail_with_failFast_transitions_run_to_Failing`
  - `Branch_fail_without_failFast_keeps_run_Running`
  - `Branch_fail_with_failFast_when_run_already_Cancelling_does_nothing`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): branch loop fail-fast escalation`.

---

### Task 8.10: Inline exception → Fail conversion in BranchLoop

**Spec:** §8.5 — if `RetryExecutor` itself throws (executor crashed, not returned Fail), the loop converts it to `Fail("EXECUTOR_CRASH", ex.Message, Retryable: false, ex)` and runs the standard fail path.

**Files:**
- Modify: `Wbskt.Workflow/Engine/BranchLoop.cs`
- Test: extend `BranchLoopTests`

- [ ] **Step 1:** Write failing test `Loop_converts_thrown_exception_to_NonRetryable_Fail`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Wrap the `RetryExecutor.RunWithRetryAsync` call in try/catch.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): branch loop catches executor crashes`.

---

### Task 8.11: DI registration for Phase 8

**Files:**
- Modify: `Wbskt.Workflow/Extensions/WorkflowServiceCollectionExtensions.cs`
- Test: extend DI smoke test

- [ ] **Step 1:** Failing assertions: `IRunFinalizer`, `IRunCancellationService`, `ICompensationOrchestrator`, `IRunCompletedPublisher` (register as `NullRunCompletedPublisher` placeholder until Phase 11; Phase 11 swaps in the real one). All `Scoped` except cancellation cache which is `Singleton` internally.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Register.
- [ ] **Step 4:** PASS + build.
- [ ] **Step 5: Commit** `(workflow): wire error-model services in DI`.

---

### Task 8.12: End-to-end failure scenario test

**Files:**
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/ErrorModelE2ETests.cs`

End-to-end: workflow with two parallel branches; one fails fatally with `failFast=false, runCompensationOnFailure=true`; assert Run terminates `PartiallyFailed`, compensation ran for the failed branch's predecessors, and the other branch completed normally.

- [ ] **Step 1:** Write failing E2E test.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Wire fixtures.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): error-model E2E test`.

---

**Phase 8 acceptance:**
- [ ] Retries, on-failure outcomes (FailBranch/ContinueAsSucceeded/JumpToNode), cooperative cancellation, fail-fast escalation, PartiallyFailed finalization, and sequential compensation all work end-to-end on stubbed providers.
- [ ] 12 commits on `feat/workflow-engine`.

---

## Phase 9 — Node Executors

**Goal:** Implement every `INodeExecutor` referenced by the spec. Trigger executors are already done in Phase 7. This phase covers the **10 control nodes** and **5 action nodes** = 15 new executors.

**Spec sections:** §5 Control nodes, §5.5 compensation linkage, §1.6 action nodes.

**Pattern (applies to every executor):**
- Live in `Wbskt.Workflow/NodeExecutors/Controls/` or `Wbskt.Workflow/NodeExecutors/Actions/`.
- `Handles` property returns the specific `NodeKind`.
- Tests live alongside in `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Controls/` or `.../Actions/`.
- DI: registered as `INodeExecutor` (Scoped) in Phase 9 Task 9.18.

**Expression evaluator dependency:** Many executors evaluate `WorkflowExpression`. We need an `IExpressionEvaluator` service before the first control-node task.

---

### Task 9.1: IExpressionEvaluator

**Spec:** §1.4 expression types — `LiteralExpression`, `BranchStateRefExpression`, `SharedVariableRefExpression`, `TemplateExpression` (handlebars-style `{{path.to.field}}`), `JsonPathExpression`.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/IExpressionEvaluator.cs`
- Create: `Wbskt.Workflow/Engine/ExpressionEvaluator.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/ExpressionEvaluatorTests.cs`

```csharp
public interface IExpressionEvaluator
{
    Task<JsonElement> EvaluateAsync(WorkflowExpression expr, BranchContext context, CancellationToken ct);
}
```

Evaluation (spec §1.4):
- **Literal** → return `expr.Value` directly.
- **BranchStateRef** → `JsonPath.Select(context.LocalState, expr.Path)`.
- **SharedVariableRef** → load via `ISharedVariableProvider.GetAsync(context.WorkflowDefinitionId, expr.Name)`, return value or `default` if missing.
- **Template** → resolve each `{{path}}` placeholder via BranchStateRef-style lookup and concatenate.
- **JsonPath** → run a JsonPath query against a base expression.

Use `System.Text.Json.JsonElement` natively. For JsonPath use `JsonPath.NET` (already in repo per `Directory.Packages.props`? if not, add).

- [ ] **Step 1:** Write failing tests — one per expression type. Plus `Template_resolves_multiple_placeholders`, `JsonPath_against_BranchStateRef`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add ExpressionEvaluator`.

---

### Task 9.2: LogicNodeExecutor (boolean branching)

**Spec:** §5.1 LogicNode evaluates a `Condition` expression to bool and routes to `truePort` or `falsePort`.

**Files:**
- Create: `Wbskt.Workflow/NodeExecutors/Controls/LogicNodeExecutor.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Controls/LogicNodeExecutorTests.cs`

- [ ] **Step 1:** Write failing tests `True_condition_continues_truePort`, `False_condition_continues_falsePort`, `Non_bool_result_returns_Fail`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add LogicNodeExecutor`.

---

### Task 9.3: VariableNodeExecutor (set local / shared)

**Spec:** §5.2 VariableNode writes a value to local state or shared variable (with CAS retry on shared).

**Files:**
- Create: `Wbskt.Workflow/NodeExecutors/Controls/VariableNodeExecutor.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Controls/VariableNodeExecutorTests.cs`

Spec rules (§5.2):
- `Scope = Local` → emit `Continue("default", { name: evaluatedValue })`.
- `Scope = Shared` → call `ISharedVariableProvider.SetWithCasAsync`; retry up to 3 times on `OptimisticConcurrencyException`; if still failing, return `Fail("SHARED_VAR_CAS_FAILED", ..., Retryable: true)`.

- [ ] **Step 1:** Write failing tests `Local_scope_patches_local_state`, `Shared_scope_persists_with_cas`, `Shared_scope_retries_on_cas_conflict`, `Shared_scope_fails_after_3_retries`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add VariableNodeExecutor`.

---

### Task 9.4: DelayNodeExecutor

**Spec:** §5.3 DelayNode parks the branch with a `TimerWake` bookmark.

**Files:**
- Create: `Wbskt.Workflow/NodeExecutors/Controls/DelayNodeExecutor.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Controls/DelayNodeExecutorTests.cs`

Returns `NodeExecutionResult.WaitForBookmark(new TimerWake(clock.UtcNow + node.Duration), LocalStatePatch: {})`. After resume, `LocalState["wakePayload"]` will contain `{ "wokeAt": <utc> }` injected by the bookmark resumer; the executor doesn't need to do anything special on re-entry — the next step is just `Continue("default")` since the branch loop calls the executor again on resume; therefore the executor checks `context.LocalState.ContainsKey("wakePayload")` to know "we are resuming, just continue" vs "we are arriving, park".

- [ ] **Step 1:** Write failing tests `First_visit_returns_WaitForBookmark_with_TimerWake`, `Resume_visit_returns_Continue_default`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add DelayNodeExecutor`.

---

### Task 9.5: WaitForHttpNodeExecutor

**Spec:** §5.6 — branch parks waiting for an external HTTP call to `/wake/{token}`; optional TTL.

**Files:**
- Create: `Wbskt.Workflow/NodeExecutors/Controls/WaitForHttpNodeExecutor.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Controls/WaitForHttpNodeExecutorTests.cs`

First visit:
1. Generate a wake token (`IIdGenerator.NewCorrelationKey("wake")`).
2. Return `WaitForBookmark(InboundWake(channelKind: "http-wake", correlationKey: $"http-wake:{token}", payloadPredicate: null), Ttl: node.Timeout?, LocalStatePatch: { "wakeToken": token, "wakeUrl": $"/wake/{token}" })`.

Resume:
- If TTL expired (resume came via timer-wake), `Continue("timeoutPort", { ... })`.
- If inbound wake arrived, `Continue("default", { "wakePayload": evt.Payload })`.

Distinguish timer vs inbound resume via the `wakePayload["__wakeKind"]` injected by the resumer (`"timer"` vs `"inbound"`).

- [ ] **Step 1:** Write failing tests `First_visit_creates_wake_url`, `Resume_inbound_continues_default`, `Resume_timer_continues_timeout_port`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. Update `BookmarkResumer` to inject `__wakeKind` into the wake payload (small modification to Task 6.4).
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add WaitForHttpNodeExecutor`.

---

### Task 9.6: AwaitSignalNodeExecutor

**Spec:** §5.7 — branch waits for an explicit named signal sent via the management API.

**Files:**
- Create: `Wbskt.Workflow/NodeExecutors/Controls/AwaitSignalNodeExecutor.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Controls/AwaitSignalNodeExecutorTests.cs`

First visit:
- Return `WaitForBookmark(SignalWake(signalName: node.SignalName, scopeRunRefId: context.RunRefId), Ttl: node.Timeout?, LocalStatePatch: {})`.

Resume:
- Timer-wake → `Continue("timeoutPort", { ... })`.
- Signal-wake → `Continue("default", { "signalPayload": wakePayload })`.

- [ ] **Step 1:** Write failing tests covering both resume paths + first visit.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add AwaitSignalNodeExecutor`.

---

### Task 9.7: FailRunNodeExecutor

**Spec:** §5.8 — explicit termination node; sets Run status to `Failed` directly via terminal result.

**Files:**
- Create: `Wbskt.Workflow/NodeExecutors/Controls/FailRunNodeExecutor.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Controls/FailRunNodeExecutorTests.cs`

Returns `NodeExecutionResult.Fail(node.ErrorCode, evaluatedMessage, Retryable: false, Cause: null)` — the branch loop's `Fail` path will mark the branch failed, and (if `failFast=true` on the workflow) escalate the Run to `Failing`.

If `node.AlsoCancelOtherBranches == true`, additionally call `IRunCancellationService.RequestCancellationAsync(runId, reason: $"FailRun:{errorCode}")` before returning the `Fail`. Spec §5.8 confirms this is the FailRun behavior.

- [ ] **Step 1:** Write failing tests `Returns_Fail_with_configured_error_code`, `Also_cancels_other_branches_when_configured`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add FailRunNodeExecutor`.

---

### Task 9.8: ForEachNodeExecutor (sequential iteration)

**Spec:** §5.9 ForEachNode iterates a list **sequentially**, one item per outer step; uses local state to track iterator position.

**Files:**
- Create: `Wbskt.Workflow/NodeExecutors/Controls/ForEachNodeExecutor.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Controls/ForEachNodeExecutorTests.cs`

Algorithm (spec §5.9):
1. On first visit (no `__foreach_index` in local state):
   - Evaluate `node.Collection` expression to a `JsonArray`.
   - If array is empty, `Continue("emptyPort", { ... })`.
   - Else patch `{ "__foreach_index": 0, "__foreach_total": array.Length, node.IteratorVar: array[0] }` and `Continue("bodyPort", patch)`.
2. On resume visit (the executor sees its own state — the loop is built by edges going back to this node):
   - `index = state["__foreach_index"] + 1`.
   - If `index >= total`, clear iterator state and `Continue("completedPort", { ... })`.
   - Else patch `{ "__foreach_index": index, node.IteratorVar: array[index] }` and `Continue("bodyPort", patch)`.

The "loop back" edge is part of the authored graph — the executor never spawns a child branch.

- [ ] **Step 1:** Write failing tests:
  - `Empty_collection_continues_emptyPort`
  - `First_iteration_patches_index_0_and_continues_bodyPort`
  - `Mid_iteration_increments_index`
  - `Last_iteration_continues_completedPort`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add ForEachNodeExecutor`.

---

### Task 9.9: ParallelForEachNodeExecutor (fan-out)

**Spec:** §5.10 — fans out one child branch per item, with concurrency cap.

**Files:**
- Create: `Wbskt.Workflow/NodeExecutors/Controls/ParallelForEachNodeExecutor.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Controls/ParallelForEachNodeExecutorTests.cs`

First visit:
1. Evaluate `node.Collection` → array of length N.
2. If N == 0, `Continue("emptyPort", { })`.
3. Build N `ForkSpec`s with each child starting at `node.BodyEntryNodeId`, each with `{ node.IteratorVar: array[i], "__pfe_index": i, "__pfe_join_token": joinToken }` where `joinToken = Guid v7`.
4. Persist a `Join` aggregator row (handled by JoinNodeExecutor in Task 9.10) keyed by `joinToken`.
5. Return `NodeExecutionResult.Fork(children, ContinueNodeId: null, patch: { "__pfe_join_token": joinToken, "__pfe_total": N })`.

The parent branch's `Fork` with `ContinueNodeId = null` means it terminates here; the JoinNode is what consolidates results.

Concurrency cap is enforced by the engine's executor scheduler — not by this node. Spec §5.10 says v1 ships an unbounded fan-out and reserves the cap for v2.

- [ ] **Step 1:** Write failing tests:
  - `Empty_collection_continues_emptyPort`
  - `Fans_out_N_children_at_body_entry`
  - `Sets_pfe_join_token_on_parent_state`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add ParallelForEachNodeExecutor`.

---

### Task 9.10: JoinNodeExecutor + Join aggregator table

**Spec:** §5.11 — collects results from N fan-out children and continues once all arrive (with optional quorum).

This task adds a **new table** `JoinAggregators` not covered in Phase 1. Add it now:

**Files:**
- Create: `Databases/Wbskt.Database/Tables/JoinAggregators.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/JoinAggregator_Initialize.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/JoinAggregator_ContributeBranch.sql`
- Create: `Databases/Wbskt.Database/StoredProcedures/JoinAggregator_GetByToken.sql`
- Create: `Wbskt.Workflow.Abstraction/Providers/IJoinAggregatorProvider.cs`
- Create: `Wbskt.Workflow/Providers/JoinAggregatorProvider.cs`
- Create: `Wbskt.Workflow.Abstraction/Engine/JoinAggregatorRow.cs`
- Create: `Wbskt.Workflow/NodeExecutors/Controls/JoinNodeExecutor.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Controls/JoinNodeExecutorTests.cs` + `Tests/.../Providers/JoinAggregatorProviderTests.cs`

`JoinAggregators` table:
| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY PK | |
| JoinToken | UNIQUEIDENTIFIER NOT NULL UNIQUE | Generated by ParallelForEach |
| RunId | BIGINT NOT NULL | FK Runs(Id) |
| ExpectedCount | INT NOT NULL | N |
| ContributedCount | INT NOT NULL DEFAULT 0 | |
| SucceededCount | INT NOT NULL DEFAULT 0 | |
| FailedCount | INT NOT NULL DEFAULT 0 | |
| ContributionsJson | NVARCHAR(MAX) NULL | Array of `{ index, outcome, payload }` |
| ContinueBranchId | BIGINT NULL | Lazy-created when quorum hit |
| ContinueNodeId | NVARCHAR(200) NOT NULL | |
| CreatedAt | DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME() | |

`JoinAggregator_ContributeBranch` uses an UPDATE with `OUTPUT inserted.*` to atomically:
1. Append the contribution JSON.
2. Increment counts.
3. Return whether the quorum / total has now been reached (`shouldContinue` flag).

`JoinNodeExecutor.ExecuteAsync`:
- The executor's parent context's child branch arrives at the Join node. The branch carries `__pfe_join_token` from the ParallelForEach patch.
- Read aggregator; call `ContributeBranchAsync` with the child's outcome (read from `context.LocalState["__pfe_outcome"]` set by the prior step, or default to "succeeded" if last completed normally).
- If `shouldContinue == true`:
  - Spawn a new branch at `node.ContinueNodeId` with `LocalState = { ...node.ContributionsVar: contributions }`.
  - Return `Terminal(Completed)` for the current child branch.
- Else return `Terminal(Completed)` only (waiting for siblings).

Quorum semantics (spec §5.11):
- `Quorum.All` → wait for all N.
- `Quorum.AtLeast(K)` → continue when K succeeded; remaining still contribute but don't trigger a second spawn (`ContinueBranchId IS NULL` check guards).
- `Quorum.AnyFailureFails` → if any child fails, immediately spawn the continuation with `failed` outcome.

- [ ] **Step 1:** Write failing tests:
  - `Aggregator_initialize_creates_row_with_expected_count`
  - `Contribute_increments_count_and_returns_should_continue_when_complete`
  - `JoinExecutor_all_quorum_waits_for_all_contributions`
  - `JoinExecutor_atLeast_quorum_continues_on_K_successes`
  - `JoinExecutor_anyFailureFails_continues_immediately_on_first_failure`
  - `JoinExecutor_does_not_spawn_continue_twice` (idempotency check)
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement table + sprocs + provider + executor.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add JoinNodeExecutor + JoinAggregators table`.

---

### Task 9.11: SubWorkflowNodeExecutor

**Spec:** §5.12 — starts a child Run from another `WorkflowDefinition` and (if synchronous) parks waiting for its completion.

**Files:**
- Create: `Wbskt.Workflow/NodeExecutors/Controls/SubWorkflowNodeExecutor.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Controls/SubWorkflowNodeExecutorTests.cs`

Algorithm:
1. Resolve child workflow via `node.ChildWorkflowDefinitionRefId` (+ optional explicit `Version`, else current).
2. Build a synthetic `InboundEvent { ChannelKind: "manual", CorrelationKey: $"manual:{childRef}", Payload: evaluatedInputMap }`.
3. Call `IRunStarter.StartAsync(childWorkflowDefinitionId, childTriggerNodeId, inboundEvent)`.
4. If `node.Mode == FireAndForget` → `Continue("default", { "childRunRefId": childRunRefId })`.
5. If `node.Mode == Synchronous` → return `WaitForBookmark(ChildRunWake(childRunRefId), Ttl: node.Timeout?, patch: { "childRunRefId": childRunRefId })`.

On resume (Synchronous mode):
- Timer-wake → `Continue("timeoutPort", { })`.
- ChildRunWake → `Continue("default", { "childResult": wakePayload })`.

Dispatch the child via `IRunDispatcher` after `RunStarter`.

- [ ] **Step 1:** Write failing tests:
  - `FireAndForget_continues_default_immediately`
  - `Synchronous_first_visit_parks_with_ChildRunWake`
  - `Synchronous_resume_continues_default_with_child_result`
  - `Synchronous_timer_resume_continues_timeout_port`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add SubWorkflowNodeExecutor`.

---

### Task 9.12: CommandNodeExecutor (action node — send command to a device)

**Spec:** §1.6 action nodes; CommandNode publishes a downstream event via `Wbskt.EventBus`.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/IWorkflowActionPublisher.cs`
- Create: `Wbskt.Workflow/Engine/WorkflowActionPublisher.cs` (wraps `IBus` from MassTransit, lives in Engine layer; Phase 11 hooks up the bus configuration)
- Create: `Wbskt.Workflow/NodeExecutors/Actions/CommandNodeExecutor.cs`
- Create: `Events/Wbskt.Events/WorkflowEngine/CommandIssuedEvent.cs` *(re-introducing one workflow event — but only this one, scoped to action dispatch, not the old contract bus)*
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Actions/CommandNodeExecutorTests.cs`

```csharp
public interface IWorkflowActionPublisher
{
    Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct) where TEvent : class;
}
```

`CommandNodeExecutor`:
1. Idempotency claim via `IIdempotencyKeyProvider.TryClaimAsync(runId, branchId, nodeId, attempt, sideEffectKey: $"command:{evaluatedDeviceSerial}:{evaluatedCommand}")`.
2. If duplicate, skip publish; `Continue("default", { "issued": false })`.
3. Else build `CommandIssuedEvent(deviceSerial, commandKind, payload, runRefId, branchId)` and publish via `IWorkflowActionPublisher`.
4. `Continue("default", { "issued": true })`.

- [ ] **Step 1:** Write failing tests:
  - `Publishes_command_on_first_attempt`
  - `Skips_publish_on_duplicate_idempotency_key`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. `WorkflowActionPublisher` impl just stores into an in-memory bus stub until Phase 11.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add CommandNodeExecutor`.

---

### Task 9.13: WebhookActionNodeExecutor (outbound HTTP)

**Spec:** §1.6 action node — POSTs to an external URL.

**Files:**
- Create: `Wbskt.Workflow/NodeExecutors/Actions/WebhookActionNodeExecutor.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Actions/WebhookActionNodeExecutorTests.cs`

Algorithm:
1. Idempotency claim.
2. Evaluate `node.Url`, `node.Headers`, `node.Body` expressions.
3. `HttpClient.PostAsync` (inject `IHttpClientFactory` via `INodeExecutionServices` — extend the interface; named client `"workflow-webhook"` configured with 30s timeout).
4. Read response: `{ "status": int, "body": str }` patched into local state.
5. If `response.IsSuccessStatusCode` → `Continue("default", { "response": ... })`.
6. Else → `Fail("WEBHOOK_HTTP_ERROR", $"{status}: {body}", Retryable: status >= 500, Cause: null)`.

Extending `INodeExecutionServices` to add `IHttpClientFactory HttpClientFactory { get; }`: modify the interface from Task 4.4. Update all prior tests that build a stub `INodeExecutionServices`.

- [ ] **Step 1:** Write failing tests:
  - `Posts_to_url_with_evaluated_headers_and_body`
  - `2xx_response_continues_default`
  - `4xx_response_fails_non_retryable`
  - `5xx_response_fails_retryable`
  - `Network_exception_fails_retryable`
  - `Duplicate_idempotency_skips_post`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Extend `INodeExecutionServices`, implement executor.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add WebhookActionNodeExecutor`.

---

### Task 9.14: EmailNodeExecutor

**Spec:** §1.6 EmailAction — sends via SMTP.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/IEmailSender.cs`
- Create: `Wbskt.Workflow/Engine/SmtpEmailSender.cs` *(uses built-in `System.Net.Mail.SmtpClient` — "native first" rule)*
- Create: `Wbskt.Workflow/NodeExecutors/Actions/EmailNodeExecutor.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Actions/EmailNodeExecutorTests.cs`

```csharp
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
public sealed record EmailMessage(string From, IReadOnlyList<string> To, string Subject, string BodyHtml);
```

SMTP options bound from `Config/email.json` (new file): host, port, username, password, useSsl.

Executor: standard idempotency-claim → evaluate to/subject/body → send → continue. Failures map to `Fail` (retryable on transient SMTP errors, non-retryable on auth / permanent reject — `SmtpStatusCode` distinguishes).

- [ ] **Step 1:** Write failing tests:
  - `Sends_email_with_evaluated_fields`
  - `Transient_smtp_error_fails_retryable`
  - `Permanent_smtp_error_fails_non_retryable`
  - `Duplicate_idempotency_skips_send`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. Bind options via `builder.Services.AddOptions<EmailOptions>().Bind(config.GetSection("Email"))`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add EmailNodeExecutor`.

---

### Task 9.15: TelegramNodeExecutor

**Spec:** §1.6 TelegramAction — POSTs to Telegram Bot API.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/ITelegramSender.cs`
- Create: `Wbskt.Workflow/Engine/TelegramSender.cs`
- Create: `Wbskt.Workflow/NodeExecutors/Actions/TelegramNodeExecutor.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Actions/TelegramNodeExecutorTests.cs`

```csharp
public interface ITelegramSender
{
    Task SendAsync(string chatId, string text, CancellationToken ct);
}
```

`TelegramSender` uses `HttpClient` to POST `https://api.telegram.org/bot{token}/sendMessage`. Bot token from `Config/telegram.json`.

Same pattern as Email/Webhook: idempotency → evaluate → send → continue / fail.

- [ ] **Step 1:** Write failing tests `Sends_message_to_evaluated_chat`, `4xx_fails_non_retryable`, `5xx_fails_retryable`, `Duplicate_idempotency_skips`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add TelegramNodeExecutor`.

---

### Task 9.16: ToastNodeExecutor

**Spec:** §1.6 ToastAction — publishes an in-system notification via the Socket Host's existing client channel.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/IToastPublisher.cs`
- Create: `Wbskt.Workflow/Engine/ToastPublisher.cs` *(publishes a `ToastDeliveredEvent` via `IWorkflowActionPublisher` — Socket Host consumes and forwards over WebSocket)*
- Create: `Events/Wbskt.Events/WorkflowEngine/ToastDeliveredEvent.cs`
- Create: `Wbskt.Workflow/NodeExecutors/Actions/ToastNodeExecutor.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/NodeExecutors/Actions/ToastNodeExecutorTests.cs`

Same pattern. Idempotency → evaluate (target user RefId, severity, message) → publish event → continue.

- [ ] **Step 1:** Write failing tests `Publishes_toast_event`, `Duplicate_idempotency_skips`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add ToastNodeExecutor`.

---

### Task 9.17: End-to-end test — multi-node workflow with mixed control + action

**Files:**
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/MixedExecutorE2ETests.cs`

Drive a workflow `[ManualTrigger] -> [Logic if temp > 30] -> truePort: [Toast] -> [Delay 1s] -> [End], falsePort: [End]` end-to-end. Stub `IToastPublisher` to assert it was called; use a fake `IClock` to fast-forward through Delay.

- [ ] **Step 1:** Write failing E2E test.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Wire fixtures (extend `InMemoryProviders`).
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): mixed executor E2E test`.

---

### Task 9.18: DI registration for Phase 9

**Files:**
- Modify: `Wbskt.Workflow/Extensions/WorkflowServiceCollectionExtensions.cs`
- Test: extend DI smoke test

- [ ] **Step 1:** Failing assertions:
  - `IExpressionEvaluator` (Scoped)
  - `IJoinAggregatorProvider` (Scoped)
  - `IWorkflowActionPublisher`, `IEmailSender`, `ITelegramSender`, `IToastPublisher` (Scoped)
  - All 15 new executors registered as `INodeExecutor` (Scoped) — including via `services.AddScoped<INodeExecutor, LogicNodeExecutor>()`-style registrations.
  - The DI smoke test now resolves `IEnumerable<INodeExecutor>` and asserts there are exactly **19** registered (4 triggers from Phase 7 + 10 controls + 5 actions).
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Register all + bind options for SMTP/Telegram from `IConfiguration`.
- [ ] **Step 4:** PASS + build.
- [ ] **Step 5: Commit** `(workflow): wire all node executors in DI`.

---

**Phase 9 acceptance:**
- [ ] Every node-kind in the spec catalog has an executor + tests.
- [ ] Idempotency claims are present on every side-effecting action node.
- [ ] DI smoke test confirms all 19 executors are registered.
- [ ] 18 commits on `feat/workflow-engine`.

---

## Phase 10 — Background Services + ILeaseHolder

**Goal:** Wire the **8 hosted services** spec §7.3 lists, plus the `AlwaysHoldsLeaseHolder` (v1) per spec §7.2. Several are already created in earlier phases — this phase ensures they're properly registered, configured, started, and gracefully shut down.

**The 8 background services (spec §7.3):**
1. `BranchExecutionPump` — Phase 5 Task 5.10
2. `BookmarkScheduler` — Phase 6 Tasks 6.2, 6.7
3. `ScheduledFireTicker` — **new in this phase**
4. `RunReaper` — **new in this phase**
5. `HistoryRetentionGc` — **new in this phase**
6. `PendingTriggerEventBacklogReaper` — **new in this phase**
7. `RunRecoveryService` — **new in this phase**
8. `MetricsExporter` — **new in this phase**

---

### Task 10.1: ILeaseHolder + AlwaysHoldsLeaseHolder

**Spec:** §7.2 — v1 ships always-on lease; v2 will swap in `SqlLeaseHolder`.

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Engine/ILeaseHolder.cs`
- Create: `Wbskt.Workflow/Engine/AlwaysHoldsLeaseHolder.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/AlwaysHoldsLeaseHolderTests.cs`

```csharp
public interface ILeaseHolder
{
    Task<bool> TryAcquireAsync(string leaseName, CancellationToken ct);
    Task ReleaseAsync(string leaseName, CancellationToken ct);
    Task<bool> IsHeldAsync(string leaseName, CancellationToken ct);
}
```

`AlwaysHoldsLeaseHolder` returns `true` for every Acquire / IsHeld and no-ops Release. Background services gate their work behind `IsHeldAsync` so swapping to `SqlLeaseHolder` in v2 is a no-touch change.

- [ ] **Step 1:** Write failing tests `Acquire_always_returns_true`, `IsHeld_always_returns_true`, `Release_does_not_throw`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add AlwaysHoldsLeaseHolder (v1)`.

---

### Task 10.2: ScheduledFireTicker

**Spec:** §3.5, §7.3 — polls `ScheduledFires` for due rows and synthesizes inbound `ScheduleTrigger` events.

**Files:**
- Create: `Hosts/Wbskt.Workflow.Engine.Host/HostedServices/ScheduledFireTicker.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/HostedServices/ScheduledFireTickerTests.cs`

Polls every 1 second (`PeriodicTimer`). Each tick:
1. `if (!leaseHolder.IsHeldAsync("schedule-tick")) return;`
2. `IScheduledFireProvider.LeaseDueAsync(now, 64, hostId, lease=2min)`.
3. For each leased fire: synthesize `InboundEvent(ChannelKind:"schedule", CorrelationKey:$"schedule:{fire.Id}", Payload:{ "scheduledFireId": fire.Id, "definitionRefId": fire.WorkflowDefinitionRefId, "fireAt": fire.NextFireAt })`. Call `IInboundHub.HandleAsync`.
4. After successful dispatch, advance via `AdvanceNextAsync(fire.Id, nextOccurrence)` where `nextOccurrence = Cronos.Parse(fire.CronExpression).GetNextOccurrence(now)`. If `null` (one-shot fire), `DeleteAllByWorkflowDefinitionIdAsync` is overkill — instead add `Task DeleteByIdAsync(long id, ct)` to the provider.

- [ ] **Step 1:** Write failing tests:
  - `Tick_no_lease_skips`
  - `Tick_leases_due_and_dispatches_inbound`
  - `Tick_advances_recurring_fire_to_next_occurrence`
  - `Tick_deletes_one_shot_fire_after_dispatch`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement + add `IScheduledFireProvider.DeleteByIdAsync` + sproc.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add ScheduledFireTicker hosted service`.

---

### Task 10.3: RunReaper

**Spec:** §7.3 — detects "stuck" Runs (no branch activity for >N minutes) and marks them `Failed("REAPER_TIMEOUT")` to unblock finalizer.

**Files:**
- Create: `Hosts/Wbskt.Workflow.Engine.Host/HostedServices/RunReaper.cs`
- Modify: `Wbskt.Workflow.Abstraction/Providers/IRunProvider.cs` — add `Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);`
- Add sproc: `Databases/Wbskt.Database/StoredProcedures/Run_GetStuck.sql` — selects Runs in `Running`/`Cancelling`/`Failing` whose latest `HistoryEvent.CreatedAt < cutoffUtc`.
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/HostedServices/RunReaperTests.cs`

Polls every 5 minutes. For each stuck Run, calls `IRunCancellationService.RequestCancellationAsync(runId, reason:"REAPER_TIMEOUT")` — cooperative cancel rather than force-terminal (lets active branches observe and clean up).

The "stuck" threshold is configurable via `WorkflowEngineOptions.RunStuckThreshold` (default: 30 minutes).

- [ ] **Step 1:** Write failing tests:
  - `Tick_skips_when_lease_not_held`
  - `Tick_requests_cancellation_for_each_stuck_run`
  - `Tick_does_nothing_when_no_stuck_runs`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add RunReaper hosted service`.

---

### Task 10.4: HistoryRetentionGc

**Spec:** §6.3 retention — delete history events for terminal Runs older than retention window (default 30 days).

**Files:**
- Create: `Hosts/Wbskt.Workflow.Engine.Host/HostedServices/HistoryRetentionGc.cs`
- Add sproc: `Databases/Wbskt.Database/StoredProcedures/HistoryEvent_DeleteForRetiredRuns.sql` (deletes `TOP (5000)` per call to keep transactions small).
- Modify: `IHistoryEventProvider` — add `Task<int> DeleteForRetiredRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/HostedServices/HistoryRetentionGcTests.cs`

Polls every 1 hour. Loops batched deletes until fewer than `batchSize` rows were deleted (i.e., caught up).

- [ ] **Step 1:** Write failing tests `Tick_loops_until_caught_up`, `Tick_skips_when_lease_not_held`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add HistoryRetentionGc hosted service`.

---

### Task 10.5: PendingTriggerEventBacklogReaper

**Spec:** §4.6 — pending trigger events should auto-expire if they outlive their reasonable window (e.g., the workflow was deprecated, or backlog is stuck).

**Files:**
- Create: `Hosts/Wbskt.Workflow.Engine.Host/HostedServices/PendingTriggerEventBacklogReaper.cs`
- Add sproc: `Databases/Wbskt.Database/StoredProcedures/PendingTriggerEvent_DeleteExpired.sql`
- Modify: `IPendingTriggerEventProvider` — add `Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/HostedServices/PendingTriggerEventBacklogReaperTests.cs`

Polls every 10 minutes. Default expiry window: 24 hours (configurable).

- [ ] **Step 1:** Write failing tests `Tick_deletes_expired_events`, `Tick_skips_when_lease_not_held`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add PendingTriggerEventBacklogReaper`.

---

### Task 10.6: RunRecoveryService

**Spec:** §7.5 startup recovery — on host startup, find all branches that were `Running` on this host (or any host, since v1 is single-host), re-dispatch them via `IRunDispatcher`.

**Files:**
- Create: `Hosts/Wbskt.Workflow.Engine.Host/HostedServices/RunRecoveryService.cs`
- Modify: `IBranchProvider` — add `Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct);`
- Add sproc: `Databases/Wbskt.Database/StoredProcedures/Branch_GetRunning.sql`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/HostedServices/RunRecoveryServiceTests.cs`

`RunRecoveryService` implements `IHostedService` (NOT `BackgroundService` — runs once at startup, not on a loop). In `StartAsync`:
1. Wait until lease is held.
2. Load all branches with `Status = Running`.
3. For each, `IRunDispatcher.DispatchAsync(BranchExecutionRequest(runId, branchId, Reason: BookmarkResumed))`.
4. Log count recovered.

Empty `StopAsync`.

- [ ] **Step 1:** Write failing tests `Start_dispatches_all_running_branches`, `Start_logs_zero_recovery_when_none`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add RunRecoveryService`.

---

### Task 10.7: MetricsExporter (Prometheus-style counters via .NET Meters)

**Spec:** §7.6 metrics — emit gauge/counter values for monitoring.

**Files:**
- Create: `Wbskt.Workflow/Telemetry/WorkflowMetrics.cs` — wraps a `System.Diagnostics.Metrics.Meter` with named instruments
- Create: `Hosts/Wbskt.Workflow.Engine.Host/HostedServices/MetricsExporter.cs` — periodically reads counter values from providers and updates gauges
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Telemetry/WorkflowMetricsTests.cs`

Metrics (spec §7.6):
- `workflow.runs.active` (gauge) — from `IRunProvider.CountByStatusAsync(Running)`. Add provider method + sproc `Run_CountByStatus`.
- `workflow.branches.active` (gauge) — from `RunCounters` aggregate sum (add `Task<long> SumActiveBranchesAsync(CancellationToken ct)`).
- `workflow.bookmarks.parked` (gauge) — `IBookmarkProvider.CountAsync(CancellationToken ct)`.
- `workflow.pending_triggers.depth` (gauge) — `IPendingTriggerEventProvider.CountAllAsync(CancellationToken ct)`.
- `workflow.runs.completed` (counter) — incremented by `RunFinalizer` on terminal transitions (modify `RunFinalizer` to inject `WorkflowMetrics`).
- `workflow.branches.dispatched` (counter) — incremented by `ChannelRunDispatcher.DispatchAsync` (modify).
- `workflow.history_events.appended` (counter) — incremented by `HistoryEventProvider.AppendBatchAsync` (modify).
- `workflow.node.executions` (counter, tagged by `nodeKind`, `outcome`) — incremented by `BranchLoop` after each executor step.
- `workflow.node.duration_ms` (histogram, tagged by `nodeKind`) — recorded by `BranchLoop`.

The `MetricsExporter` polls every 15 seconds to update the gauges from DB-backed sources. Counters/histograms are incremented inline by the existing services.

- [ ] **Step 1:** Write failing tests:
  - `Meter_publishes_active_runs_gauge`
  - `Counter_increments_on_completed_run`
  - `Histogram_records_node_duration`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. Use `Meter("Wbskt.Workflow", "1.0.0")`. For testing, use `MeterListener` to capture instrument values.
- [ ] **Step 4:** Add metrics increments to `RunFinalizer`, `ChannelRunDispatcher`, `HistoryEventProvider`, `BranchLoop`. Run all prior tests — they should still pass (no behavior change, just side effects).
- [ ] **Step 5: Commit** `(workflow): add WorkflowMetrics + MetricsExporter`.

---

### Task 10.8: WorkflowEngineOptions

**Files:**
- Create: `Wbskt.Workflow.Abstraction/Configuration/WorkflowEngineOptions.cs`
- Create: `Config/workflow-engine.json` (default values; loaded by Engine Host)
- Modify: `Hosts/Wbskt.Workflow.Engine.Host/Program.cs` — `builder.Configuration.AddJsonFile("../../Config/workflow-engine.json", optional: true);`
- Modify: `Wbskt.Workflow/Extensions/WorkflowServiceCollectionExtensions.cs` — `services.AddOptions<WorkflowEngineOptions>().Bind(config.GetSection("Workflow"));`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Configuration/WorkflowEngineOptionsTests.cs`

```csharp
public sealed class WorkflowEngineOptions
{
    public TimeSpan BookmarkPollInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan BookmarkOrphanGcInterval { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ScheduleTickInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan RunReaperInterval { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan RunStuckThreshold { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan HistoryRetentionInterval { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan HistoryRetentionWindow { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan PendingTriggerEventBacklogInterval { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan PendingTriggerEventTtl { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan MetricsExportInterval { get; init; } = TimeSpan.FromSeconds(15);
    public int LeaseDurationSeconds { get; init; } = 120;
    public int BookmarkLeaseBatchSize { get; init; } = 64;
    public int ScheduledFireLeaseBatchSize { get; init; } = 64;
}
```

Refactor each hosted service from earlier tasks to read its interval from this options object via `IOptionsMonitor<WorkflowEngineOptions>` instead of hard-coded constants.

- [ ] **Step 1:** Write failing test `Options_loaded_from_config_section` (drives a `ConfigurationBuilder` with an in-memory JSON, asserts properties hydrate).
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Add the options class + binding + refactor hosted services.
- [ ] **Step 4:** PASS + run ALL prior tests to confirm no regressions.
- [ ] **Step 5: Commit** `(workflow): add WorkflowEngineOptions`.

---

### Task 10.9: DI registration for Phase 10

**Files:**
- Modify: `Wbskt.Workflow/Extensions/WorkflowServiceCollectionExtensions.cs`
- Modify: `Hosts/Wbskt.Workflow.Engine.Host/Program.cs` — register all 8 hosted services.
- Test: extend DI smoke test

- [ ] **Step 1:** Failing assertions:
  - `ILeaseHolder` resolves to `AlwaysHoldsLeaseHolder` (Singleton)
  - `WorkflowMetrics` (Singleton)
  - The Engine Host's `IHost.Services` collection includes hosted-service types for all 8 (assert via `IEnumerable<IHostedService>` count).
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Register.
- [ ] **Step 4:** PASS + `dotnet build`.
- [ ] **Step 5: Commit** `(workflow): wire background services in DI`.

---

### Task 10.10: Graceful shutdown integration test

**Files:**
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/HostedServices/GracefulShutdownTests.cs`

Start the Engine Host's `IHost`, let services run for 2 seconds, call `host.StopAsync(timeout:30s)`, assert all `IHostedService.StopAsync` calls completed without throwing and within the timeout.

- [ ] **Step 1:** Write failing test.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Ensure every hosted service's `StopAsync` cooperates with the host cancellation token (already enforced by `BackgroundService` base class; verify `RunRecoveryService.StopAsync` is no-op).
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): graceful shutdown test`.

---

**Phase 10 acceptance:**
- [ ] All 8 hosted services registered and start cleanly.
- [ ] Lease-gated where appropriate (every poller except `BranchExecutionPump` and `RunRecoveryService`).
- [ ] Options-driven intervals throughout.
- [ ] Metrics flowing.
- [ ] Graceful shutdown verified.
- [ ] 10 commits on `feat/workflow-engine`.

---

## Phase 11 — Inbound Adapters

**Goal:** Translate external triggers into `IInboundHub.HandleAsync(InboundEvent)` calls. Adapters live in the Engine Host (no engine code depends on them). Five adapters (spec §4.7):
1. **RabbitMQ device-payload consumer** — listens for `DevicePayloadReceived` events from the Socket Host.
2. **HTTP wake controller** — `POST /wake/{token}` resolves a `WaitForHttp` bookmark.
3. **HTTP webhook controller** — `POST /hooks/{path}` fires a `WebhookTrigger`.
4. **HTTP manual-start + signal controller** — operator APIs `POST /runs` (manual), `POST /runs/{runRefId}/signals/{name}` (signal).
5. **ChildRunCompleted internal hook** — wired inside the Engine Host's `RunFinalizer` to synthesize a `child-completed` inbound event.

**Plus:** real `WorkflowActionPublisher` swapped from Phase 9's stub to a MassTransit-backed publisher, and `RunCompletedPublisher` for downstream consumers.

---

### Task 11.1: MassTransit configuration in Engine Host

**Spec:** §7.4. Repo's existing pattern: see `Hosts/Wbskt.Management.Host/Program.cs` MassTransit setup.

**Files:**
- Create: `Hosts/Wbskt.Workflow.Engine.Host/Extensions/MassTransitExtensions.cs`
- Modify: `Hosts/Wbskt.Workflow.Engine.Host/Program.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Extensions/MassTransitExtensionsTests.cs` (smoke test only — assert `IBus` resolves)

`AddWorkflowMassTransit(this IServiceCollection, IConfiguration)`:
- Configure RabbitMQ from `Config/rabbitmq.json`.
- Register consumers added in later tasks.
- Add `IBus` to DI.

- [ ] **Step 1:** Write failing smoke test `ServiceProvider_resolves_IBus`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. Mirror Management Host's pattern verbatim — same exchange names, same rabbitmq.json keys.
- [ ] **Step 4:** PASS + build.
- [ ] **Step 5: Commit** `(workflow): wire MassTransit in Engine Host`.

---

### Task 11.2: Replace WorkflowActionPublisher stub with MassTransit-backed impl

**Files:**
- Modify: `Wbskt.Workflow/Engine/WorkflowActionPublisher.cs` — wrap `IBus.Publish`.
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/WorkflowActionPublisherTests.cs`

- [ ] **Step 1:** Write failing test `PublishAsync_calls_IBus_Publish` using Moq for `IBus`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Replace stub.
- [ ] **Step 4:** PASS. Run **all** prior tests that referenced action executors — they should still pass (publisher is mocked).
- [ ] **Step 5: Commit** `(workflow): MassTransit-backed action publisher`.

---

### Task 11.3: RunCompletedPublisher (real impl)

**Files:**
- Modify: `Wbskt.Workflow.Abstraction/Engine/IRunCompletedPublisher.cs` (from Phase 8 Task 8.5)
- Create: `Wbskt.Workflow/Engine/RunCompletedPublisher.cs`
- Create: `Events/Wbskt.Events/WorkflowEngine/RunCompletedEvent.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/RunCompletedPublisherTests.cs`

`RunCompletedEvent`: `Guid RunRefId, Guid WorkflowDefinitionRefId, int Version, RunStatus TerminalStatus, DateTime CompletedAt, string CorrelationKey`.

`RunCompletedPublisher.PublishAsync(runId)` loads the Run row + publishes the event via `IBus`.

- [ ] **Step 1:** Write failing test `PublishAsync_loads_run_and_publishes_event`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement; replace `NullRunCompletedPublisher` registration from Phase 8 Task 8.11.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): real RunCompletedPublisher`.

---

### Task 11.4: DevicePayloadInboundConsumer

**Spec:** §4.7.1.

**Files:**
- Create: `Hosts/Wbskt.Workflow.Engine.Host/InboundAdapters/DevicePayloadInboundConsumer.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/InboundAdapters/DevicePayloadInboundConsumerTests.cs`

MassTransit `IConsumer<DevicePayloadReceivedEvent>` — the event already exists in `Events/Wbskt.Events` (per repo's current Socket Host → Workflow Engine flow). Read its current shape; if missing, define it minimally:

```csharp
public sealed record DevicePayloadReceivedEvent(Guid DeviceRefId, string DeviceSerial, string PayloadType, JsonElement Payload, DateTime ReceivedAt);
```

Consumer:
1. Build `InboundEvent(ChannelKind:"device", CorrelationKey:resolved later by hub, InboundEventId:$"device:{deviceSerial}:{guid}", Payload:{ "deviceSerial":..., "payloadType":..., "payload":... })`.
2. Call `IInboundHub.HandleAsync`.
3. Honor the consumer's `CancellationToken`.

- [ ] **Step 1:** Write failing test `Consumer_invokes_inbound_hub_with_correct_event`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement + register in MassTransit config (Task 11.1).
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add DevicePayloadInboundConsumer`.

---

### Task 11.5: HTTP wake controller — POST /wake/{token}

**Spec:** §4.7.2.

**Files:**
- Create: `Hosts/Wbskt.Workflow.Engine.Host/Controllers/WakeController.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Controllers/WakeControllerTests.cs`

```csharp
[ApiController]
[Route("wake")]
public sealed class WakeController(IInboundHub hub) : ControllerBase
{
    [HttpPost("{token}")]
    public async Task<WakeResponse> Wake(string token, [FromBody] JsonElement payload, CancellationToken ct);
}

public sealed record WakeResponse(bool Matched, string Outcome);
```

Builds `InboundEvent(ChannelKind:"http-wake", CorrelationKey:$"http-wake:{token}", InboundEventId:$"wake:{token}:{Guid.CreateVersion7()}", Payload:{ "wakeToken":token, "body":payload })`, calls hub, maps `TriggerDispatchResult` → `WakeResponse`.

- [ ] **Step 1:** Write failing test `Post_wake_token_returns_Matched_when_bookmark_existed` using `WebApplicationFactory<Program>` + mocked `IInboundHub`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add WakeController`.

---

### Task 11.6: HTTP webhook controller — POST /hooks/{path}

**Spec:** §4.7.3.

**Files:**
- Create: `Hosts/Wbskt.Workflow.Engine.Host/Controllers/WebhooksController.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Controllers/WebhooksControllerTests.cs`

```csharp
[ApiController]
[Route("hooks")]
public sealed class WebhooksController(IInboundHub hub) : ControllerBase
{
    [HttpPost("{*path}")]
    public async Task<WebhookResponse> Receive(string path, [FromBody] JsonElement body, [FromQuery] Dictionary<string, string> query, CancellationToken ct);
}
```

Build `InboundEvent(ChannelKind:"webhook", InboundEventId:$"webhook:{path}:{Guid v7}", Payload:{ "webhookPath":path, "body":body, "query":query })`.

The `{*path}` catch-all matches any subpath (`/hooks/github/push`, `/hooks/stripe/payment-succeeded`, etc.).

- [ ] **Step 1:** Write failing test `Post_to_webhook_path_calls_hub_with_path_in_payload`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add WebhooksController`.

---

### Task 11.7: Manual-start + signal controller

**Spec:** §4.7.4 + §5.7 signal API.

**Files:**
- Create: `Hosts/Wbskt.Workflow.Engine.Host/Controllers/RunsController.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Controllers/RunsControllerTests.cs`

```csharp
[ApiController]
[Route("runs")]
public sealed class RunsController(IInboundHub hub, IRunCancellationService cancellation, IRunProvider runs) : ControllerBase
{
    [HttpPost]
    public async Task<StartRunResponse> Start([FromBody] StartRunRequest request, CancellationToken ct);

    [HttpPost("{runRefId:guid}/signals/{signalName}")]
    public async Task<SignalResponse> Signal(Guid runRefId, string signalName, [FromBody] JsonElement payload, CancellationToken ct);

    [HttpPost("{runRefId:guid}/cancel")]
    public async Task<CancelResponse> Cancel(Guid runRefId, [FromBody] CancelRequest request, CancellationToken ct);
}

public sealed record StartRunRequest(Guid WorkflowDefinitionRefId, JsonElement Input);
public sealed record StartRunResponse(Guid RunRefId);
public sealed record SignalResponse(bool Delivered);
public sealed record CancelRequest(string Reason);
public sealed record CancelResponse(bool Cancelled);
```

`Start`: build manual InboundEvent → hub → return `RunRefId` from result.
`Signal`: build signal InboundEvent (ChannelKind="signal") → hub → return `Delivered = (result.Outcome == ResumedBookmark)`.
`Cancel`: resolve `runRefId` via `RunProvider.GetByRefIdAsync` → `cancellation.RequestCancellationAsync(runId, request.Reason)`.

Apply `[Authorize]` per repo conventions (controllers in other hosts are `[Authorize]`; verify the pattern from `Hosts/Wbskt.Management.Host/Controllers/`).

- [ ] **Step 1:** Write failing tests — one per endpoint (3 tests).
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): add RunsController`.

---

### Task 11.8: ChildRunCompleted internal hook

**Spec:** §5.12 — when a child Run completes, the engine synthesizes an inbound event so parent `SubWorkflow` bookmarks can wake.

**Files:**
- Modify: `Wbskt.Workflow/Engine/RunFinalizer.cs` — after publishing `RunCompletedEvent`, if the Run has a parent reference, also call `IInboundHub.HandleAsync` with `InboundEvent(ChannelKind:"child-completed", CorrelationKey:$"child-completed:{runRefId}", Payload:{ "runRefId":..., "status":terminal, "output":... })`.
- Modify: `Wbskt.Workflow.Abstraction/Providers/IRunProvider.cs` + `RunRow` — add `Guid? ParentRunRefId`, `long? ParentBranchId` columns (and update Phase 1 `Runs` table sproc/columns — yes, this is a back-edit to Phase 1; cite the change in the commit).
- Modify: `Databases/Wbskt.Database/Tables/Runs.sql` + `Run_Insert` + `Run_GetById` + `Run_GetBy_RefId` to include the parent columns.
- Modify: `RunStarter.StartAsync` to accept optional `(Guid parentRunRefId, long parentBranchId)` and write them. `SubWorkflowNodeExecutor` (Phase 9 Task 9.11) updates: when starting the child, pass the parent ids.
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/Engine/ChildRunCompletedHookTests.cs`

- [ ] **Step 1:** Write failing tests:
  - `Finalizer_synthesizes_child_completed_inbound_when_parent_set`
  - `Finalizer_does_not_synthesize_when_parent_not_set`
  - `RunStarter_persists_parent_run_ref_id_when_provided`
  - `SubWorkflow_executor_passes_parent_ids_to_starter`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement the column additions + sproc updates + back-references. Update prior tests that constructed `RunRow` without the new columns (use a `RunRow.Default` factory or default values).
- [ ] **Step 4:** PASS + `dotnet build`.
- [ ] **Step 5: Commit** `(workflow): wire ChildRunCompleted hook`.

---

### Task 11.9: HTTP outbound webhook — replace `HttpClient.PostAsync` in WebhookActionNodeExecutor

The Phase 9 Task 9.13 stub used `IHttpClientFactory` already. This task ensures it's configured properly here.

**Files:**
- Modify: `Hosts/Wbskt.Workflow.Engine.Host/Program.cs` — `builder.Services.AddHttpClient("workflow-webhook", c => { c.Timeout = TimeSpan.FromSeconds(30); });`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/InboundAdapters/HttpClientFactoryConfigurationTests.cs`

- [ ] **Step 1:** Write failing test `Named_client_has_30s_timeout`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Add `AddHttpClient` configuration.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): configure outbound webhook HttpClient`.

---

### Task 11.10: End-to-end adapter test — RabbitMQ device payload to terminal Run

**Files:**
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/InboundAdapters/DeviceToRunE2ETests.cs`

Using MassTransit's in-memory test harness:
1. Publish `DevicePayloadReceivedEvent`.
2. Consumer fires `InboundHub.HandleAsync`.
3. TriggerDispatcher matches registration, starts Run.
4. BranchLoop drives through trigger → logic → toast action → terminal.
5. Assert: `Run.Status == Succeeded`, `IToastPublisher` was invoked, expected history events present, `RunCompletedEvent` was published on the bus harness.

- [ ] **Step 1:** Write failing E2E test using `MassTransit.Testing.InMemoryTestHarness`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Stitch fixtures. Use in-memory providers for SQL.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(workflow): inbound adapter E2E test`.

---

**Phase 11 acceptance:**
- [ ] MassTransit wired; 4 controllers + 1 consumer + child-completed hook live.
- [ ] All E2E paths pass with in-memory bus harness + in-memory providers.
- [ ] 10 commits on `feat/workflow-engine`.

---

## Phase 12 — Management Host Wiring

**Goal:** Restore (cleanly, not reviving old code) the workflow **authoring + operator REST surface** in the Management Host that Phase 0 wiped. This is the surface that GUIs / CLIs use to publish workflows, list runs, query history.

**Spec sections:** §1.7 publish lifecycle, §6.4 operator/observability APIs.

---

### Task 12.1: Management Host project reference + DTO models

**Files:**
- Modify: `Hosts/Wbskt.Management.Host/Wbskt.Management.Host.csproj` — add `<ProjectReference>` to `Wbskt.Workflow.Abstraction` (for `WorkflowDefinition` deserialization) and to `Wbskt.Workflow` (for `IWorkflowDefinitionProvider`, `ITriggerRegistrationService`, `IRunProvider`, `IHistoryEventProvider`, `IRunCancellationService`).
- Create: `Models/Wbskt.Management.Models/Workflow/WorkflowPublishRequest.cs`
- Create: `Models/Wbskt.Management.Models/Workflow/WorkflowPublishResponse.cs`
- Create: `Models/Wbskt.Management.Models/Workflow/WorkflowDefinitionDto.cs`
- Create: `Models/Wbskt.Management.Models/Workflow/RunSummaryDto.cs`
- Create: `Models/Wbskt.Management.Models/Workflow/RunDetailDto.cs`
- Create: `Models/Wbskt.Management.Models/Workflow/HistoryEventDto.cs`
- Test: build only (DTOs have no behavior).

`WorkflowPublishRequest`: `Guid RefId, JsonElement Definition` (the full `WorkflowDefinition` JSON).
`WorkflowPublishResponse`: `Guid RefId, int Version, string Status`.
`WorkflowDefinitionDto`: `Guid RefId, int Version, string Status, JsonElement Definition, DateTime CreatedAt`.
`RunSummaryDto`: `Guid RefId, Guid WorkflowDefinitionRefId, int Version, string Status, string CorrelationKey, DateTime StartedAt, DateTime? CompletedAt`.
`RunDetailDto`: `RunSummaryDto Summary, IReadOnlyList<BranchSummaryDto> Branches`.
`HistoryEventDto`: `long HistoryEventId, DateTime CreatedAt, string EventKind, JsonElement Payload`.

- [ ] **Step 1:** Add project references + create DTO files (no test for DTOs).
- [ ] **Step 2:** Run `dotnet build Wbskt.slnx` — must succeed.
- [ ] **Step 3: Commit** `(management): add workflow DTOs and project references`.

---

### Task 12.2: WorkflowsController — publish endpoint

**Spec:** §1.7 publish derives a new `Version` per `RefId`.

**Files:**
- Create: `Hosts/Wbskt.Management.Host/Controllers/WorkflowsController.cs`
- Create: `Hosts/Wbskt.Management.Host/Services/IWorkflowDefinitionService.cs`
- Create: `Hosts/Wbskt.Management.Host/Services/WorkflowDefinitionService.cs`
- Test: `Tests/Wbskt.Management.Host.Tests/Controllers/WorkflowsControllerTests.cs` *(create the tests project if it doesn't exist)*

Wait — does `Tests/Wbskt.Management.Host.Tests` exist? Probably not. **Out of scope:** this plan stays focused on the workflow engine; Management Host controller tests use the same `Tests/Wbskt.Workflow.Engine.Host.Tests` project (which already references both `Wbskt.Workflow` and now `Wbskt.Management.Host` via a transitive). Put controller tests under `Tests/Wbskt.Workflow.Engine.Host.Tests/ManagementHost/`. Document this carve-out in the task.

`IWorkflowDefinitionService`:
```csharp
public interface IWorkflowDefinitionService
{
    Task<WorkflowPublishResponse> PublishAsync(WorkflowPublishRequest request, CancellationToken ct);
    Task<WorkflowDefinitionDto> GetCurrentAsync(Guid refId, CancellationToken ct);
    Task<WorkflowDefinitionDto> GetVersionAsync(Guid refId, int version, CancellationToken ct);
    Task DeprecateAsync(Guid refId, CancellationToken ct);
}
```

`PublishAsync`:
1. Validate the definition via `IWorkflowValidator` (from Phase 2 Task 2.12).
2. Find current version via `IWorkflowDefinitionProvider.FindByRefIdVersionAsync(refId, version: 0)` — wait, that signature requires version. Use `GetCurrentByRefIdAsync(refId)`, catch `NotFoundException`, and set `nextVersion = current.Version + 1` (or 1 if first).
3. Insert new row with status `Published`.
4. Mark prior current version `Deprecated`.
5. Call `ITriggerRegistrationService.OnPublishedAsync(newId)`.
6. Call `ITriggerRegistrationService.OnDeprecatedAsync(oldId)` if it existed.
7. Invalidate cache via `IWorkflowDefinitionCache.Invalidate(newId)` and (if existed) `oldId`.
8. Return response.

Use the repo's Reference Mapper Pattern + `[FromKeyedServices("WorkflowDefinition")]` per coding conventions.

- [ ] **Step 1:** Write failing tests:
  - `Publish_first_version_returns_version_1`
  - `Publish_second_version_increments_and_deprecates_first`
  - `Publish_invokes_trigger_registration_hooks`
  - `Publish_invalid_definition_returns_400_via_global_handler`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(management): workflow publish endpoint`.

---

### Task 12.3: WorkflowsController — get current + get version + deprecate

**Files:**
- Modify: `Hosts/Wbskt.Management.Host/Controllers/WorkflowsController.cs` — add GET/POST endpoints.
- Test: extend `WorkflowsControllerTests`

```csharp
[HttpGet("{refId:guid}")]
public Task<WorkflowDefinitionDto> GetCurrent(Guid refId, CancellationToken ct);

[HttpGet("{refId:guid}/versions/{version:int}")]
public Task<WorkflowDefinitionDto> GetVersion(Guid refId, int version, CancellationToken ct);

[HttpPost("{refId:guid}/deprecate")]
public Task DeprecateAsync(Guid refId, CancellationToken ct);
```

Per coding conventions: failed `RefId` lookup throws `SecurityException` (→ 403) in the reference mapper layer. Not `NotFoundException`.

- [ ] **Step 1:** Write failing tests `GetCurrent_returns_dto`, `GetVersion_returns_dto`, `Deprecate_marks_status_Deprecated`, `Unknown_RefId_returns_403`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(management): workflow read + deprecate endpoints`.

---

### Task 12.4: RunsController in Management Host — list + detail + cancel

**Spec:** §6.4 operator APIs.

**Files:**
- Create: `Hosts/Wbskt.Management.Host/Controllers/WorkflowRunsController.cs`
- Create: `Hosts/Wbskt.Management.Host/Services/IWorkflowRunQueryService.cs`
- Create: `Hosts/Wbskt.Management.Host/Services/WorkflowRunQueryService.cs`
- Modify: `IRunProvider` — add `Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(int workflowDefinitionId, RunStatus? statusFilter, int top, long? cursorId, CancellationToken ct);` + sproc `Run_ListByWorkflow.sql`.
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/ManagementHost/WorkflowRunsControllerTests.cs`

Endpoints:
```csharp
[HttpGet("/workflows/{workflowRefId:guid}/runs")]
public Task<RunListResponse> List(Guid workflowRefId, [FromQuery] string? status, [FromQuery] int top = 50, [FromQuery] long? cursor = null, CancellationToken ct = default);

[HttpGet("/runs/{runRefId:guid}")]
public Task<RunDetailDto> Get(Guid runRefId, CancellationToken ct);

[HttpPost("/runs/{runRefId:guid}/cancel")]
public Task CancelAsync(Guid runRefId, [FromBody] CancelRunRequest req, CancellationToken ct);
```

`Get` returns `RunDetailDto` with all active+completed branches (from `IBranchProvider.GetByRunIdAsync` — add the method + sproc if not present from Phase 3).

- [ ] **Step 1:** Write failing tests:
  - `List_returns_runs_for_workflow_filtered_by_status`
  - `List_supports_cursor_pagination`
  - `Get_returns_summary_plus_branches`
  - `Cancel_calls_cancellation_service`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement (+ provider/sproc additions). The cancel endpoint duplicates Phase 11 Task 11.7's Engine Host `/runs/{id}/cancel` — that's intentional: Engine Host owns internal HTTP, Management Host owns the operator-facing surface. Both call into the same `IRunCancellationService`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(management): workflow runs operator endpoints`.

---

### Task 12.5: HistoryController — paged event reads

**Files:**
- Create: `Hosts/Wbskt.Management.Host/Controllers/WorkflowHistoryController.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/ManagementHost/WorkflowHistoryControllerTests.cs`

```csharp
[ApiController]
[Route("runs/{runRefId:guid}/history")]
public sealed class WorkflowHistoryController(/* ... */) : ControllerBase
{
    [HttpGet]
    public Task<HistoryListResponse> List(Guid runRefId, [FromQuery] long fromEventId = 0, [FromQuery] int top = 200, CancellationToken ct = default);
}

public sealed record HistoryListResponse(IReadOnlyList<HistoryEventDto> Events, long? NextCursor);
```

- [ ] **Step 1:** Write failing tests `List_returns_events_from_cursor`, `List_returns_next_cursor_when_more_available`, `List_with_no_events_returns_empty`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(management): workflow history endpoint`.

---

### Task 12.6: SharedVariablesController — read + write

**Spec:** §5.2 + §6.4 operator visibility into shared state.

**Files:**
- Create: `Hosts/Wbskt.Management.Host/Controllers/SharedVariablesController.cs`
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/ManagementHost/SharedVariablesControllerTests.cs`

```csharp
[HttpGet("/workflows/{workflowRefId:guid}/variables/{name}")]
public Task<SharedVariableDto> Get(Guid workflowRefId, string name, CancellationToken ct);

[HttpPut("/workflows/{workflowRefId:guid}/variables/{name}")]
public Task<SharedVariableDto> Set(Guid workflowRefId, string name, [FromBody] SharedVariableSetRequest request, CancellationToken ct);
```

`SharedVariableSetRequest`: `JsonElement Value, byte[]? ExpectedRowVersion`.

Operator can perform CAS by passing `ExpectedRowVersion`; omit for force-overwrite.

- [ ] **Step 1:** Write failing tests `Get_returns_value_and_row_version`, `Set_with_matching_row_version_succeeds`, `Set_with_mismatched_row_version_returns_409`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement. Map `OptimisticConcurrencyException` → 409 via a new entry in the global exception handler in `Wbskt.Infrastructure/Middlewares/GlobalExceptionHandler.cs` (the handler exists already; add a switch case).
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(management): shared variables endpoints`.

---

### Task 12.7: ManualStart proxy endpoint in Management Host

**Spec:** §6.4 — operators usually call Management Host, not Engine Host directly. The Management Host's `WorkflowsController.StartManualRun(refId)` proxies to Engine Host's `InboundHub`.

For v1, since Management Host references `Wbskt.Workflow` directly, just call `IInboundHub` in-process. The architectural note: when Engine Host becomes a separate process, this proxy becomes an HTTP call.

**Files:**
- Modify: `Hosts/Wbskt.Management.Host/Controllers/WorkflowsController.cs`
- Test: extend `WorkflowsControllerTests`

```csharp
[HttpPost("{refId:guid}/runs")]
public Task<StartRunResponse> StartManualRun(Guid refId, [FromBody] StartRunRequest request, CancellationToken ct);
```

Note: this requires Management Host to register the workflow engine's DI. Modify `Hosts/Wbskt.Management.Host/Program.cs` to call `builder.Services.AddWorkflowEngine(builder.Configuration);` — same registration used by Engine Host. **But** Management Host should NOT register the hosted services (the polling pollers belong to Engine Host only).

Refactor `AddWorkflowEngine` from Phase 3 Task 3.13 to accept an option: `AddWorkflowEngine(IConfiguration config, bool includeHostedServices = false)`. Engine Host passes `true`; Management Host passes `false`.

- [ ] **Step 1:** Write failing tests:
  - `StartManualRun_calls_inbound_hub`
  - `Management_Host_does_not_start_hosted_services_when_includeHostedServices_false`
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Refactor `AddWorkflowEngine` + implement endpoint.
- [ ] **Step 4:** PASS + `dotnet build`.
- [ ] **Step 5: Commit** `(management): manual run start endpoint`.

---

### Task 12.8: Signal proxy endpoint

**Files:**
- Modify: `Hosts/Wbskt.Management.Host/Controllers/WorkflowRunsController.cs`
- Test: extend `WorkflowRunsControllerTests`

```csharp
[HttpPost("/runs/{runRefId:guid}/signals/{signalName}")]
public Task<SignalResponse> Signal(Guid runRefId, string signalName, [FromBody] JsonElement payload, CancellationToken ct);
```

Calls `IInboundHub.HandleAsync` with the signal inbound event. Same pattern as Engine Host's `RunsController.Signal` (Task 11.7); same comment about proxying applies.

- [ ] **Step 1:** Write failing test `Signal_calls_inbound_hub`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(management): signal endpoint`.

---

### Task 12.9: Management Host DI smoke test

**Files:**
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/ManagementHost/ManagementHostDiTests.cs`

Asserts: in Management Host's service collection, all controllers/services are resolvable; no hosted services from the workflow engine are registered.

- [ ] **Step 1:** Write failing test.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Verify registrations from previous tasks suffice.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(management): DI smoke test`.

---

### Task 12.10: End-to-end test — publish workflow + start run + observe completion

**Files:**
- Test: `Tests/Wbskt.Workflow.Engine.Host.Tests/ManagementHost/PublishToObserveE2ETests.cs`

End-to-end (in-process):
1. POST `/workflows` with the §1.10 greenhouse sample → assert version 1.
2. POST `/workflows/{refId}/runs` → assert `RunRefId` returned.
3. Wait (synchronously, since `IRunDispatcher` is in-process) until Run terminal.
4. GET `/runs/{runRefId}` → assert `Status == Succeeded`, branches present.
5. GET `/runs/{runRefId}/history` → assert event sequence.
6. POST `/workflows/{refId}/deprecate` → assert `Status == Deprecated` and trigger registrations removed.

- [ ] **Step 1:** Write failing E2E test using two `WebApplicationFactory<Program>` instances (Engine Host + Management Host) sharing the same in-memory provider state.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Stitch fixtures.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(management): publish-to-observe E2E test`.

---

**Phase 12 acceptance:**
- [ ] Operator REST surface complete: publish/get/deprecate workflows, list/get/cancel runs, history reads, shared variables, manual start, signal.
- [ ] Global exception handler maps `OptimisticConcurrencyException` → 409.
- [ ] Management Host does not start workflow engine hosted services.
- [ ] 10 commits on `feat/workflow-engine`.

---

## Phase 13 — Integration Tests

**Goal:** A small but realistic SQL-backed integration test suite that runs against the dev SQL Edge container. Validates everything Phase 1–12 unit-tested with mocks now works against real SQL semantics: TVPs, ROWVERSION, `UPDLOCK READPAST` leases, the OUTPUT-INSERTED race fix in `RunCounters_IncrementActiveBranches`, retention sprocs.

**Spec sections:** §6.5 testing strategy — "unit tests cover most logic; integration tests prove SQL behavior."

**Tooling:** The repo already runs SQL Edge via `Scripts.bat`. Integration tests must:
- Use `Microsoft.Data.SqlClient` against `Server=localhost,1433;User Id=sa;Password=...` (read from env var, mirroring `Deploy-Databases.ps1` conventions).
- Deploy the DACPAC against a per-test-class database (e.g., `WbsktTest_<random>`) via `sqlpackage` CLI invoked from test setup.
- Drop the database in test cleanup.

---

### Task 13.1: Tests/Wbskt.Workflow.Engine.Host.IntegrationTests project bootstrap

**Files:**
- Create: `Tests/Wbskt.Workflow.Engine.Host.IntegrationTests/Wbskt.Workflow.Engine.Host.IntegrationTests.csproj`
- Create: `Tests/Wbskt.Workflow.Engine.Host.IntegrationTests/Infrastructure/SqlEdgeFixture.cs`
- Create: `Tests/Wbskt.Workflow.Engine.Host.IntegrationTests/Infrastructure/DatabaseDeployer.cs`
- Modify: `Wbskt.slnx` — add the new test project.
- Test: `Tests/Wbskt.Workflow.Engine.Host.IntegrationTests/SmokeTests.cs` — `Sql_edge_is_reachable` + `Schema_deployed`.

`SqlEdgeFixture : IAsyncLifetime`:
- `InitializeAsync`: generates per-fixture DB name, runs `sqlpackage /a:Publish` with the DACPAC.
- `DisposeAsync`: `DROP DATABASE`.
- Exposes `ConnectionString` property.

`DatabaseDeployer` wraps `sqlpackage.exe` invocation; reads DACPAC path from build output `Databases/Wbskt.Database/bin/Debug/Wbskt.Database.dacpac`.

Test project references: `Wbskt.Workflow.Abstraction`, `Wbskt.Workflow`, `xunit`, `Microsoft.Data.SqlClient`, `FluentAssertions`.

Add a `[CollectionDefinition("SqlEdge")]` so the fixture is shared across the test class set (DACPAC deploy is slow; aim for 1 deploy per fixture, not per test).

- [ ] **Step 1:** Write failing smoke test that opens a connection + executes `SELECT 1`.
- [ ] **Step 2:** FAIL (project doesn't exist).
- [ ] **Step 3:** Create project, csproj, fixture, deployer.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(test): bootstrap integration test project`.

---

### Task 13.2: WorkflowDefinitionProvider integration test — round-trip

**Files:**
- Test: `Tests/Wbskt.Workflow.Engine.Host.IntegrationTests/Providers/WorkflowDefinitionProviderIntegrationTests.cs`

- [ ] **Step 1:** Write failing tests:
  - `Insert_then_GetByRefIdVersion_round_trips`
  - `Publish_then_GetCurrent_returns_published_row`
  - `Deprecate_changes_status`
  - `Duplicate_Insert_throws_SqlException` (unique constraint on `(RefId, Version)`).
- [ ] **Step 2:** FAIL (assuming no implementation gaps).
- [ ] **Step 3:** Fix any mismatches between Phase 3 mappers and actual SSDT column types.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(test): WorkflowDefinitionProvider integration tests`.

---

### Task 13.3: RunCounters integration — atomic OUTPUT race

**Spec:** §2.15 the "who completes last" race depends on the sproc's `OUTPUT inserted.ActiveBranchCount` returning the post-update value.

**Files:**
- Test: `Tests/Wbskt.Workflow.Engine.Host.IntegrationTests/Providers/RunCountersIntegrationTests.cs`

- [ ] **Step 1:** Write failing tests:
  - `IncrementActiveBranches_returns_post_update_value`
  - `Concurrent_decrement_from_two_callers_one_observes_zero_and_one_observes_one` — `Parallel.ForEachAsync` invoking `IncrementActiveBranchesAsync(runId, -1)` 100 times from 10 concurrent tasks against a counter initialized to 100; assert exactly one call returned `0` (the "last branch") and no call returned a negative number.
- [ ] **Step 2:** FAIL or PASS (this is the integrity check).
- [ ] **Step 3:** If race condition observed (e.g., two callers see same post-value), revisit Phase 1 Task 1.4 sproc — ensure `UPDATE` with `OUTPUT inserted.ActiveBranchCount` is used, NOT `SELECT then UPDATE`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(test): RunCounters atomic race integration`.

---

### Task 13.4: Bookmarks lease integration — UPDLOCK READPAST

**Spec:** §3.4 + §7.2 — `Bookmark_LeaseDue` must give different rows to concurrent callers.

**Files:**
- Test: `Tests/Wbskt.Workflow.Engine.Host.IntegrationTests/Providers/BookmarkLeaseIntegrationTests.cs`

- [ ] **Step 1:** Write failing tests:
  - `LeaseDue_with_two_concurrent_callers_returns_disjoint_sets` — insert 100 due bookmarks; spawn 10 concurrent `LeaseDueAsync(batch=10, owner=$"caller-{i}")` calls; assert union has 100 distinct bookmark ids and each caller got at most 10.
  - `LeaseDue_does_not_return_already_leased_unexpired_rows`
  - `LeaseDue_returns_expired_lease_rows` — fast-forward `LeasedUntilUtc` past now, assert reclaimed.
- [ ] **Step 2:** Run.
- [ ] **Step 3:** Fix sproc if any test fails (e.g., missing `UPDLOCK, READPAST` hints).
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(test): bookmark lease integration`.

---

### Task 13.5: HistoryEvent TVP append integration

**Spec:** §2.11 batch append via TVP, monotone `HistoryEventId` per `RunId`.

**Files:**
- Test: `Tests/Wbskt.Workflow.Engine.Host.IntegrationTests/Providers/HistoryEventTvpIntegrationTests.cs`

- [ ] **Step 1:** Write failing tests:
  - `AppendBatch_assigns_monotone_HistoryEventIds`
  - `AppendBatch_multiple_calls_continue_sequence`
  - `AppendBatch_concurrent_for_same_RunId_does_not_assign_duplicate_HistoryEventIds`
- [ ] **Step 2:** FAIL/PASS.
- [ ] **Step 3:** Fix sproc/TVP if needed (likely needs `SERIALIZABLE` or `UPDLOCK` on the max-id read).
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(test): history event TVP integration`.

---

### Task 13.6: SharedVariable CAS integration via ROWVERSION

**Spec:** §5.2 ROWVERSION-based CAS.

**Files:**
- Test: `Tests/Wbskt.Workflow.Engine.Host.IntegrationTests/Providers/SharedVariableCasIntegrationTests.cs`

- [ ] **Step 1:** Write failing tests:
  - `SetWithCas_first_write_returns_RowVersion`
  - `SetWithCas_with_correct_RowVersion_succeeds_and_returns_new_version`
  - `SetWithCas_with_stale_RowVersion_throws_OptimisticConcurrencyException`
  - `Concurrent_writers_one_wins_one_loses`
- [ ] **Step 2:** Run.
- [ ] **Step 3:** Fix sproc if needed (`WHERE @ExpectedRowVersion IS NULL OR RowVersion = @ExpectedRowVersion`).
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(test): SharedVariable CAS integration`.

---

### Task 13.7: PendingTriggerEvent dequeue-oldest integration

**Spec:** §4.6 queue-policy backlog; oldest first; concurrent dequeue gives distinct rows.

**Files:**
- Test: `Tests/Wbskt.Workflow.Engine.Host.IntegrationTests/Providers/PendingTriggerEventQueueIntegrationTests.cs`

- [ ] **Step 1:** Write failing tests:
  - `DequeueOldest_returns_oldest_first`
  - `Concurrent_dequeues_return_distinct_rows` — 10 concurrent callers against 100 rows for the same `(workflowDefinitionId, correlationKey)`; assert no duplicate dequeues.
  - `DequeueOldest_returns_null_when_empty`
- [ ] **Step 2:** Run.
- [ ] **Step 3:** Fix sproc if needed.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(test): pending trigger event queue integration`.

---

### Task 13.8: Full happy-path E2E integration — publish, start, complete

**Files:**
- Test: `Tests/Wbskt.Workflow.Engine.Host.IntegrationTests/E2E/HappyPathIntegrationTests.cs`

End-to-end against real SQL:
1. Boot both Engine Host + Management Host via `WebApplicationFactory` configured to point at the integration database.
2. Use the rabbit-mq in-memory test harness for the bus (per Phase 11 Task 11.10).
3. POST `/workflows` to publish a small workflow.
4. POST `/workflows/{refId}/runs` to start.
5. Poll `/runs/{runRefId}` until terminal (or fail after 30s).
6. Assert: `Status == Succeeded`, history events written, no orphan bookmarks, no orphan pending events.

- [ ] **Step 1:** Write failing E2E.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Stitch the integration fixture + WebApplicationFactory + bus harness together.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `(test): full happy-path integration E2E`.

---

**Phase 13 acceptance:**
- [ ] Integration test project deploys DACPAC, runs against real SQL Edge, covers each SQL-semantics-critical sproc.
- [ ] Full publish→start→complete E2E passes.
- [ ] 8 commits on `feat/workflow-engine`.

---

## Final Acceptance & Merge

After Phase 13 completes:

- [ ] All unit tests pass: `dotnet test Tests/Wbskt.Workflow.Engine.Host.Tests`.
- [ ] All integration tests pass: `dotnet test Tests/Wbskt.Workflow.Engine.Host.IntegrationTests` (requires running SQL Edge per `Scripts.bat`).
- [ ] Full solution builds: `dotnet build Wbskt.slnx`.
- [ ] Approximately ~140 commits on `feat/workflow-engine` (0.x + 1.x + ... + 13.x), all signed with `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`.
- [ ] Rebase / squash policy: per repo's existing pattern (check recent merges on `master`).
- [ ] Open PR with the `Docs/Workflow.Engine.V3.Design.md` + this plan referenced in the description.

---

## Plan Self-Review Notes

**Spec coverage audit** *(written after the plan was complete; cross-checks against `Docs/Workflow.Engine.V3.Design.md`):*

| Spec section | Phase / Task |
|---|---|
| §1 Authoring layer | Phase 2 |
| §1.7 Publish lifecycle | Phase 12 Task 12.2 + Phase 7 Task 7.11 |
| §1.10 Greenhouse sample workflow | Phase 2 Task 2.12 (validator), Phase 12 Task 12.10 (E2E) |
| §2.1–2.6 Run/Branch/State model | Phase 1 (schema), Phase 3 (providers), Phase 4 (primitives) |
| §2.9 Executor contract | Phase 4 Task 4.4, Phase 9 |
| §2.10 Branch loop pseudocode | Phase 5 Tasks 5.5–5.9 |
| §2.11 History event taxonomy + TVP | Phase 1 Task 1.7, Phase 3 Task 3.8, Phase 13 Task 13.5 |
| §2.13 Idempotency | Phase 1 Task 1.8, Phase 3 Task 3.9, Phase 9 Tasks 9.12–9.16 |
| §2.15 Completion race | Phase 1 Task 1.4, Phase 3 Task 3.5, Phase 5 Task 5.9, Phase 13 Task 13.3 |
| §3 Bookmarks | Phase 6 entirely |
| §3.6 TTL companion timers | Phase 6 Task 6.6 |
| §3.7 Inbound idempotency | Phase 6 Task 6.3 |
| §4 Triggers + InboundHub | Phase 7 entirely |
| §4.4 Correlation key resolution | Phase 7 Task 7.2 |
| §4.5 Concurrency policies | Phase 7 Tasks 7.4–7.5 |
| §4.6 Pending event backlog | Phase 7 Task 7.10, Phase 10 Task 10.5 |
| §4.7 Inbound adapters | Phase 11 entirely |
| §5.1 Logic | Phase 9 Task 9.2 |
| §5.2 SharedVariables / Variable | Phase 1 Task 1.9, Phase 3 Task 3.10, Phase 9 Task 9.3, Phase 13 Task 13.6 |
| §5.3 Delay | Phase 9 Task 9.4 |
| §5.4 Credit cost | Phase 4 Task 4.7 |
| §5.5 Compensation (sequential v1) | Phase 8 Task 8.7 |
| §5.6 WaitForHttp | Phase 9 Task 9.5 |
| §5.7 AwaitSignal | Phase 9 Task 9.6 |
| §5.8 FailRun | Phase 9 Task 9.7 |
| §5.9 ForEach | Phase 9 Task 9.8 |
| §5.10 ParallelForEach | Phase 9 Task 9.9 |
| §5.11 Join + quorum | Phase 9 Task 9.10 |
| §5.12 SubWorkflow + child-completed | Phase 9 Task 9.11, Phase 11 Task 11.8 |
| §6.1 Schema (11 tables) | Phase 1 |
| §6.2 Definition cache | Phase 5 Task 5.4 |
| §6.3 Retention | Phase 10 Task 10.4 |
| §6.4 Operator APIs | Phase 12 |
| §6.5 Testing strategy | Phase 13 |
| §7.2 LeaseHolder + RunDispatcher | Phase 5 Tasks 5.1–5.2, Phase 10 Task 10.1 |
| §7.3 Eight background services | Phase 5 Task 5.10, Phase 6 Tasks 6.2/6.7, Phase 10 Tasks 10.2–10.7 |
| §7.4 MassTransit | Phase 11 Task 11.1 |
| §7.5 Startup recovery | Phase 10 Task 10.6 |
| §7.6 Metrics | Phase 10 Task 10.7 |
| §8 Error model | Phase 8 |

**Gaps identified during self-review:**
1. **JoinAggregators table was not in the Phase 1 schema list** — handled by Phase 9 Task 9.10 introducing it as a back-edit. Acceptable but called out in the commit. *(Resolution: documented.)*
2. **`Runs.ParentRunRefId` / `Runs.ParentBranchId` columns** — same back-edit pattern in Phase 11 Task 11.8. *(Resolution: documented.)*
3. **`Run_GetActiveBy_Correlation` sproc, `Run_CountByStatus` sproc, `Run_GetStuck` sproc, `Branch_GetRunning` sproc, `IdempotencyKey_TryClaim` sproc, `Run_ListByWorkflow` sproc, `Bookmark_DeleteSiblings`, `Bookmark_DeleteOrphans`, `ScheduledFire_DeleteById`, `HistoryEvent_DeleteForRetiredRuns`, `PendingTriggerEvent_DeleteExpired`** — all introduced as back-edits in later phases (7.4, 10.3, 10.6, 3.9, 12.4, 6.6, 6.7, 10.2, 10.4, 10.5). Acceptable; engineer should re-run Phase 1's "all sprocs build" check at each addition. *(Resolution: each task explicitly lists its added sproc.)*
4. **`AddWorkflowEngine` parameterization for hosted-services opt-out** is introduced in Phase 12 Task 12.7 — a back-edit to Phase 3 Task 3.13. Engineer must update Engine Host's `Program.cs` call site at that point. *(Resolution: documented in Task 12.7.)*

**Placeholder scan:** no TBDs, no "implement later", no unspecified test code. Spec citations are by section number; pseudocode is referenced rather than re-inlined, per the user's directive.

**Type consistency:** spot-checked the following interface signatures match across phases:
- `IInboundHub.HandleAsync(InboundEvent, ct)` — Tasks 7.6, 11.1, 11.4–11.7, 12.7, 12.8 ✓
- `IRunDispatcher.DispatchAsync(BranchExecutionRequest, ct)` — Tasks 5.1, 5.2, 5.10, 6.2, 7.3, 10.6 ✓
- `NodeExecutionResult.Continue/Fork/WaitForBookmark/Fail/Terminal` — Tasks 4.3, 5.5–5.9, all of Phase 9 ✓
- `IRunStarter.StartAsync(int, string, InboundEvent, ct)` initially → extended in Task 11.8 to add parent ids ✓ (back-edit called out)

---

**End of plan.**


