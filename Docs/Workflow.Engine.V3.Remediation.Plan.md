# Workflow Engine V3 — Remediation Plan

## Context

An architecture review (2026-07-19) of the V3 workflow engine (`Docs/Workflow.Engine.V3.Design.md` vs the implementation in `Wbskt.Workflow*`, `Hosts/Wbskt.Workflow.Engine.Host`, `Hosts/Wbskt.Management.Host`, and `Databases/Wbskt.Database`) found 6 production-critical bugs, a set of high-impact divergences from the locked design, and assorted DB/code hygiene issues. This plan fixes all of them, phased so each phase compiles and passes tests independently.

**Decisions already made by the owner (do not revisit):**
1. **Bookmarks: single-row model** per design §3.5 — one bookmark per wait with `ExpiresAt` + `TtlPort` on the same row. Companion sibling timer rows and `DeleteSiblings` are removed.
2. **ForEach: sequential** per design §2.8 — iterator state + graph back-edge. `ParallelForEach` remains the fan-out node.
3. **Multi-edge fan-out: reject at publish** — validator error for duplicate `(node, port)` edges; do NOT implement multi-edge fan-out in BranchLoop.
4. **Scope: everything**, phased.

**Repo conventions to preserve:**
- Statuses are strings. Run: `Running | Failing | Cancelling | Succeeded | Failed | PartiallyFailed | Cancelled | Faulted` (this plan adds `OutOfCredits`). Branch: `Active | Waiting | WaitingAtJoin | Compensating | Completed | Failed | Cancelled`.
- SPs named `Table_Action.sql` under `Databases/Wbskt.Database/StoredProcedures/`, tables under `Tables/`. Providers extend `BaseSqlProvider` (`Wbskt.Infrastructure`), never depend on other providers; orchestration lives in `Wbskt.Workflow/Runtime`.
- Providers are Scoped; engine core Singleton resolving providers via `IServiceScopeFactory`; executors keyed by kind string via `NodeExecutorRegistry`.
- Tests: xUnit + Moq + FluentAssertions. Unit tests in `Tests/Wbskt.Workflow.Engine.Host.Tests`, DB integration in `Tests/Wbskt.Workflow.Engine.Host.IntegrationTests`, full E2E in `Tests/Wbskt.E2E.FeatureTests` (SkippableFact, needs hosts running).
- The owner annotates open questions with `[RJ]:` comments — several are resolved by this plan; delete the comment when the fix lands.

---

## Phase 1 — Critical correctness (P0)

### 1.1 Fix crash recovery (recovers nothing today)

**Bug:** `Databases/Wbskt.Database/StoredProcedures/Branch_GetRunning.sql` filters `Status = N'Running'`, but branches are only ever persisted as `Active` (see `BranchLoop.cs` `ActiveStatus`), so `RunRecoveryService` re-dispatches nothing after a restart.

**Changes:**
- `Branch_GetRunning.sql`: change filter to `Status IN (N'Active', N'Compensating')` (Compensating branches also need re-dispatch after crash — see 5.6).
- Optionally delete the now-duplicate `Branch_GetAllActive.sql`/`GetAllActiveAsync` if unused elsewhere (grep first; keep if referenced).
- `Tests/Wbskt.Workflow.Engine.Host.IntegrationTests/E2E/CrashRecoveryIntegrationTests.cs` already seeds `Status = "Active"` and expects recovery — it should pass unchanged once the SP is fixed. Add a second case seeding a `Compensating` branch.

### 1.2 RunReaper must not kill healthy long-running runs

**Bug:** `Run_GetStuck.sql` selects any `Running|Cancelling|Failing` run with `CreatedAt < now - 30min`; `RunReaper` cancels them all. Any Delay/AwaitSignal/sub-workflow wait > 30 min is killed with `REAPER_TIMEOUT`.

**Changes:**
- Rewrite `Databases/Wbskt.Database/StoredProcedures/Run_GetStuck.sql` to select only genuinely wedged runs:
  ```sql
  -- A run is stuck when it is non-terminal, old enough, AND has no live work:
  -- no bookmark rows and no branches that could still make progress.
  WHERE r.Status IN (N'Running', N'Cancelling', N'Failing')
    AND r.CreatedAt < @CutoffUtc
    AND NOT EXISTS (SELECT 1 FROM dbo.Bookmarks b WHERE b.RunId = r.Id)
    AND NOT EXISTS (SELECT 1 FROM dbo.Branches br
                    WHERE br.RunId = r.Id
                      AND br.Status IN (N'Active', N'Waiting', N'WaitingAtJoin', N'Compensating'))
  ```
  Note: `Active` branches are excluded because an Active branch mid-execution is legitimate; a *crashed* Active branch is handled by 1.1 recovery, not the reaper. Keep `@CutoffUtc`/`@BatchSize` params.
- `Hosts/Wbskt.Workflow.Engine.Host/HostedServices/RunReaper.cs` (`ProcessStuckRunsAsync`): after `RequestCancellationAsync`, also resolve `IRunFinalizer` from the scope and call `FinalizeAsync(run.Id, ct)` — a stuck run by the new definition has no branch left to drive finalization. Safe because 2.1 makes the finalizer idempotent.
- Keep `RunStuckThreshold` default at 30 min (now correct with the tighter definition).
- **Tests:** unit-test the reaper with a `IRunProvider` stub; integration test for `Run_GetStuck` proving a Running run *with* a bookmark is NOT returned and a Running run with zero bookmarks/branches IS.

### 1.3 Queue concurrency policy loses events at drain

**Bug (two parts):**
1. Queued events are re-injected by `Wbskt.Workflow/Runtime/PendingTriggerEventDrainer.cs` through `InboundHub.HandleAsync` with the **same `InboundEventId`**. `BookmarkResumer.MatchInboundAsync` (`Wbskt.Workflow/Runtime/BookmarkResumer.cs:33-72`) finds the existing idempotency claim (marked `Succeeded` when the event was originally queued) and drops the event as a duplicate. Queued runs never start.
2. Once (1) is fixed, the drainer's `while(true)` loop becomes an infinite re-queue cycle: after the first drained event starts a run, every subsequent dequeue re-enters the enforcer, sees the new active run, re-enqueues, and is dequeued again forever. Design §4.6 says drain exactly ONE event per run-terminal.

**Changes:**
- `PendingTriggerEventDrainer.DrainAsync`: replace the `while(true)` loop with a **single** dequeue + dispatch (dequeue once; if null, return; deserialize; dispatch; return).
- Re-mint the event identity before re-injection so the old claim doesn't match:
  ```csharp
  evt = evt with { InboundEventId = $"{evt.InboundEventId}:drain:{row.Id}" };
  ```
  (`row.Id` keeps it deterministic per dequeued row; `InboundEvent` is a record — `with` works.)
- **Wedged-claim fix (same area):** in `BookmarkResumer.MatchInboundAsync`, wrap the post-claim work (bookmark match + resume loop) so that on exception the claim is released — call `_idempotencyKeyProvider.MarkFailedAsync(claim.KeyValue, errorJson, ct)` in a catch before rethrowing, and change the claim-check in `UpsertPendingAsync` handling: a row with `Status = 'Failed'` should be treated as claimable again. Concretely: add a new SP `IdempotencyKey_ReclaimFailed.sql` (UPDATE ... SET Status='Pending', BranchRefId=@NewClaimToken WHERE KeyValue=@Key AND Status='Failed'; SELECT row) OR simpler: in `MatchInboundAsync`, if the existing claim row's `Status == "Failed"`, delete it and retry the upsert once. Choose the simpler delete-and-retry unless it complicates tests.
- Also in `TriggerDispatcher.DispatchAsync` (`Wbskt.Workflow/Runtime/TriggerDispatcher.cs`): the `NoRegistration` early-return at ~line 66 leaves the claim `Pending` forever — mark it `Succeeded` before returning (mirrors the end-of-method behavior).
- **Tests:**
  - Unit: drainer dispatches exactly one event; drained event has a re-minted ID.
  - Unit: `MatchInboundAsync` releases the claim on resume failure; a redelivery then succeeds.
  - **New E2E** `Tests/Wbskt.E2E.FeatureTests/Scenarios/Lifecycle/TriggerQueuePolicyE2ETests.cs` (copy the shape of `TriggerConcurrencyE2ETests`): publish a workflow with `WorkflowConcurrencyPolicy.Queue` + a short AwaitSignal; fire 3 events; assert 1 run active and 2 queued; let runs finish; assert all 3 runs eventually exist, serialized.

### 1.4 Bookmarks: single-row model + atomic claim (replaces sibling rows)

**Bug:** the atomic `DELETE`-claim from design §3.4.2/§3.5 was never implemented. `Bookmark_Delete.sql` returns no rowcount; both `BookmarkScheduler` and `BookmarkResumer` dispatch the branch *before* deleting the bookmark; `DeleteSiblingsAsync` runs *after* dispatch and can delete new bookmarks created by the already-resumed branch; `BookmarkProvider.LeaseDueAsync` ignores its lease params entirely. Result: signal-vs-TTL races double-resume branches, and resumed branches can be stranded in `Waiting`.

**Target model (per owner decision — design §3.5 single row):** one `Bookmarks` row per wait. For a wait with TTL, `ExpiresAt = now + Ttl` and `TtlPort` live on the same row as the Signal/Http/Child match key. For a pure timer, `ExpiresAt = Timer.At`, `TtlPort = NULL`, `MatchKey = ''`. Whoever claims (deletes) the row first wins; the loser gets rowcount 0 and drops.

**Changes:**

*SQL:*
- New `Bookmark_ClaimByRefId.sql`:
  ```sql
  DELETE FROM dbo.Bookmarks WITH (ROWLOCK)
  OUTPUT deleted.Id
  WHERE RefId = @RefId;
  ```
  (Caller checks whether a row came back.)
- New `Bookmark_ClaimDue.sql` (replaces `Bookmark_GetDue` for the scheduler):
  ```sql
  DELETE TOP (@BatchSize) FROM dbo.Bookmarks WITH (ROWLOCK, READPAST)
  OUTPUT deleted.Id, deleted.RefId, deleted.RunId, deleted.BranchRefId, deleted.NodeId,
         deleted.WakeConditionKind, deleted.MatchKey, deleted.WakeConditionJson,
         deleted.ExpiresAt, deleted.TtlPort, deleted.CreatedAt
  WHERE ExpiresAt <= @Now;
  ```
  Claim-by-delete is atomic and needs no lease columns. Accepted trade-off: a crash between claim and dispatch loses the wake; the fixed RunReaper (1.2) now detects exactly that state (no bookmarks, no active branches) and reaps it. Document this in the SP header comment.
- Delete `Bookmark_DeleteSiblings.sql` and `Bookmark_GetDue.sql`.

*Provider (`Wbskt.Workflow/Providers/BookmarkProvider.cs` + `IBookmarkProvider`):*
- Add `Task<bool> TryClaimAsync(Guid refId, CancellationToken ct)` → `Bookmark_ClaimByRefId`, returns whether a row was deleted.
- Replace `LeaseDueAsync` with `Task<IReadOnlyCollection<BookmarkRow>> ClaimDueAsync(DateTime nowUtc, int batchSize, CancellationToken ct)` → `Bookmark_ClaimDue`. Remove the unused `hostId`/`leaseDuration` params.
- Remove `DeleteSiblingsAsync`.

*BranchLoop (`Wbskt.Workflow/Runtime/BranchLoop.cs`, `WaitForBookmark` case ~line 270):*
- Create ONE row. `ExpiresAt` = `timer.At` for `TimerWakeCondition`, else `nowUtc + condition.Ttl` when `Ttl` is set, else NULL. `TtlPort = condition.TtlPort`. Delete the companion-row block (lines ~279-288).
- `GetWakeConditionMatchKey`: return `string.Empty` for timers (drop the ISO-timestamp pseudo-key).

*BookmarkResumer (`Wbskt.Workflow/Runtime/BookmarkResumer.cs`):*
- In the match loop: `if (!await _bookmarkProvider.TryClaimAsync(bookmark.RefId, ct)) continue;` — then merge payload into branch local state, **then** dispatch (dispatch is the LAST step). Remove the `DeleteSiblingsAsync` call.
- Delete `ResumeViaBookmarkAsync` (the `[RJ]` comment already marks it as a dead remnant); remove it from `IBookmarkResumer` and update/delete the unit tests that call it directly.

*BookmarkScheduler (`Hosts/Wbskt.Workflow.Engine.Host/HostedServices/BookmarkScheduler.cs`):*
- `ProcessDueBookmarksAsync`: call `ClaimDueAsync` (rows are already claimed/deleted). For each row: if `TtlPort` is set AND `WakeConditionKind != "timer"`, this is a TTL firing — set `PendingTakePort = TtlPort` on the branch via `UpsertAsync`; for a pure timer, no `PendingTakePort`. Then dispatch. Remove the post-dispatch `DeleteAsync`/`DeleteSiblingsAsync` calls.
- Keep `RunOrphanGcAsync` as-is (it becomes the safety net for rows belonging to terminal runs).

*Tests:*
- Update `BookmarkCompanionTimerTests`, `BookmarkResumerTests`, `BookmarkSchedulerTests`, `BookmarkResumerBranchLoopIntegrationTests` to the single-row model (assert exactly ONE row per wait; assert TTL fields set on it).
- New race test: two concurrent `TryClaimAsync` calls on one refId — exactly one true (integration test against real SQL, place beside `BookmarkLeaseIntegrationTests`; rename/replace that fixture).
- E2E `BookmarkTtlRaceE2ETests` should still pass; review its assertions for the single-row semantics.

### 1.5 Cron format mismatch kills schedules after one fire

**Bug:** publish parses 5-field (`TriggerRegistrationService.cs:64`, `CronExpression.Parse(cron)`); the ticker re-parses with `CronFormat.IncludeSeconds` (6-field) (`ScheduledFireTicker.cs:179`). A standard 5-field cron fires once, then `ComputeNextOccurrence` throws `CronFormatException`, returns null, and the row is deleted as one-time.

**Changes:**
- Add one shared helper, e.g. `Wbskt.Workflow.Abstraction/Runtime/CronParser.cs` (static): try `CronExpression.Parse(expr)` (5-field), on `CronFormatException` fall back to `CronExpression.Parse(expr, CronFormat.IncludeSeconds)`; expose `TryGetNextOccurrence(string expr, DateTime afterUtc, out DateTime next)`.
- Use it in both `TriggerRegistrationService.CreateScheduleRegistrationAsync` and `ScheduledFireTicker.ComputeNextOccurrence`.
- In the ticker, distinguish "unparseable cron" (log **error**, do NOT delete — leave the row so the bug is visible and recoverable) from "cron has no next occurrence / blank" (legit one-time → delete).
- **Tests:** unit tests for both 5-field (`0 6 * * *`) and 6-field (`0 0 6 * * *`) round-tripping publish → first fire → advance; ticker test asserting the row survives a parse failure.

### 1.6 Graceful shutdown must not mark in-flight branches Failed

**Bug:** `BranchLoop.RunAsync` catch block (~line 162-176) treats `OperationCanceledException` as `EXECUTOR_CRASH` unless *run* cancellation was requested. On host shutdown, `stoppingToken` cancels executors → branches go through the OnFailure path and are persisted `Failed`. The post-executor check at ~line 198 (`linkedToken.IsCancellationRequested`) similarly converts host shutdown into branch `Cancelled`.

**Changes in `BranchLoop.RunAsync`:**
- In the generic catch: if `ex is OperationCanceledException` and run-cancellation is NOT requested but the **outer `ct`** (host token) IS cancelled → log info and `return` immediately, leaving the branch `Active` and counters untouched (recovery re-dispatches it per 1.1).
- Change the post-executor check from `linkedToken.IsCancellationRequested` to `runToken.IsCancellationRequested` (only run-level cancellation converts the result to `Terminal(Cancelled)`); if only the host token fired, `return` without state changes.
- **Tests:** extend `Tests/Wbskt.Workflow.Engine.Host.Tests/HostedServices/GracefulShutdownIntegrationTests.cs` — cancel the host token mid-node; assert branch remains `Active` and `ActiveBranchCount` unchanged; assert no `NodeFailed` history event.

---

## Phase 2 — Run lifecycle correctness (P1)

### 2.1 Terminal status stickiness + idempotent finalizer

**Bug:** `Run_SetTerminal.sql` and `Run_UpdateStatus.sql` update unconditionally; `RunFinalizer.FinalizeAsync` never checks for an already-terminal run. `Faulted` can be silently overwritten; concurrent finalizes double-publish events.

**Changes:**
- `Run_SetTerminal.sql`: guard the UPDATE:
  ```sql
  UPDATE dbo.Runs WITH (ROWLOCK)
  SET Status = @Status, CompletedAt = @CompletedAt
  WHERE Id = @RunId
    AND Status IN (N'Running', N'Failing', N'Cancelling');
  SELECT @@ROWCOUNT AS Transitioned;
  -- then the existing row SELECT
  ```
- `IRunProvider.SetTerminalAsync` → return `(bool Transitioned, RunRow Run)` (or add an out-style record). `RunFinalizer.FinalizeAsync`: if `Transitioned == false`, log debug and **return before** the history event / drain / publish / hook / bookmark-delete (someone else already finalized or the run is Faulted).
- `Run_UpdateStatus.sql`: add the same non-terminal guard (`WHERE RefId = @RefId AND Status IN (N'Running', N'Failing', N'Cancelling')`).
- `RunFinalizer.DetermineTerminalStatus`: add cancelled aggregation per design §5.4 — `cancelled > 0 && completed == 0 → "Cancelled"`, `cancelled > 0 && completed > 0 → "PartiallyFailed"`. Remove the dead `_ = counters` read.
- **Faulted path in `BranchLoop`** (EngineFaultException catch ~line 151): after transitioning the run to `Faulted`, also `SetFailedAsync(branchId, faultJson)` and decrement `ActiveBranchCount` so the run drains cleanly; do NOT call the finalizer (the terminal guard now protects `Faulted` from being overwritten by sibling completions).
- **Tests:** unit — finalize twice, second is a no-op (publisher called once); finalize a `Faulted` run leaves it `Faulted`; cancelled-only branches → `Cancelled`.

### 2.2 Credits: atomic charge, OutOfCredits terminal status, configurable budget

**Bugs:** `OutOfCredits` run status is never set; sibling branches keep running after exhaustion; budget hardcoded `100m` in `RunStarter.cs:73`; the check is 3 racy DB round-trips per attempt (`RetryExecutor.cs:139-182`).

**Changes:**
- New SP `RunCounters_TryCharge.sql`:
  ```sql
  UPDATE rc WITH (ROWLOCK)
  SET rc.CreditsConsumed = rc.CreditsConsumed + @Cost,
      rc.UpdatedAt = SYSUTCDATETIME()
  OUTPUT inserted.CreditsConsumed
  FROM dbo.RunCounters rc
  JOIN dbo.Runs r ON r.Id = rc.RunId
  WHERE rc.RunId = @RunId
    AND rc.CreditsConsumed + @Cost <= r.CreditBudget;
  ```
  Zero rows returned = out of credits. Add `IRunCountersProvider.TryChargeAsync(int runId, decimal cost, ct) → bool`.
- `RetryExecutor`: replace the GetById+GetByRunId+AddCredits block with one `TryChargeAsync` call. Drop the `runProvider` parameter from `RunWithRetryAsync` if no longer needed.
- `BranchLoop` Fail case: when `fail.ErrorCode == "OUT_OF_CREDITS"`, after `SetFailedAsync`, call `_runCancellationService.RequestCancellationAsync(runId, "OUT_OF_CREDITS", ct)` so siblings stop.
- `RunFinalizer.DetermineTerminalStatus`: when `currentStatus == "Cancelling"` and the run's `CancellationReason == "OUT_OF_CREDITS"` → return `"OutOfCredits"` (pass the RunRow in, not just the status string). Add `OutOfCredits` to every terminal-status list touched in 2.1 and to `Bookmark_DeleteOrphans.sql` (see 4.3).
- `WorkflowEngineOptions`: add `decimal DefaultCreditBudgetPerRun { get; init; } = 10000m;`. `RunStarter` takes `IOptions<WorkflowEngineOptions>` (or the value via ctor) and uses it instead of `100m`.
- **Tests:** integration — parallel `TryChargeAsync` calls never overshoot the budget; unit — OUT_OF_CREDITS fail cascades cancellation and finalizes as `OutOfCredits`. Extend `CreditBudgetE2ETests` to assert the run status is `OutOfCredits`.

### 2.3 Cancellation path cleanups

**File:** `Wbskt.Workflow/Runtime/RunCancellationService.cs`, `Hosts/Wbskt.Workflow.Engine.Host/InboundAdapters/WorkflowRunCancellationRequestedEventConsumer.cs`, `Wbskt.Workflow/Runtime/TriggerConcurrencyEnforcer.cs`, `TriggerDispatcher.cs`.

- **Single status write:** extend `Run_TransitionStatus.sql` to accept optional `@CancellationRequestedAt DATETIME2(3) = NULL, @CancellationReason NVARCHAR(500) = NULL` and set them (COALESCE) in the same guarded UPDATE. Remove the follow-up `UpdateStatusAsync` call in `RequestCancellationAsync` (lines 99-100).
- **Cross-host cancellation:** `WorkflowRunCancellationRequestedEventConsumer.Consume` currently only flips the local CTS. Change it to: `await _runCancellationService.RequestCancellationAsync(runId, reason, ct)` (idempotent — returns false if already transitioned) **and then unconditionally** `CancelCts(runId)`. This makes the engine host authoritative regardless of whether the management host already transitioned the DB status.
- **Management host:** verify `WorkflowRunQueryService.CancelAsync` (`Hosts/Wbskt.Management.Host/Services/Workflow/WorkflowRunQueryService.cs`) — it should only publish `WorkflowRunCancellationRequestedEvent` on the bus (plus an optimistic status transition for UX is fine, since the consumer path is now idempotent). If it currently performs the full `RequestCancellationAsync` including the finalize-fallback (`SetTerminalAsync("Cancelled")` when `IRunFinalizer` is absent — `RunCancellationService.cs:136-145`), remove the bare `SetTerminalAsync` fallback entirely: with 2.1 the finalizer is idempotent and the engine host will finalize. The fallback currently skips pending-trigger drain, run-completed publish, and the sub-workflow completion hook (stranding parent workflows).
- **CancelExisting cancels all matches:** `TriggerConcurrencyEnforcer.EvaluateAsync` returns only `activeRuns.First().Id`. Change `TriggerConcurrencyDecision` to carry `IReadOnlyCollection<long> RunIdsToCancel` and have `TriggerDispatcher` loop over them.
- **Tests:** consumer unit test (already-Cancelling run still gets CTS cancelled); enforcer unit test with 2 active runs → both cancelled.

---

## Phase 3 — Feature completeness (P1)

### 3.1 Implement `$shared` and template expressions

**Bug:** `ExpressionEvaluator.cs:18` throws NotImplemented for `SharedVariableRefExpression` and `TemplateExpression` — the design's flagship shared-counter scenario cannot run.

**Changes in `Wbskt.Workflow/Runtime/ExpressionEvaluator.cs`:**
- The evaluator is a singleton without provider access. Inject `IServiceScopeFactory`; on `SharedVariableRefExpression`, create a scope, resolve `ISharedVariableProvider`, call `GetByWorkflowRefIdNameAsync(branch.WorkflowDefinitionRefId, name, ct)`, parse `ValueJson` to `JsonElement`. Missing variable → return JSON null (not throw), matching how `$trigger` misses behave.
- `TemplateExpression`: interpolate sub-expressions into the template string (inspect the model in `Wbskt.Workflow.Abstraction/Models/Expressions/TemplateExpression.cs` for its parts shape) and return a JSON string element.
- **Tests:** extend `ExpressionEvaluatorTests` — `$shared` read hits provider; missing var → null; template with mixed literal + refs.

### 3.2 Variable node: Increment/Decrement + last-writer-wins Set

**Bug:** `VariableNodeExecutor.cs:34` rejects everything except `Set`; `Set` on shared scope uses a 3-attempt CAS loop that can fail — inverted vs design §2.14 (`Set` = last-writer-wins; counters = atomic SPs, no retry loops). `SharedVariable_Increment.sql` already exists and is correct.

**Changes in `Wbskt.Workflow/NodeExecutors/Controls/VariableNodeExecutor.cs`:**
- `Op == Increment | Decrement`, `Scope == Shared`: call `ISharedVariableProvider.IncrementAsync/DecrementAsync` (add to the interface/provider if missing — SPs `SharedVariable_Increment`/`SharedVariable_Decrement` exist). If the SP returns no row (var not initialized), call `InitializeAsync(refId, var, "Counter", delta.ToString())` and continue.
- `Op == Increment | Decrement`, `Scope == Local`: read current local value from `ctx.Branch.LocalState`, numeric add, emit patch.
- `Op == Set`, `Scope == Shared`: replace the CAS loop with `SetAsync` (SP `SharedVariable_Set`), keeping the Initialize-if-missing fallback (`SharedVariable_Set` is UPDATE-only; if the post-select returns no row, Initialize).
- Keep `CompareAndSetAsync` available on the provider for future explicit CAS ops but stop using it here.
- **Tests:** `VariableNodeExecutorTests` — increment shared/local, decrement, set overwrite, uninitialized-var bootstrap.

### 3.3 Sequential ForEach + Delay-in-loop fix

**Bugs:** `ForEachNodeExecutor` forks all items in parallel and fires `done` immediately (design §2.8 specifies sequential back-edge iteration). Separately, `DelayNodeExecutor` never clears `__delay_until`, so a Delay revisited in any loop skips its wait.

**Prerequisite plumbing — local-state key removal:** `BranchLoop.MergeLocalState` can only add/overwrite keys. Add `IReadOnlyCollection<string>? RemoveKeys` to `NodeExecutionResult.Continue` (default null; update the `[JsonDerivedType]` payload shape is fine — it's only serialized for the idempotency cache) and have `MergeLocalState` remove those keys. Update `WaitForBookmark` similarly if needed (not required today).

**`ForEachNodeExecutor` rewrite (sequential):**
- Iterator key: `__foreach:{ctx.Node.NodeId:N}:index` in local state.
- Each visit: evaluate the collection; read index (default 0). If `index < count` → return `Continue("body", patch: { item = items[index], index, iteratorKey = index + 1 })`. Else → return `Continue("done", RemoveKeys: [iteratorKey])`.
- The graph back-edge (body tail → ForEach `in`) already works — cycles are allowed and `ResolveNextNodeId` follows edges normally.
- Check `Wbskt.Workflow.Builder/WorkflowBuilder.cs` — its `AddForEach` (used by E2E tests) must wire the body tail back to the ForEach node. Update the builder if it currently wires body → done linearly.
- **`ParallelForEachNodeExecutor` unchanged** (it remains the fan-out node).
- **`DelayNodeExecutor`:** on the resume-visit `Continue`, pass `RemoveKeys: [DelayUntilKey]` so a later visit re-arms the delay.
- **Tests:** rewrite `ForEachNodeExecutorTests` for sequential semantics; update `ForEachFanOutE2ETests` / `NestedLoopE2ETests` / `WorkflowCommandLoopTests` expectations (items processed in order, `done` after last). New unit test: Delay → loop back → Delay waits again.

### 3.4 Join: failure contributions, continuation spawn, aggregator cleanup

**Bugs:** `JoinNodeExecutor.cs:36` hardcodes `outcome = "succeeded"`; a forked branch that fails never contributes, so `Mode=All` cohorts never complete and everything after Join is silently skipped; Quorum's `FailedCount` is dead; `JoinAggregators` rows are never deleted.

**Changes:**
- **Store mode at initialize:** move `Mode` + `QuorumCount` into `JoinAggregators` (new columns) and `JoinAggregator_Initialize.sql`; `JoinAggregator_Contribute.sql` drops its `@Mode/@QuorumCount` params and reads them from the row. `ParallelForEachNodeExecutor` resolves its paired Join node from the definition (walk edges from its `body` port breadth-first until a `JoinNode` is found — guaranteed by the new validator rule in 3.6) and passes the config to `InitializeAsync`.
- **Failed branches contribute:** in `BranchLoop`'s Fail handling (before `SetFailedAsync`), if the branch's local state contains `__join_token`, resolve `IJoinAggregatorProvider` and `ContributeAsync(token, "failed")`. If the response says `ShouldContinue` (quorum satisfied by this failed arrival — e.g. Mode=All with mixed outcomes), spawn a fresh continuation branch: create a `BranchRow` (`Active`, empty locals except the join counters patch, `NodeId` = the node on the far side of the Join's `default` edge), increment `ActiveBranchCount`, dispatch it. Extract this "spawn continuation" logic into a small helper shared with `JoinNodeExecutor`'s inline-continue path — but note the executor path continues inline on the arriving branch (existing behavior, fine); only the failed-arrival path needs the spawn.
- **Quorum semantics check:** with mode stored, `Mode='All'` should complete when `ContributedCount >= ExpectedCount` regardless of outcomes (already true); decide continuation port by outcome mix if the Join node later grows failed-path ports (out of scope; keep single `default` port).
- **Cleanup:** new SP `JoinAggregator_DeleteByRunId.sql`; call it from `RunFinalizer` alongside `DeleteAllByRunIdAsync` for bookmarks. Also add `ContinueClaimed` rows purge inside `Contribute` when claimed? No — keep rows until run-finalize for observability.
- **Tests:** `JoinNodeExecutorTests` + `JoinAggregatorProviderTests` for stored-mode contribute; `BranchLoopTests` case: PFE of 3, one child fails, Join continuation still spawns (Mode=All), run ends `PartiallyFailed`; finalizer deletes aggregator rows.

### 3.5 Webhook trigger scoping + secret

**Bugs:** TriggerKey is `webhook:{path}` — global across workspaces (`TriggerRegistrationService.cs:43`); `InboundWebhookController` accepts unauthenticated POSTs; engine host CORS is `AllowAnyOrigin`.

**Changes:**
- TriggerKey becomes `webhook:{WorkflowRefId}:{path}` (WorkflowRefId is already on the registration row and stable across versions).
- Route becomes `POST api/inbound/webhook/{workflowRefId:guid}/{**path}` in `InboundWebhookController`; MatchKeys = `[$"webhook:{workflowRefId}:{path}"]`. Update `CorrelationKeyResolver`'s `"webhook"` branch to include the refId (payload gains `workflowRefId`).
- Add optional `Secret` to `WebhookTriggerConfig` (`Wbskt.Workflow.Abstraction/Models/Triggers/WebhookTriggerConfig.cs`); copy to a new `SecretHash` column? Keep v1 simple: store the secret on the registration row (new nullable `WebhookSecret NVARCHAR(200)` column + SP updates) and have the controller-side dispatch compare a `X-Wbskt-Secret` header. Comparison happens in `TriggerDispatcher` after registration lookup (the controller doesn't know registrations): add `SecretHeader` to the webhook event payload and check `registration.WebhookSecret == null || registration.WebhookSecret == provided` before proceeding; mismatch → skip registration + history-worthy log.
- CORS: remove `AllowAnyOrigin` default policy from the engine host `Program.cs` (webhooks are server-to-server; the wake endpoint doesn't need CORS either). If dashboard needs it, scope to specific origins via config.
- **Tests:** update `WebhookTriggerE2ETests` and `WaitForHttpWorkflowE2ETests` for the new route; unit tests for secret match/mismatch; two workflows with the same path in different workspaces both fire only their own.

### 3.6 Validator additions

**File:** `Wbskt.Workflow.Abstraction/Validation/WorkflowValidator.cs` (+ `WorkflowValidatorTests`).

Add, per design's locked decisions:
1. **Duplicate outbound edges (owner decision #3):** Error `DUPLICATE_PORT_EDGE` when two edges share the same `(From.NodeId, From.PortId)` — this is what makes `BranchLoop.ResolveNextNodeId`'s `SingleOrDefault` safe. Exception: allow multiple edges only if we ever add explicit fan-out; today, reject.
2. **`ContinueOnError` requires an `error` port (Q9):** Error when a node's `OnFailure.Outcome == ErrorOutcome.ContinueOnError` and the node declares no output port `"error"`. Note `GetExpectedPorts` currently only grants `error` to `action:webhook` — extend it so any action node MAY declare an optional `error` output (change the extra-port check to tolerate `error` on `action:*`).
3. **ParallelForEach ↔ Join pairing (§2.9):** Error when a `ParallelForEachNode`'s `body` subgraph (BFS over edges) contains no `JoinNode`. Reuse the same BFS helper that 3.4's PFE executor uses — put it in a shared static (e.g. `WorkflowGraph.FindDownstream<TNode>` in `Wbskt.Workflow.Abstraction/Models/WorkflowDefinition.cs` or a new `Validation/GraphWalk.cs`).
4. **Correlation expression sanity (§4.5):** Warning when a trigger config's `CorrelationKey` is non-null but neither starts with `$trigger.` nor is a plain constant; Warning when `ConcurrencyPolicy != AllowParallel` and `CorrelationKey` is null ("policy will behave as AllowParallel").
5. **`JumpToNode` target exists:** Error when `OnFailure.TargetNodeId` is set but not a NodeId in the definition.

### 3.7 Trigger filter expressions (optional — keep last in phase)

`FilterExpression` is plumbed through registrations but always null (`TriggerRegistrationService.cs:91`). Wire it: add `Filter` to `ClientTriggerConfig`/`WebhookTriggerConfig` (a `WorkflowExpression` JSON blob), copy to the registration at publish, and in `TriggerDispatcher.DispatchAsync` evaluate it against the event payload before the concurrency check (reuse `IExpressionEvaluator` with a payload-backed context); false → skip registration and log debug (design's `TriggerDispatchSkipped`). If the expression-context plumbing turns out large, defer this item — it's the only net-new feature in the plan.

---

## Phase 4 — DB hygiene (P2)

### 4.1 Indexes
- `Tables/IdempotencyKeys.sql`: add `CREATE INDEX IX_IdempotencyKeys_CreatedAt ON dbo.IdempotencyKeys (CreatedAt);` (the GC in `IdempotencyKey_DeleteExpired.sql` scans it hourly; this is the fastest-growing table — one row per inbound event + per action attempt).
- `Tables/PendingTriggerEvents.sql`: add `IX_PendingTriggerEvents_EnqueuedAt` for `PendingTriggerEvent_DeleteExpired`.
- `Tables/Runs.sql`: extend the filtered active index to `(WorkflowRefId, TriggerNodeId, CorrelationKey)` to exactly cover `Run_GetActiveBy_Correlation`.

### 4.2 SP fixes
- `PendingTriggerEvent_DequeueNextBy_WorkflowDefinitionId_Correlation.sql`: add `AND p.TriggerNodeId = @TriggerNodeId` (param exists in the provider call chain; verify `PendingTriggerEventProvider.DequeueNextAsync` passes it — the SP currently drops it, so two triggers in one workflow with the same correlation cross-drain).
- `Bookmark_DeleteOrphans.sql`: add `N'Faulted', N'OutOfCredits'` to the terminal list.
- `SharedVariable_CompareAndSet.sql`: header comment noting the string comparison is whitespace-sensitive (callers must serialize canonically) — no behavior change.

### 4.3 Match-key consistency (latent bug)
`BranchLoop.GetWakeConditionMatchKey` maps `InboundWakeCondition` to `{DeviceRefId}:{PropertyName}` but the client adapter emits `client:{refId}:{type}` (`ClientPayloadReceivedConsumer.cs:15`). No executor creates `InboundWakeCondition` yet, so it's latent — fix the mapping to `client:{DeviceRefId}:{PropertyName}` now so the first wait-for-device-property executor isn't born broken. Add a unit test pinning every WakeCondition kind's match key against the corresponding adapter/resolver key format (there's a comment at `BranchLoop.cs:508` demanding exactly this invariant).

### 4.4 INT → BIGINT (defer — do NOT do in this pass)
`Runs.Id`/`Branches.Id` are `INT` while code models them as `long` with `checked((int))` casts. Migration is invasive (FKs everywhere). Record as a known limitation in the design doc (Phase 6); revisit before production scale.

---

## Phase 5 — Code hygiene (P2)

### 5.1 RetryExecutor cleanup (`Wbskt.Workflow/Runtime/RetryExecutor.cs`)
- Apply the `RetryOn` filter: when `policy.RetryOn` is non-empty, only retry a transient failure whose `ErrorCode` or exception type name is in the list.
- Delete the dead side-effect-free branches: the `else { UpsertPending + Mark* }` blocks can never run because `idempotencyKeyProvider` is only resolved when `!isSideEffectFree` (line 35). Decide with a comment: side-effect-free executors get NO idempotency rows (current de-facto behavior; design's write-after cache is dropped deliberately).
- Extract one `RecordOutcomeAsync(provider, keyValue, isFailure, json, ct)` helper replacing the six duplicated try/catch blocks.
- `GetDelay`: use `Random.Shared`.
- Update `RetryExecutorTests` accordingly (add RetryOn cases).

### 5.2 RunFinalizer / history polish
- Emit `BranchFailed` history event in `BranchLoop`'s fail path and `BranchCancelled` severity per design §5.8 (`BranchFailed` → Warn). Centralize the kind→severity mapping in a small static (extend the existing ternary in `AppendEventAsync`).

### 5.3 Publish flow (`Hosts/Wbskt.Management.Host/Services/Workflow/WorkflowDefinitionService.cs`)
- Version authority: `WorkflowDefinition_Publish.sql` computes `@NextVersion` under HOLDLOCK while the service pre-computes `nextVersion` in C# and bakes it into `DefinitionJson` — they can disagree under concurrent publishes. Fix in the SP: `SET @DefinitionJson = JSON_MODIFY(JSON_MODIFY(@DefinitionJson, '$.version', @NextVersion), '$.isEnabled', CAST(1 AS BIT))` before INSERT (verify the JSON property casing matches `JsonSerializerDefaults.Web` output: `version`). The service keeps its computed value only for the response DTO — read the real version from the inserted row instead.
- Narrow `catch (Exception) { existing = null; }` (line 62) to the provider's not-found exception (`KeyNotFoundException`) so transient DB errors don't skip the workspace-ownership check.

### 5.4 Compensation (`Wbskt.Workflow/Runtime/CompensationOrchestrator.cs`)
- Replace the bare `catch { }` (line 62-65) with: log warning + append a `CompensationFailed` history event (Warn) carrying the node id and error. Still non-blocking per design §5.5.
- Recovery of `Compensating` branches is handled by 1.1's SP change; verify `BranchLoop` re-entry with a `Compensating` branch behaves (it will re-run the node at `CurrentNodeId` — acceptable; compensation `IdempotencyKey` strings exist but are never persisted, note as known gap in the doc).

### 5.5 BranchExecutionPump (`Hosts/Wbskt.Workflow.Engine.Host/HostedServices/BranchExecutionPump.cs`)
- Move the hardcoded `50` to `WorkflowEngineOptions.BranchWorkerLimit` (default 50).
- On unhandled exception from `loop.RunAsync` (line 40-43): in addition to logging, attempt a best-effort `SetFailedAsync` + counter decrement + finalize so the run doesn't leak (wrap in its own try/catch; if that fails too, the fixed RunReaper catches it).

### 5.6 Misc
- `BranchLoop` ctor: remove the duplicated `_workflowMetrics = workflowMetrics;` assignment (lines 69/74).
- `TriggerDispatcher.EvaluateCorrelationExpression`: leave as-is for now but add a `// TODO(arch): unify with IExpressionEvaluator` marker — full unification rides with 3.7.
- Delete resolved `[RJ]:` comments in files this plan touches (`BranchLoop.cs`, `BookmarkResumer.cs`, `RunFinalizer.cs`, `RunStarter.cs`, `RunCancellationService.cs`, `PendingTriggerEventDrainer.cs`, `TriggerDispatcher.cs`).

---

## Phase 6 — Design doc reconciliation

Update `Docs/Workflow.Engine.V3.Design.md` to match the (now-fixed) implementation. Sections to amend:
- §2.6/§2.10: `NodeExecutionResult.Continue` has `LocalStatePatch` (+ new `RemoveKeys`), no `Output`; there is no `$output`/`LastOutput` flow and no Run-scope state — all state flows through branch-local patches. `$trigger` is materialized as `LocalJson["trigger"]` seeded at run start and copied to forks (document the current mechanism and its caveat: a node patch could overwrite the `trigger` key; note as accepted v1 trade-off or open item).
- §3: single-row bookmark model is now real; document `Bookmark_ClaimByRefId`/`Bookmark_ClaimDue` as the claim primitives; remove the "ResumeViaBookmark" pseudocode or mark it superseded by the two concrete paths (resumer + scheduler).
- §4: trigger executors exist and the initial branch starts AT the trigger node (not its downstream target); TriggerKey formats are the implemented ones (`client:{ref}:{type}`, `webhook:{workflowRefId}:{path}` after 3.5, `schedule:{fireId}`, `manual:{refId}`); registrations are deleted (not disabled) on supersede/deprecate.
- §5: run terminal statuses are `Succeeded | Failed | PartiallyFailed | Cancelled | Faulted | OutOfCredits` ("Succeeded" not "Completed"); `OnFailure` values are `FailBranch | FailRun | ContinueOnError | ContinueAsSucceeded | JumpToNode | Compensate` — document `JumpToNode` and the Guid-port jump mechanism in `ResolveNextNodeId`.
- §6: history events are written synchronously (already noted); IdempotencyKeys double as the inbound-event dedup/claim table — document that pattern (currently only explained in a code comment in `BookmarkResumer.cs`).
- §7: actual hosted-service names (BranchExecutionPump, BookmarkScheduler, ScheduledFireTicker, RunReaper, RunRecoveryService, HistoryRetentionGc, IdempotencyKeyGc, PendingTriggerEventBacklogReaper, MetricsExporter).
- Add "Known limitations" list: INT ids (4.4), no trigger payload immutability enforcement, compensation idempotency keys not persisted, `SqlRunDispatcher`/`SqlLeaseHolder` still future.

---

## Suggested implementation order & dependencies

```
Phase 1:  1.1 → 1.2 (needs 2.1's idempotent finalizer — implement the Run_SetTerminal guard early, it's small)
          1.5, 1.6 independent
          1.3 independent
          1.4 largest item; land last in phase
Phase 2:  2.1 first (1.2 and 2.3 depend on it) → 2.2 → 2.3
Phase 3:  3.1 → 3.2 (both shared-variable); 3.3; 3.6 before 3.4 (graph-walk helper + pairing rule); 3.5; 3.7 last/optional
Phase 4/5: any order
Phase 6:  last
```

Practical tip: pull the `Run_SetTerminal` guard (first bullet of 2.1) into Phase 1 alongside 1.2 since the reaper fix depends on idempotent finalization.

## Verification

After each phase:
1. `dotnet build Wbskt.slnx` — zero warnings introduced.
2. `dotnet test Tests/Wbskt.Workflow.Engine.Host.Tests` — full unit suite green.
3. `dotnet test Tests/Wbskt.Workflow.Engine.Host.IntegrationTests` — needs a SQL Server with the DB project deployed; deploy the DACPAC/scripts from `Databases/Wbskt.Database` first (all SP changes ship there). If the environment lacks SQL, state so explicitly rather than skipping silently.
4. E2E (`Tests/Wbskt.E2E.FeatureTests`) uses `SkippableFact` and requires the hosts running (see `Tests/Wbskt.E2E.FeatureTests/README.md` / `ServicesFixture`); run at least: `TriggerQueuePolicyE2ETests` (new), `DelayWorkflowE2ETests`, `BookmarkTtlRaceE2ETests`, `NodeRetryE2ETests`, `SagaCompensationE2ETests`, `ScheduleTriggerE2ETests`, `WebhookTriggerE2ETests`, `SubWorkflowE2ETests`.
5. Manual smoke for the headline fixes:
   - Publish a workflow with a 5-field cron → observe ≥2 fires.
   - Start a run with a 45-minute Delay → confirm the reaper leaves it alone (check `Runs.Status` stays `Running`, bookmark row present).
   - Kill the engine host mid-run → restart → branch resumes (was `Active`, gets re-dispatched, completes).
   - Queue policy: 3 rapid events, same correlation → 3 serialized runs.
