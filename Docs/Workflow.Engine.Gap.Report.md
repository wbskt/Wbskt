# Workflow Engine — Gap & Issue Report

> ## ⚡ HANDOFF — read this first
>
> **This document is self-sufficient. You do not need any prior conversation.**
>
> A user-perspective review of the workflow engine catalogued 36 gaps (`WF-01`…`WF-36`), and a 37th
> was found while fixing WF-31. **34 are done, 2 are partly done, 1 is deferred by design.** Each item
> below carries symptom, root cause with `file:line`, fix, tests and acceptance criteria.
>
> **What is actually left**, and both are owner decisions rather than unwritten code:
>
> | Item | What remains | Why it was not just done |
> |---|---|---|
> | **WF-10** | Windowed `MaxConcurrency` on `ParallelForEach`. The hard `MaxFanOut` ceiling landed and meets the acceptance criterion. | Needs pending items on the aggregator row, an atomic pop inside `JoinAggregator_Contribute`, and a branch spawn on both contribution paths — surgery on exactly the code WF-03 fixed, **unverifiable without a live SQL Server**. Getting it wrong ships as "cohorts hang". Full design sketch is in the item. |
> | **WF-29** | A durable record of arrivals that produce *no* run. Everything else landed. | Needs its own table; an arrival that matched nothing has no workspace to scope an endpoint to, it costs an insert on the hottest path, and WF-32 already tells a caller *why* nothing ran. Three decisions, all the owner's. |
> | **WF-36** | Nothing — deferred by design. `INT` keys modelled as `long`, contained by `checked` casts so the ceiling is loud rather than silent. | Document only, do not action. |
>
> ### Where the work lives
>
> Branch `claude/workflow-engine-gap-report-e12741`, in a worktree at
> `C:\dev\wbskt\Wbskt\.claude\worktrees\sleepy-shaw-b56b7c`.
>
> | Commit | Covers |
> |---|---|
> | `da022d0` | WF-01, 02, 03, 04, 05, 06, 07, 17, 18, 19, 20, 21, 22, 33 |
> | `b9703df` | WF-14, WF-23 |
> | `613fea8` | WF-13 |
> | `7ba1312` | WF-29 (partial) |
> | `c1e4e95` | WF-26 (per-workflow) |
> | `ef7dc51` | WF-27, WF-30 |
> | `6f77180` | WF-24, WF-25, WF-34 (partial) |
> | `fe7e8a3` | WF-32 |
> | `5d14abf` | WF-31 |
> | `2638858` | WF-15, WF-16 |
> | `b3318df` | WF-09, WF-10 (ceiling) |
> | `bc54888` | WF-07, WF-28 |
> | `d52ec61` | WF-08 |
> | `1ebd3af` | WF-37 |
> | `3d6110e` | WF-35 |
> | `342ea10` | WF-26 (workspace rollup) |
> | `057c354` | WF-29 (inbound arrivals in the run trace) |
> | `033fa10` | WF-34 (`RetryExecutor` cleanup, remaining markers) |
>
> ### ⚠️ Live caveats — do not lose these
>
> 1. **A DACPAC redeploy is outstanding, and has grown.** The owner last deployed mid-way through the
>    earlier commits. Since then the schema has changed as follows, and **none of it is deployed**:
>    - `HistoryEvent_DeleteForRetiredRuns` — changed signature; three procedures deleted (WF-24, WF-25).
>    - `TriggerRegistrations.WebhookSecret` — **new nullable column**, plus all four
>      `TriggerRegistration_*` procedures updated (WF-16).
>    - `Run_GetStatsBy_WorkspaceId`, `Run_GetStatsPerWorkflowBy_WorkspaceId` — **two new procedures**
>      (WF-26).
>
>    Until it is deployed, webhook triggers and the workspace stats endpoint will fail against the
>    live database.
> 2. **The E2E suite has never been run in-session.** It needs live hosts and failed at fixture setup
>    with HTTP 429 from the auth host. The owner plans to run it once the items are finished.
>    Two known problems waiting there:
>    - `ForEachFanOutE2ETests` was rewritten for sequential `ForEach` (WF-04) but never executed.
>    - **`NestedLoopE2ETests` builds a definition the new PFE↔Join rule rejects** — its
>      `AddFork(...)` has no join mode, and its expected command count of 31 came from the old
>      fan-out arithmetic. It needs a join mode and a re-derived count.
> 3. **Toasts reach every workspace member.** The notification hub feed is per workspace, not per
>    permission (`Docs/API.Endpoints.md` §2.8). WF-01 inherited this; nothing permission-sensitive
>    should go in a toast until it is closed.
> 4. **Indexes (WF-23) were reasoned from query shapes, not measured.** No execution plan has been
>    inspected.
>
> ### Environment notes that will save you an hour
>
> - **Mixed line endings.** Some files are LF, some CRLF. Bulk `perl`/`sed` patterns anchored on
>   `;\n` silently match only half the files — always allow `\r?\n`.
> - **The integration fixture builds its own database.** `SqlEdgeFixture` connects to **`master`**
>   (default `localhost,1433`, `sa`/`Welcome1234`, override with `WBSKT_INTEGRATION_CONNSTR`),
>   creates a throwaway DB, deploys the DACPAC, and drops it. Pointing it at a pre-deployed database
>   does nothing. When it cannot connect, **34 of 36 tests silently "pass" by skipping** — check with
>   `--logger "console;verbosity=detailed"` and grep for `SKIPPED`.
> - **Adding a method to `IRunProvider`/`IHistoryEventProvider` means patching ~15 test doubles.**
>   Expect it; batch it.
> - Commands:
>   ```
>   dotnet build Wbskt.slnx -v q --nologo
>   dotnet test Tests/Wbskt.Workflow.Engine.Host.Tests/Wbskt.Workflow.Engine.Host.Tests.csproj --nologo -v q
>   dotnet test Tests/Wbskt.Workflow.Engine.Host.IntegrationTests/Wbskt.Workflow.Engine.Host.IntegrationTests.csproj --nologo -v q
>   ```
>   Current baseline: **641 unit tests passing** (574 when this document was written). The 36 DB
>   integration tests have **not** been re-run since the schema changes above — they need a live SQL
>   Server, and silently skip without one.
>
> ### Decisions already taken — do not re-litigate
>
> | Decision | Where |
> |---|---|
> | Expressions are **strictly typed, no coercion**. `"5" ≠ 5`. Ordering across kinds is an error; equality across kinds is just "not equal". | WF-02 |
> | Logic conditions are **structured `WorkflowExpression`**, with a converter that still reads the legacy bare-string form. **No data migration is needed.** | WF-02 B2 |
> | **No separate enable/disable axis** — `deprecate` *is* disable; `reinstate` makes it reversible. **No rename** (name lives in the versioned definition). **No delete** (runs FK to definitions). | WF-13 |
> | **`node_id` is deliberately not a metric tag** — unbounded cardinality. Per-node timings come from the history stream instead. | WF-27 |
> | A `Fork` with no downstream `Join` is **legal**; only `ParallelForEach` requires one. | WF-03, WF-19 |
> | Rollback republishes as a **new version**; history stays append-only. | WF-13 |
> | A **secret or credential never lives in a workflow definition** — SMTP password, bot token, webhook secret. A definition is readable by the whole workspace and frozen into every published version. | WF-08, WF-16 |
> | A trigger filter **fails closed**: one that cannot be evaluated has not been passed. | WF-15 |
> | A **lossy summary plus full detail** beats a cleverer summary — dispatch outcomes, workspace success rate. | WF-32, WF-26 |
> | Workspace success rate is **run-weighted**, with a per-workflow breakdown beside it. Averaging per-workflow rates was rejected. | WF-26 |
> | Credit costs are **configuration**, not compiled in; outbound I/O costs more than bookkeeping. | WF-28 |
> | `CompareAndSet` losing its race is a **normal outcome**, reported as `casSucceeded`, not a node failure. | WF-07 |
>
> ### Things that will bite the next person
>
> - **`NodeKind.NotYetImplemented` is now empty.** The `NODE_KIND_NOT_IMPLEMENTED` validator rule stays
>   for the next kind that lands ahead of its executor. `NodeExecutorRegistryTests` pins the set against
>   the real DI container, so adding a kind without an executor fails there first.
> - **Cancellation now really interrupts a running node** (WF-37). Any new executor must let
>   `OperationCanceledException` propagate — `RetryExecutor` rethrows it rather than turning it into
>   `EXECUTOR_CRASH`, and `BranchLoop` is the only place that can tell a run cancel from a host shutdown.
> - **The E2E suite has still never been run** (see caveat 2), and now has more surface to cover:
>   webhook secrets, trigger filters, sub-workflow inputs, and the two new notification nodes.

---

## Context

A user-perspective review (2026-08-05) of the workflow engine as it stands **after** Phases 1 and 2 of
`Docs/Workflow.Engine.V3.Remediation.Plan.md` landed. Scope of the review: both hosts' controllers,
`Wbskt.Workflow` runtime, all node executors, the validator, every workflow stored procedure, and the
metrics path.

**What is already fixed and verified good** (do not re-fix):

- Crash recovery — `Branch_GetRunning.sql` matches `'Active'`/`'Compensating'`.
- Reaper safety — `Run_GetStuck.sql` excludes runs with live bookmarks or live branches.
- Terminal-status stickiness — `Run_SetTerminal.sql` guards on non-terminal source status and reports
  `Transitioned`.
- Single-write cancellation — `Run_TransitionStatus.sql`.
- Bookmark single-row model with `ExpiresAt`/`TtlPort`; atomic claim via `Bookmark_ClaimByRefId` /
  `Bookmark_ClaimDue`.
- Graceful shutdown — `BranchLoop` leaves branches `Active` on host-shutdown cancellation.
- Public callback hardening — `PublicCallbackController` (anonymous, rate-limited, body-capped,
  uniform opaque 202, no match oracle). This is the best-hardened surface in the codebase.

---

## How to use this document

Each item is **self-contained** — it names the files, the exact symptom, the root cause, the fix, and
the acceptance criteria. You can open a fresh chat, point at one item ID, and fix it without rereading
the rest.

Items are grouped by area (A–G) and carry stable IDs (`WF-01` …). Where an item overlaps the existing
remediation plan, the plan section is cross-referenced — **the plan's prose for those is still accurate
against current code** and worth working from directly.

**Severity legend**

| | Meaning |
|---|---|
| 🔴 **P0** | Silently breaks a user's workflow, leaks resources, or makes a headline feature unusable. |
| 🟠 **P1** | Real user-facing gap or wrong behaviour with a workaround. |
| 🟡 **P2** | Hygiene, performance, dead code, or missing polish. |

**Status legend** — each item carries a `**Status:**` line.

| | Meaning |
|---|---|
| ✅ Fixed | Landed and verified. The item is kept for the record; the fix notes describe what was done. |
| ◐ Partial | Some sub-parts landed; the remainder is listed explicitly. |
| ☐ Open | Not started. |

**Repo conventions to preserve** (unchanged from the remediation plan)

- Statuses are strings. Run: `Running | Failing | Cancelling | Succeeded | Failed | PartiallyFailed |
  Cancelled | Faulted | OutOfCredits`. Branch: `Active | Waiting | WaitingAtJoin | Compensating |
  Completed | Failed | Cancelled`.
- SPs named `Table_Action.sql` under `Databases/Wbskt.Database/StoredProcedures/`, tables under
  `Tables/`. Providers extend `BaseSqlProvider` (`Wbskt.Infrastructure`), never depend on other
  providers; orchestration lives in `Wbskt.Workflow/Runtime`.
- Providers are Scoped; engine core Singleton resolving providers via `IServiceScopeFactory`;
  executors keyed by kind string via `NodeExecutorRegistry`.
- Tests: xUnit + Moq + FluentAssertions. Unit tests in `Tests/Wbskt.Workflow.Engine.Host.Tests`, DB
  integration in `Tests/Wbskt.Workflow.Engine.Host.IntegrationTests`, full E2E in
  `Tests/Wbskt.E2E.FeatureTests`.
- DACPAC redeploy is required before integration tests can validate new or changed SPs. DB integration
  tests silently no-op without a live SQL Server (pre-existing `if (!fixture.IsAvailable) return;`
  pattern — not a regression).

---

## Summary table

| ID | Sev | Area | One-line |
|---|---|---|---|
| WF-01 | 🔴 | Runtime | ✅ An `action:toast` node hangs its run forever and survives restarts |
| WF-02 | 🔴 | Expressions | ✅ No comparison/function/boolean logic exists — the Logic node is unusable |
| WF-03 | 🔴 | Control flow | ✅ A failed branch inside `Fork`/`ParallelForEach` hangs the Join permanently |
| WF-04 | 🔴 | Control flow | ✅ `ForEach` is parallel, not sequential, and fires `done` before the body runs |
| WF-05 | 🟠 | Control flow | ✅ A `Delay` inside a loop waits once and is skipped forever after |
| WF-06 | 🟠 | Expressions | ✅ `$shared` variable refs and template interpolation throw at runtime |
| WF-07 | 🟠 | Nodes | ✅ Variable node supports only `Set`; shared `Set` can fail under contention |
| WF-08 | 🟠 | Nodes | ✅ `action:email` and `action:telegram` are unimplemented stubs |
| WF-09 | 🟠 | Nodes | ✅ Sub-workflow cannot receive input from its parent |
| WF-10 | 🟠 | Nodes | ◐ No fan-out concurrency cap on `ForEach`/`ParallelForEach` |
| WF-11 | 🟠 | API | ✅ Normal outcomes on "start run" return HTTP 500 |
| WF-12 | 🟠 | API | ✅ No validate / dry-run endpoint |
| WF-13 | 🟠 | API | ◐ Deprecate is one-way; no pause/resume, rename, rollback, or delete |
| WF-14 | 🟠 | API | ✅ No workspace-wide run list |
| WF-15 | 🟠 | Triggers | ✅ Trigger filter expressions are plumbed but hardcoded to null |
| WF-16 | 🟠 | Triggers | ✅ Webhook triggers have no secret |
| WF-17 | 🟠 | Triggers | ✅ Invalid cron publishes the workflow, then throws — leaving it scheduleless |
| WF-18 | 🟠 | Validator | ✅ Duplicate `(node, port)` edges throw at runtime instead of failing at publish |
| WF-19 | 🟠 | Validator | ✅ Five more missing publish-time checks |
| WF-20 | 🟡 | Database | ✅ `PendingTriggerEvent_DequeueNextBy_…_Correlation` dropped `TriggerNodeId` (was dead code — deleted) |
| WF-21 | 🟠 | Database | ✅ `Bookmark_DeleteOrphans` omits `Faulted` |
| WF-22 | 🟠 | Database | ✅ Publish version authority is split between C# and the SP |
| WF-23 | 🟠 | Database | ✅ Four missing indexes, one of them on the user-facing run list |
| WF-24 | 🟡 | Database | ✅ Unbounded `Warn`/`Error` history growth |
| WF-25 | 🟡 | Database | ✅ Three dead stored procedures |
| WF-26 | 🟠 | Analytics | ✅ No user-facing analytics endpoint at all |
| WF-27 | 🟠 | Analytics | ✅ Metrics are not workspace-scoped and node timings are not per-node |
| WF-28 | 🟠 | Analytics | ✅ Credit accounting is a stub — every node costs exactly 1.0 |
| WF-29 | 🟠 | Analytics | ◐ History trace is missing inbound, retry, resume and charge events |
| WF-30 | 🟡 | Analytics | ✅ `FlusherLag` gauge is hardcoded to zero |
| WF-31 | 🟠 | Runtime | ✅ Cancellation state is cached per-host with no cross-host invalidation |
| WF-32 | 🟠 | Runtime | ✅ Multi-registration dispatch reports only the first run started |
| WF-33 | 🟡 | Runtime | ✅ Branch worker limit is hardcoded; pump has no failure containment |
| WF-34 | 🟡 | Hygiene | ✅ Resolved `[RJ]:` markers and duplicate assignments |
| WF-35 | 🟡 | Hygiene | ✅ Shipped example generator emits a workflow that fails at runtime |
| WF-36 | 🟡 | Runtime | ☐ `INT` primary keys modelled as `long` with checked casts |
| WF-37 | 🟠 | Runtime | ✅ A run's `CancellationToken` can never be cancelled (found 2026-08-07 during WF-31) |

---

# A. Runtime correctness — silent hangs and leaks

## WF-01 🔴 An `action:toast` node hangs its run forever and survives restarts

**Status:** ✅ Fixed 2026-08-06 — all three parts. Containment and the toast implementation landed
first; the publish-time gate followed as **WF-19 item 1**, which now rejects any kind the engine
cannot run.

*Containment.* `NodeExecutorRegistry.For` now throws a dedicated `NodeKindNotSupportedException`
(`Wbskt.Workflow.Abstraction/Exceptions/NodeExceptions.cs`) instead of `InvalidOperationException`, and
`BranchLoop` resolves the executor **inside** its `try`, mapping that exception to
`Fail("NODE_KIND_NOT_SUPPORTED", …)`. Any unregistered kind now fails its branch cleanly — counter
decremented, run finalized, no reaper needed, no restart crash-loop. Regression test:
`BranchLoopTests.RunAsync_fails_branch_cleanly_when_node_kind_has_no_executor`. WF-33's pump
containment is the second, generic layer behind this.

*Toast implemented* (owner decision: workspace toast, not removal). A toast has no device target, so
it does not reuse the client-message path:

- `Events/Wbskt.Events/Workflow/WorkflowToastEvent.cs` — carries `IWorkflowContext` (so a dashboard
  can link the toast back to its run) and `[SignalRNotify("OnWorkflowToastEvent")]`.
- `Wbskt.Workflow.Abstraction/Runtime/IToastPublisher.cs` — abstraction mirroring
  `IDeviceCommandPublisher`, keeping `Wbskt.Workflow` free of an event-bus dependency.
- `Wbskt.Workflow/NodeExecutors/Actions/ToastNodeExecutor.cs` — publishes and continues; a bus
  failure is a **retryable** `TOAST_PUBLISH_ERROR`.
- `Hosts/Wbskt.Workflow.Engine.Host/InboundAdapters/ToastPublisher.cs` + DI registration.
- `WorkflowBuilder.AddToast(title, message)` for parity with `AddClientMessage`.

No management-host wiring was needed: `SignalRForwardingExtensions.AddAutoSignalRForwarding` scans
`Wbskt*` assemblies for `[SignalRNotify]` events and registers a forwarder automatically, so the event
reaches `NotificationHub.GroupName(workspaceId)` on its own.

**⚠ Carried limitation.** The hub feed is per workspace, not per permission (`Docs/API.Endpoints.md`
§2.8 known gap) — every workspace member with a live connection receives the toast. This is noted in
the event's XML doc: nothing permission-sensitive should be put in `Title`/`Message` until that gap is
closed.

**Symptom (user).** Author publishes a workflow containing a Toast notification node. Publish
succeeds. The run starts, reaches the toast node, and then never finishes — no failure, no history
event, no terminal status. It stays `Running` indefinitely. Restarting the engine re-dispatches it and
it hangs again.

**Root cause.** Three things line up:

1. `ToastNotificationNode` deserializes fine — `BaseNodeJsonConverter.ResolveNodeType` maps
   `NodeKind.ActionToast` (`Wbskt.Workflow.Abstraction/Models/Nodes/BaseNodeJsonConverter.cs`).
2. It validates fine — `WorkflowValidator.GetExpectedPorts` grants every `action:*` kind an `in` +
   `default` port (`Wbskt.Workflow.Abstraction/Validation/WorkflowValidator.cs:182`).
3. **No executor is registered for it.** `WorkflowServiceCollectionExtensions.AddWorkflowRuntime`
   registers `CommandNodeExecutor`, `WebhookNodeExecutor`, `EmailNodeExecutor`,
   `TelegramNodeExecutor` — and nothing for `action:toast`
   (`Wbskt.Workflow/Extensions/WorkflowServiceCollectionExtensions.cs:106-109`).

The hang comes from *where* the lookup happens. `NodeExecutorRegistry.For` throws
`InvalidOperationException` (`Wbskt.Workflow/Runtime/NodeExecutorRegistry.cs:31`), and `BranchLoop`
calls it at **line 134 — outside the `try` that starts at line 138**
(`Wbskt.Workflow/Runtime/BranchLoop.cs:134`). The exception escapes `RunAsync`, reaches
`BranchExecutionPump`, and is only logged (`Hosts/Wbskt.Workflow.Engine.Host/HostedServices/BranchExecutionPump.cs:42`).

Consequences, in order:
- The branch row stays `Active`.
- `RunCounters.ActiveBranchCount` is never decremented, so `RunFinalizer` is never reached.
- `Run_GetStuck.sql` **deliberately excludes** runs with an `Active` branch, so the reaper will never
  collect it.
- `RunRecoveryService` picks the `Active` branch up on next startup and re-dispatches it into the same
  crash.

**Fix.** Three independent changes; do all three:

1. **Contain the blast radius.** Move the registry lookup inside the `try` in `BranchLoop.RunAsync` so
   an unregistered kind becomes `Fail("EXECUTOR_CRASH", …)` and flows through the normal failure path.
2. **Fail at publish, not at runtime.** Add a validator rule (see WF-19 item 1) rejecting any node
   kind with no registered executor. The validator lives in `Abstraction` and cannot see the DI
   container, so expose the supported kinds as a static set on `NodeKind` (e.g.
   `NodeKind.Implemented`) and assert in a runtime unit test that the registry's keys equal that set —
   that keeps the two from drifting.
3. **Decide toast's fate.** Either implement `ToastNodeExecutor` (a toast is a client message with a
   fixed message type — see `ToastConfig`/`CommandNodeExecutor` for the shape), or remove
   `NodeKind.ActionToast` + `ToastNotificationNode` + `ToastConfig` from the model entirely. Do not
   leave it publishable and unimplemented.

**Also fix alongside** — `BranchExecutionPump`'s catch-all should make a best-effort
`SetFailedAsync` + `DecrementActiveBranchesAsync` + `FinalizeAsync` (each in its own try/catch) so
*any* future escape becomes a failed run rather than a leak. See WF-33.

**Tests.**
- `BranchLoopTests`: node with an unregistered kind → branch ends `Failed`, counter decremented, run
  finalizes `Failed`.
- Registry/DI test: every `NodeKind` constant has a registered executor (or is in an explicit
  `NotImplemented` allow-list).
- `WorkflowValidatorTests`: publishing a definition with an unimplemented kind returns an error.

**Acceptance.** Publishing a toast node either works or is rejected at publish time. No code path can
leave a branch `Active` after an unhandled executor exception.

---

## WF-03 🔴 A failed branch inside `ParallelForEach` hangs the Join permanently

**Status:** ✅ Fixed 2026-08-06.

**What landed.**

- **Mode moved onto the cohort.** `JoinAggregators` gains `Mode`, `QuorumCount` and `JoinNodeId`;
  `JoinAggregator_Initialize` stamps them at fan-out and `JoinAggregator_Contribute` drops its
  `@Mode`/`@QuorumCount` parameters and reads them from the row. This is the enabling change: a
  branch that *fails* contributes from `BranchLoop`, which never sees the Join node's config.
- **Failed branches contribute.** `BranchLoop.ContributeFailureToJoinAsync` runs in the fail path
  before the counter decrement. Best-effort and fully guarded — a problem here must not mask the
  original node failure.
- **Continuation spawn.** When the failed arrival is the one that satisfies the quorum, a fresh
  branch is created at the node past the Join's `default` edge and dispatched. It is counted
  **before** the failed branch's own decrement, so the run cannot finalize out from under it. The
  failed branch keeps its `Failed` status, so the run still ends `Failed`/`PartiallyFailed` while
  post-Join work proceeds. Emits a `JoinContinuationSpawned` history event.
- **Unreachable quorum released.** `Mode='Quorum'` now also releases when
  `(ExpectedCount - FailedCount) < QuorumCount` — once too many members have failed no further
  success is possible, so the cohort would otherwise park forever.
- **Cleanup.** New `JoinAggregator_DeleteAllBy_RunId.sql` called from `RunFinalizer` alongside the
  bookmark delete, plus `IX_JoinAggregators_RunId`.
- **Shared graph helper.** New `Wbskt.Workflow.Abstraction/Models/WorkflowGraph.cs`
  (`FindDownstream<TNode>` BFS + `ResolveTarget`), cycle-safe because loop back-edges are legal.
  **WF-19 item 3 should reuse this rather than writing its own walk.**

**⚠ Scope correction — `Fork` was affected too.** The original writeup framed this as a
`ParallelForEach` problem. `ForkNodeExecutor` also opens cohorts and writes `__join_token`
(`ForkNodeExecutor.cs:31`), so it had the identical hang and is fixed the same way. One difference in
handling: a `ParallelForEach` with no downstream `Join` now **fails** with `PFE_NO_JOIN` (its cohort
could never converge), whereas a `Fork` without one is legal — its branches simply run to their own
ends — so Fork stores a null `JoinNodeId` instead of failing.

**Tests.** `BranchLoopTests.RunAsync_contributes_failure_to_join_when_a_cohort_branch_fails` and
`RunAsync_spawns_join_continuation_when_failed_arrival_meets_quorum`; `ParallelForEachNodeExecutorTests`
gained stamped-config and `PFE_NO_JOIN` cases; `JoinNodeExecutorTests` re-pointed at the
mode-free `ContributeAsync`; `JoinAggregatorProviderTests` cover `JoinNodeId` present and null.

**⚠ Deployment note.** `JoinAggregators` changed shape — a **DACPAC redeploy is required** before
these paths work against a live database.

**Symptom (user).** A `ParallelForEach` over 5 items where one item's body fails. The other four reach
the Join. Everything downstream of the Join never executes. The run sits `Running` until the reaper
eventually cancels it (30 min default). No error explains why.

**Root cause.** `JoinNodeExecutor` hardcodes `string outcome = "succeeded"`
(`Wbskt.Workflow/NodeExecutors/Controls/JoinNodeExecutor.cs:36`). A branch that *fails* never reaches
the Join node at all — it dies in `BranchLoop`'s fail path — so it never contributes. With
`Mode=All`, `JoinAggregator_Contribute.sql` requires `ContributedCount >= ExpectedCount`, which can
now never happen. `FailedCount` in the aggregator is written but never read by any mode.
`JoinAggregators` rows are never deleted either.

**Fix.** Per remediation plan §3.4 (still accurate):

- **Store the mode at initialize.** Add `Mode NVARCHAR(20)` + `QuorumCount INT` columns to
  `Databases/Wbskt.Database/Tables/JoinAggregators.sql` and to
  `JoinAggregator_Initialize.sql`. `JoinAggregator_Contribute.sql` drops its `@Mode`/`@QuorumCount`
  parameters and reads them from the row (a failed branch contributing from `BranchLoop` does not know
  the Join node's config).
- **Resolve the paired Join at fan-out.** `ParallelForEachNodeExecutor` walks edges breadth-first from
  its `body` port until it finds a `JoinNode`, and passes that node's config to `InitializeAsync`.
  Put the BFS in a shared helper (e.g. `Validation/GraphWalk.cs`) — WF-19 item 3 reuses it.
- **Make failed branches contribute.** In `BranchLoop`'s `Fail` handling, before `SetFailedAsync`: if
  the branch's local state has `__join_token`, call `ContributeAsync(token, "failed")`. If the result
  says `ShouldContinue`, spawn a fresh continuation branch — create a `BranchRow` (`Active`, locals =
  the join counter patch, `NodeId` = the node on the far side of the Join's `default` edge), increment
  `ActiveBranchCount`, dispatch it. Extract this as a helper; the executor's inline-continue path
  stays as-is.
- **Clean up.** New `JoinAggregator_DeleteByRunId.sql`, called from `RunFinalizer` alongside the
  bookmark delete.

**Tests.** `JoinNodeExecutorTests` + `JoinAggregatorProviderTests` for stored-mode contribute;
`BranchLoopTests`: PFE of 3, one child fails, continuation still spawns under `Mode=All`, run ends
`PartiallyFailed`, finalizer deletes the aggregator row.

**Acceptance.** A PFE cohort with mixed outcomes always releases its Join exactly once, and the run
reaches a terminal status without the reaper.

---

## WF-20 🟡 `PendingTriggerEvent_DequeueNextBy_…_Correlation` dropped `TriggerNodeId` (dead code)

**Status:** ✅ Fixed 2026-08-06 — by deletion.

**Correction to the original finding.** This was first written up as an active cross-drain bug. It was
not. The broken procedure was reachable only through a **second, unused** `DequeueNextAsync(int
workflowDefinitionId, …)` overload. The single production caller —
`PendingTriggerEventDrainer.DrainAsync` — uses the *other* overload, backed by
`PendingTriggerEvent_DequeueNext.sql`, which filters `WorkflowRefId + TriggerNodeId + CorrelationKey`
correctly. Every unit-test double for the dead overload threw `NotSupportedException`. So the bug was
latent, never reachable in production.

**What landed.** Rather than patch a procedure nobody called, the footgun was removed:
- deleted `PendingTriggerEvent_DequeueNextBy_WorkflowDefinitionId_Correlation.sql` (the `.sqlproj`
  globs `StoredProcedures\*.sql`, so no project edit was needed);
- removed the `DequeueNextAsync(int, string, ct)` overload from `IPendingTriggerEventProvider` and
  `PendingTriggerEventProvider`, with a comment on the survivor explaining why trigger-node scoping is
  mandatory;
- removed the dead stub from five unit-test doubles;
- **retargeted** `PendingTriggerEventQueueIntegrationTests` (FIFO order + concurrent-dequeue
  atomicity) onto the surviving procedure — that coverage now applies to the one actually used in
  production, which it did not before;
- added `Dequeue_does_not_cross_trigger_nodes`, pinning the invariant: two triggers in one workflow
  sharing a correlation key drain independently.

**Acceptance.** Met — there is now exactly one dequeue path and it is trigger-node scoped.
Cross-ref remediation plan §4.2.

---

# B. Expressions and node feature gaps

## WF-02 🔴 No comparison, function or boolean logic exists

**Status:** ✅ Fixed 2026-08-06 — B1 (evaluation) and B2 (authoring surface) both landed.

**What B1 delivered.** `ExpressionEvaluator` now evaluates the whole tree:

- `BinaryExpression` — all eight operators. `And`/`Or` **short-circuit**, so a guard like
  `isPresent && value > 10` cannot blow up on the missing value.
- `UnaryExpression` — `Not`, `Negate`.
- `FunctionExpression` — all ten `FunctionName` members. `Now`/`UtcNow` take the injected `IClock`
  (the evaluator gained an `IClock` dependency); `Contains` does substring *and* array membership.
- `MemberAccessExpression` — previously unhandled and unmentioned in the original writeup; resolves
  as the branch-state path `{Object}.{Member}`.

**Type rules — deliberately strict, no implicit coercion.** Numbers compare numerically, strings
ordinally; booleans and null support equality only; objects/arrays compare structurally with property
order irrelevant. `Equal`/`NotEqual` across different kinds is simply *not equal*; **ordering** across
different kinds is an *error*, because there is no defensible answer. Notably **`"5"` does not equal
`5`** — payloads carrying numbers as strings must be converted explicitly. Silent coercion was
rejected: it turns a visible type mismatch into a workflow that quietly takes the wrong branch. If
that proves annoying in practice it is a deliberate, reversible decision — revisit it as its own item
rather than sprinkling coercion in ad hoc.

**Failure model.** New `ExpressionEvaluationException : PermanentNodeException`. `PermanentNodeException`
gained a virtual `ErrorCode` (default `"PERMANENT_ERROR"`) which `RetryExecutor` now uses, so a bad
expression becomes a **non-retryable** `Fail("EXPRESSION_EVALUATION_ERROR")` with a message naming the
offending types — never an unhandled crash, and never a pointless retry loop. Invalid JSONPath moved
onto the same path (it used to throw `ArgumentException` → `EXECUTOR_CRASH`).

**What B2 delivered** (owner decision: structured, option (a)).

`LogicGateConfig.Condition` is now a `WorkflowExpression` instead of a `string`, so a Logic node can
express `reading.temperature > 30` directly. `LogicNodeExecutor.ParseCondition` — the four-line
"bool literal or state path" reader that was the whole limitation — is gone; the executor evaluates
the tree.

**Back-compat is handled, not deferred.** `LogicConditionJsonConverter` accepts *either* shape on
read: a structured object, or a legacy bare string mapped exactly the way the old executor read it
("true"/"false" → literal, anything else → branch-state path). Definitions published before this
change keep running with identical behaviour, and are rewritten in structured form the next time they
round-trip — so republishing normalises them. **No data migration is required.**

`WorkflowBuilder.AddLogicGate` gained `WorkflowExpression` overloads; the `string` overloads remain
and route through `FromLegacyString`, so existing callers produce byte-identical definitions.

**Example generator fixed** (part of WF-35). `Tools/Wbskt.Workflow.Exporter` built
`AddLogicGate("event.value > 100", …)`, which was never parsed — it became a branch-state path,
resolved to null, and failed the node at runtime. It now emits a real `BinaryExpression`. Verified by
running the exporter: the generated `BranchingWithLogicAndForEach.json` carries a proper
`{"kind":"binary", …}` condition and passes `BuildAndValidate()`.

**Tests.** 34 new cases in `ExpressionEvaluatorTests` (every operator, short-circuiting, structural
equality, the no-coercion rule, ordering-mismatch errors, all functions, arity checks, clock-driven
`UtcNow`); Logic node comparison cases; and four `ControlNodeTests` cases pinning the converter —
structured form, legacy string → path, legacy `"true"` → literal, and the legacy-to-structured
round-trip.

**Also usable.** `VariableNodeExecutor` already deserialized a `WorkflowExpression` from its `value`
config, so Variable nodes gained comparisons and functions from B1 alone.

---

**This is the single largest gap in the product.** Everything else in this document is secondary.

**Symptom (user).** A user cannot express `if temperature > 30`. The Logic node accepts only:
- the literal strings `"true"` / `"false"`, or
- a dotted path into branch state that must *already* hold a boolean.

Anything else — `"event.value > 100"`, `"$trigger.body.status == 'active'"` — is parsed as a state
path, resolves to `null`, and the node fails with `LOGIC_CONDITION_NOT_BOOL`.

**Root cause.** Two layers, both incomplete:

1. `LogicNodeExecutor.ParseCondition`
   (`Wbskt.Workflow/NodeExecutors/Controls/LogicNodeExecutor.cs:38`) is four lines: `bool.TryParse` →
   `LiteralExpression`, else `BranchStateRefExpression`. **There is no expression parser anywhere in
   the codebase.**
2. `ExpressionEvaluator.EvaluateAsync`
   (`Wbskt.Workflow/Runtime/ExpressionEvaluator.cs:13-20`) handles only `LiteralExpression`,
   `BranchStateRefExpression`, `JsonPathExpression`. `BinaryExpression`, `UnaryExpression` and
   `FunctionExpression` fall into `_ => throw CreateNotImplemented(expr)` — so even a hand-authored
   expression tree in JSON throws `NotImplementedException` at runtime.

The models are all there and unused:
`Wbskt.Workflow.Abstraction/Models/Expressions/{Binary,Unary,Function}Expression.cs`, with
`BinaryOperator` = `Equal, NotEqual, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, And,
Or`; `UnaryOperator` = `Not, Negate`; `FunctionName` = `Now, UtcNow, ToString, ToLower, ToUpper,
Contains, StartsWith, EndsWith, IsNull, IsEmpty`.

**Fix.** Two separable pieces — do them in this order.

**B1. Evaluate the expression tree** (`ExpressionEvaluator`):
- `BinaryExpression`: evaluate both sides, then compare. Define and document the coercion rules
  explicitly — numeric comparison when both sides are numbers, ordinal string comparison when both are
  strings, `Equal`/`NotEqual` structural for objects/arrays, and a typed failure (not an exception)
  on mismatched kinds. `And`/`Or` short-circuit and require boolean operands.
- `UnaryExpression`: `Not` on a boolean, `Negate` on a number.
- `FunctionExpression`: implement all ten `FunctionName` members. `Now`/`UtcNow` take `IClock` (inject
  it — the evaluator is currently a dependency-free singleton).
- A malformed expression should return a *typed evaluation error* the caller turns into
  `NodeExecutionResult.Fail`, never an escaping `NotImplementedException`.

**B2. Give authors a way to write one.** The Logic node's `Condition` is a `string`. Choose one:
- **(a) Structured** — change `LogicGateConfig.Condition` to a `WorkflowExpression` and let the UI
  build the tree. Cleanest; requires a definition-model migration for any already-published
  workflows.
- **(b) Parsed** — keep the string and write a small recursive-descent parser for a documented
  mini-grammar (`$trigger.x`, `$local.y`, `$shared.z`, literals, `== != > >= < <= && || !`,
  `contains()`, `isNull()`). More work, but authors can type a condition, and the same parser serves
  `TemplateExpression` (WF-06) and trigger filters (WF-15).

Recommendation: **(a) for the model + (b) as a convenience parser producing (a)**, so the wire format
is unambiguous and the UI can round-trip it.

**Tests.** `ExpressionEvaluatorTests`: every operator, every function, both coercion paths, mismatched
types → typed error not throw. `LogicNodeExecutorTests`: numeric comparison routes `true`/`false`
ports correctly. E2E: a workflow that branches on trigger payload data.

**Acceptance.** A user can publish a workflow whose Logic node compares a trigger field to a constant,
and both ports are reachable.

---

## WF-06 🟠 `$shared` refs and template interpolation throw at runtime

**Status:** ✅ Fixed 2026-08-06.

**What landed.**
- `SharedVariableRefExpression` reads through `ISharedVariableProvider`. A variable that was never
  written resolves to **JSON null** rather than failing — the same as a missing branch-state path — so
  a workflow can test `isNull($shared.counter)` before initialising it. A variable holding invalid
  JSON is a clean evaluation error.
- `TemplateExpression` interpolates `{{ … }}` placeholders. A placeholder holds either
  `$shared.NAME` or a branch-state path (same resolution as `branchStateRef`, so it falls back to the
  trigger payload). Missing values render as empty; an unclosed or empty placeholder is a clean
  evaluation error; the result is always a JSON string.

**⚠ Correction to the prescribed approach.** This section said to inject `IServiceScopeFactory`
because "the evaluator is a singleton without provider access". That was unnecessary — **all four**
`IExpressionEvaluator` consumers (the Logic, Variable, ForEach and ParallelForEach executors) are
themselves Scoped. The evaluator was simply re-registered `Singleton` → `Scoped` and takes
`ISharedVariableProvider` directly. No scope factory, no manual scope management. The DI lifetime test
now pins this (same instance within a scope, different across scopes).

The provider constructor parameter is optional so the many hand-built test evaluators need no stub;
without one, a `$shared` reference fails with a clear message rather than silently reading as null.

**Tests.** 8 new cases including the design doc's shared-counter comparison
(`$shared.counter >= 5`), which could not run at all before.

**Symptom (user).** Any expression referencing a shared variable, or any templated string, throws
`NotImplementedException` when the node executes. The shared-counter scenario the design leads with
cannot run.

**Root cause.** `Wbskt.Workflow/Runtime/ExpressionEvaluator.cs:18` —
`SharedVariableRefExpression or TemplateExpression => throw CreateNotImplemented(expr)`.

**Fix.** Per remediation plan §3.1:
- The evaluator is a singleton with no provider access. Inject `IServiceScopeFactory`; on
  `SharedVariableRefExpression`, open a scope, resolve `ISharedVariableProvider`, call
  `GetByWorkflowRefIdNameAsync(branch.WorkflowDefinitionRefId, name, ct)`, parse `ValueJson` into a
  `JsonElement`. A missing variable returns JSON `null` (not a throw) — consistent with how `$trigger`
  misses already behave.
- `TemplateExpression`: interpolate sub-expressions into the template string (see the model for its
  parts shape) and return a JSON string element.

**Depends on** WF-02's B1 work landing first if you want templates to contain comparisons.

**Tests.** `ExpressionEvaluatorTests` — `$shared` read hits the provider; missing var → null; template
mixing literals and refs.

**Acceptance.** A workflow can read a shared counter into branch state and interpolate it into a
webhook body.

---

## WF-04 🔴 `ForEach` is parallel, not sequential, and fires `done` too early

**Status:** ✅ Fixed 2026-08-06 (engine + builder). ⚠ Two E2E tests were updated but **could not be
run here** — see the verification note below.

**What landed.**
- **`RemoveKeys` plumbing.** `NodeExecutionResult.Continue` gained an optional
  `IReadOnlyCollection<string>? RemoveKeys`, honoured by `BranchLoop.MergeLocalState` (removals apply
  before the patch, so a node can drop and re-add a key in one step). This is what lets a revisitable
  node clear its own bookkeeping; **WF-05 depends on it**.
- **Sequential `ForEach`.** The executor now hands out one item per visit, tracking progress under
  `__foreach:{nodeId:N}:index` — keyed by node id so nested loops keep separate counters. When the
  collection is exhausted it leaves via `done` **and drops the iterator**, so a re-entry (from an
  outer loop) starts over. `ParallelForEachNodeExecutor` is untouched and remains the fan-out node.
- **Builder closes the loop.** `ForEachScope` now exposes `BodyTail`, and
  `AddForEach(collection, scope)` wires that tail back to the ForEach node's `in` port. **Without
  this back-edge the builder produced a straight line, not a loop** — the body would have run once.

**⚠ Knock-on found while doing this: the builder's `AddParallelForEach(collection, scope)` helper
never added a Join**, so it produced definitions that **WF-03's new `PFE_NO_JOIN` check rejects**. A
PFE cohort has nowhere to converge without a Join, so the helper now adds one (default
`JoinMode.All`) and continues from its `default` port. Note the helper can only do this when the body
leaves a tail — a body ending in `AddFork(names, scope)` **without** a join mode leaves none, so such
a definition is still rejected and the inner fork needs its own join mode. This is worth keeping in
mind for **WF-19 item 3**, which turns the same condition into a publish-time error.

**Tests.** `ForEachNodeExecutorTests` rewritten for sequential semantics (first item, full walk in
order then `done` with the iterator dropped, empty collection, per-node iterator isolation).
`NodeExecutorsE2ETests` now drives a real loop through `BranchLoop` with a back-edge and asserts the
loop node is entered once per item plus once for `done`, the body once per item, items in order, no
branches forked, and the iterator cleared at the end.

**⚠ Verification gap.** `ForEachFanOutE2ETests` was updated (it asserted fan-out: N branches each
binding a distinct item; a sequential loop produces **one** branch). `NestedLoopE2ETests` builds a
ParallelForEach whose body is a `AddFork(...)` with no join mode — **that definition is invalid under
WF-03 regardless of WF-04**, and its expected command count (31) was derived from the old fan-out
arithmetic. **Neither test could be executed here** — the E2E suite needs live hosts and currently
fails at fixture setup with HTTP 429 from the auth host. Both need a run against live hosts before
being trusted; `NestedLoopE2ETests` in particular likely needs its fork given a join mode and its
count re-derived.

**Symptom (user).** A user picks "ForEach" (expecting one-at-a-time) over "ParallelForEach". They get
identical fan-out behaviour — every item spawns a branch at once — **plus** the `done` port fires
immediately, before any body branch has executed. Anything wired to `done` runs against incomplete
state.

**Root cause.** `Wbskt.Workflow/NodeExecutors/Controls/ForEachNodeExecutor.cs` builds one `ForkSpec`
per item on the `body` port and returns
`new NodeExecutionResult.Fork(children, continueOutboundPort: "done", …)` — the fork's continue-port
is taken by the *parent* branch immediately. It is functionally `ParallelForEachNodeExecutor` minus the
join bookkeeping.

**Fix.** Per remediation plan §3.3 — rewrite as sequential iteration:

- **Prerequisite plumbing:** `BranchLoop.MergeLocalState` can only add/overwrite keys. Add
  `IReadOnlyCollection<string>? RemoveKeys` to `NodeExecutionResult.Continue` (default `null`) and
  have `MergeLocalState` honour it.
- Iterator key: `__foreach:{ctx.Node.NodeId:N}:index` in local state.
- Each visit: evaluate the collection, read the index (default 0). If `index < count` →
  `Continue("body", patch: { item = items[index], index, iteratorKey = index + 1 })`. Else →
  `Continue("done", RemoveKeys: [iteratorKey])`.
- The graph back-edge (body tail → ForEach `in`) already works — cycles are allowed and
  `ResolveNextNodeId` follows edges normally.
- Check `Wbskt.Workflow.Builder/WorkflowBuilder.cs` `AddForEach` — it must wire the body tail back to
  the ForEach node, not linearly to `done`.
- **`ParallelForEachNodeExecutor` is unchanged** — it remains the fan-out node.

**Tests.** Rewrite `ForEachNodeExecutorTests` for sequential semantics; update `ForEachFanOutE2ETests`
/ `NestedLoopE2ETests` / `WorkflowCommandLoopTests` (items processed in order, `done` after the last).

**Acceptance.** `ForEach` processes items strictly one at a time; `done` fires only after the final
item's body completes.

---

## WF-05 🟠 A `Delay` inside a loop waits once, then is skipped forever

**Status:** ✅ Fixed 2026-08-06.

`DelayNodeExecutor` now returns `RemoveKeys: [__delay_until]` on the resume `Continue`, so the
deadline is cleared on the way out and the next lap re-arms. Depended on WF-04's `RemoveKeys`
plumbing. Regression test drives park → fire → continue → park again and asserts the second park has a
fresh deadline computed from the current clock.

**Symptom (user).** A poll loop (`ForEach` or a Logic back-edge) containing a 5-minute Delay waits
5 minutes on the first pass and zero on every subsequent pass — the loop spins.

**Root cause.** `DelayNodeExecutor` writes `__delay_until` into local state on first visit and, on the
resume visit, returns `Continue()` **without removing the key**
(`Wbskt.Workflow/NodeExecutors/Controls/DelayNodeExecutor.cs`, `DelayUntilKey`). A later visit sees a
past deadline and continues immediately.

**Fix.** Depends on WF-04's `RemoveKeys` plumbing. On the resume-visit `Continue`, pass
`RemoveKeys: [DelayUntilKey]` so the next visit re-arms.

**Tests.** Unit: Delay → loop back → Delay parks again with a fresh deadline.

**Acceptance.** A Delay inside a loop waits its full duration on every iteration.

---

## WF-07 🟠 Variable node supports only `Set`

**Status:** ✅ Fixed — `Increment`/`Decrement` and `Set` landed 2026-08-06; `CompareAndSet` completed
2026-08-07.

**CompareAndSet.** `VariableConfig.Expected` (a literal or an expression, like `Value`) closed the
blocker. Shared scope calls the existing `SharedVariable_CompareAndSet`; local scope compares against
branch state. Both compare **serialized JSON text**, because that is exactly what the SQL does
(`ValueJson = @Expected`) — matching it stops local quietly accepting a structural match the shared
path would reject.

**Losing the race is a normal outcome, not a node failure.** Somebody else won, which is the situation
the operation exists to detect; failing the node would make the optimistic-concurrency retry pattern
impossible to express. The result lands in branch state as `casSucceeded`, so a Logic gate can branch
on it and loop back — the same convention as `childResult`. A `CompareAndSet` with no `expected` at all
*is* rejected (`VARIABLE_EXPECTED_MISSING`): writing unconditionally would silently degrade it to a
`Set`, which is the exact race it exists to avoid.

**What landed.**
- **`Increment`/`Decrement`, shared scope** — call the atomic `SharedVariable_Increment`/`_Decrement`
  procedures. No read-modify-write, so no retry loop and no lost updates; the test asserts nothing is
  read first. A counter that does not exist yet is seeded via `InitializeAsync(…, "Counter", delta)`.
- **`Increment`/`Decrement`, local scope** — numeric add against branch state, missing value treated
  as zero.
- **Step value** — defaults to 1, so a bare Increment node needs no config; a configured value is
  honoured and may itself be an expression. A non-numeric step fails with `VARIABLE_STEP_NOT_NUMERIC`.
- **`Set` on shared scope is now last-writer-wins** — a plain `SetAsync`, replacing the three-attempt
  CAS loop that could fail the node outright with `SHARED_VAR_CAS_FAILED` under contention. That error
  code no longer exists. Initialize-if-missing is retained because `SharedVariable_Set` is UPDATE-only.

**Provider change.** `IncrementAsync`/`DecrementAsync`/`SetAsync` now signal "no such row" with
`KeyNotFoundException` (by passing `null` for `exceptionIfNotFound`) instead of a generic
`InvalidOperationException`, matching the convention `GetByWorkflowRefIdNameAsync` already used. That
is what lets the executor branch on it precisely rather than catching a broad exception type.

**⚠ `CompareAndSet` cannot be implemented as scoped.** It needs an *expected* value to compare
against, and `VariableConfig` has no field for one (it carries only `Scope`, `Op`, `Var`, `Value`).
The provider primitive and `SharedVariable_CompareAndSet.sql` both exist and are retained. The node
currently returns `VARIABLE_OPERATION_NOT_SUPPORTED` with that explanation. **To finish this: add an
`expected` field to `VariableConfig`, then wire the existing provider call.**

**Tests.** 11 cases covering both scopes, both directions, the default and configured step, the
seed-on-missing paths, the non-numeric step failure, and the CompareAndSet rejection.

**Symptom (user).** Selecting `Increment`, `Decrement` or `CompareAndSet` on a Variable node fails at
runtime with `VARIABLE_OPERATION_NOT_SUPPORTED`. Separately, a shared `Set` under concurrency can fail
outright with `SHARED_VAR_CAS_FAILED`.

**Root cause.** `Wbskt.Workflow/NodeExecutors/Controls/VariableNodeExecutor.cs:34` rejects every op
except `Set`. The shared `Set` path then uses a 3-attempt compare-and-set loop
(`MaxCompareAndSetAttempts`) — **inverted** relative to the design, where `Set` is last-writer-wins and
counters use atomic SPs.

The database side is already done and unused: `SharedVariable_Increment.sql`,
`SharedVariable_Decrement.sql`, `SharedVariable_Set.sql` all exist, and
`SharedVariableProvider.IncrementAsync` / `DecrementAsync` already wrap them
(`Wbskt.Workflow/Providers/SharedVariableProvider.cs:63,79`).

**Fix.** Per remediation plan §3.2:
- `Increment`/`Decrement` + `Scope == Shared` → call `IncrementAsync`/`DecrementAsync`. If the SP
  returns no row (variable not initialized), `InitializeAsync(refId, var, "Counter", delta)` then
  continue.
- `Increment`/`Decrement` + `Scope == Local` → read the current value from `ctx.Branch.LocalState`,
  numeric add, emit a patch.
- `Set` + `Scope == Shared` → replace the CAS loop with `SetAsync`, keeping the initialize-if-missing
  fallback (`SharedVariable_Set` is UPDATE-only, so if the post-select returns no row, Initialize).
- Keep `CompareAndSetAsync` on the provider for a future explicit CAS op, but stop using it here.

**Tests.** `VariableNodeExecutorTests` — increment shared/local, decrement, set overwrite,
uninitialized-variable bootstrap.

**Acceptance.** All four `VariableOperation` values work in both scopes, and a shared `Set` never
fails under contention.

---

## WF-08 🟠 `action:email` and `action:telegram` are unimplemented stubs

**Status:** ✅ Fixed 2026-08-07. **`NodeKind.NotYetImplemented` is now empty** — every kind the model
can express, the engine can run.

**Credentials are host configuration, never node config.** `EmailOptions` (`WorkflowEngine:Email`) and
`TelegramOptions` (`WorkflowEngine:Telegram`) carry the SMTP password and bot token. This is the load-
bearing decision: a definition is stored, versioned and readable by anyone with `workflows.read`, so a
token written into one leaks to every workspace member *and* is frozen into every published version,
where deleting it later does nothing. The existing `EmailConfig`/`TelegramConfig` already carried only
addressing and content, so nothing had to be taken away.

- **Email** goes through a new `IEmailSender` (SMTP has no injectable seam of its own, so this is what
  makes the node testable and lets a provider API be swapped in later). Bodies send as **plain text** —
  the body is author-controlled and sanitised nowhere, so rendering it as HTML in a recipient's client
  would hand them author-controlled markup. An unconfigured relay fails non-retryably with
  `EMAIL_NOT_CONFIGURED`; a malformed address is permanent; everything else is retryable, because SMTP
  is routinely transient.
- **Telegram** posts to the Bot API on its own named `HttpClient`, so its timeout and handler lifetime
  are not shared with arbitrary author-controlled webhook targets. No `IOutboundAddressGuard` — the
  target is the configured API base, not an author-supplied URL, so there is no SSRF surface. 4xx is
  permanent except **429**, which is exactly what retries are for. **The API response body is never
  echoed into the failure message**: it can quote the request URL, and the request URL carries the bot
  token. A test pins that.

`WorkflowBuilder` gained `AddEmail` and `AddTelegram`, closing the last builder gap.

**Acceptance.** Met — the nodes work.

**Symptom (user).** Both node kinds publish successfully and then fail the run at execution with
`EXECUTOR_CRASH: … not yet implemented - Phase 9 TODO`.

**Root cause.** `Wbskt.Workflow/NodeExecutors/Actions/EmailNodeExecutor.cs:14` and
`TelegramNodeExecutor.cs:14` both `throw new NotImplementedException`. Unlike WF-01, these throw
*inside* `BranchLoop`'s try, so they degrade to a clean node failure — bad UX, not a leak.

**Fix.** Either implement them, or gate them at publish time via the WF-19 item 1 validator rule so an
author gets an error rather than a broken run. `EmailConfig` and `TelegramConfig` already define the
shapes. Note `WorkflowBuilder` has no `AddEmail`/`AddTelegram`/`AddToast` — add them when the
executors land.

**Acceptance.** Either the nodes work, or they cannot be published.

---

## WF-09 🟠 Sub-workflow cannot receive input from its parent

**Status:** ✅ Fixed 2026-08-07.

`SubWorkflowConfig.Input` is an `IReadOnlyDictionary<string, WorkflowExpression>`, each entry evaluated
against the *parent* branch and merged into the child's `$trigger.body` alongside the engine's own
`parentRunRefId`/`correlationKey`. The return path already worked. `WorkflowBuilder.AddSubWorkflow`
takes the map.

**`parentRunRefId` and `correlationKey` are reserved.** `parentRunRefId` is how the child's completion
hook finds its way back to the parent; an input of the same name would overwrite it and strand the
parent on its bookmark forever. Rejected at publish (`SUBWORKFLOW_INPUT_KEY_RESERVED`, listed on
`SubWorkflowConfig.ReservedInputKeys` so the validator and the executor cannot disagree) **and** at
runtime, so a definition published before the rule existed cannot do it either.

**An input that cannot be evaluated fails the node without starting the child**, non-retryably —
starting it with a silently incomplete payload would have it run against missing data with nothing to
explain why, and a bad expression yields the same result on every retry.

**Tests.** Input evaluated into the child body with the engine's keys intact; unevaluatable input fails
and the hub is never called; a reserved key is refused; plus two validator cases.

**Symptom (user).** A user factors shared logic into a child workflow and finds no way to pass data
into it. The child always receives the same fixed payload.

**Root cause.** `SubWorkflowConfig` carries only `WorkflowRefId` and `CorrelationKey`
(`Wbskt.Workflow.Abstraction/Models/Nodes/Controls/SubWorkflowConfig.cs`).
`SubWorkflowNodeExecutor` hardcodes the child's trigger body to
`{ parentRunRefId, correlationKey }` (`Wbskt.Workflow/NodeExecutors/Controls/SubWorkflowNodeExecutor.cs`).

**Fix.** Add an `Input` map to `SubWorkflowConfig` — `IReadOnlyDictionary<string, WorkflowExpression>`
— evaluate each entry against the parent branch context, and merge the results into the synthetic
manual event's `body` alongside `parentRunRefId`. The return path already works (the child's result is
promoted to `childResult` on resume).

**Depends on** WF-02's B1 if the inputs should be expressions rather than literals.

**Tests.** `SubWorkflowNodeExecutorTests`: input map is evaluated and appears in the child's
`$trigger`. E2E: parent passes a value, child echoes it back via `childResult`.

**Acceptance.** A parent can pass arbitrary evaluated data to a child run.

---

## WF-10 🟠 No fan-out concurrency cap

**Status:** ◐ Partial — the **ceiling** landed 2026-08-07 and meets the acceptance criterion. **Windowed
`MaxConcurrency` was deliberately not built**; the reasoning and a design sketch are below, because it
is a real feature and someone will want it.

**What landed.** `WorkflowEngineOptions.MaxFanOut` (default 1000). `ParallelForEachNodeExecutor` checks
the evaluated collection *before* writing anything and fails with `PFE_FAN_OUT_TOO_LARGE`, naming both
the actual count and the ceiling. Nothing is written on that path — no cohort to clean up, no branches
to reap. `ForEach` is sequential since WF-04 and `Fork`'s children come from an author-written list, so
`ParallelForEach` is the only unbounded fan-out left.

**Why the window was not built.** It needs the pending items and the cohort's body-entry node stored on
the aggregator row (three new columns), an atomic pop inside `JoinAggregator_Contribute`, and a
branch-spawn on both contribution paths — the success path in `JoinNodeExecutor` and the failure path
in `BranchLoop`. That is surgery on the exact code WF-03 just fixed, and **it cannot be verified here**:
the DB integration tests no-op without a live SQL Server, so a mistake in the popping procedure would
ship as "cohorts hang", which is the P0 WF-03 existed to remove. The ceiling removes the denial of
service; the window is a fairness optimisation, and it should be built where its SQL can be run.

**Sketch, for whoever picks it up.** `JoinAggregators` gains `PendingItemsJson NVARCHAR(MAX) NULL`,
`MaxConcurrency INT NULL` and `BodyNodeId UNIQUEIDENTIFIER NULL` (so the spawn need not re-walk the
graph). `ParallelForEachNodeExecutor` forks only the first `MaxConcurrency` items and stores the rest.
`JoinAggregator_Contribute`, in the same transaction as the count, pops the head of the pending array
and returns it; both callers spawn a branch for it with `{item, __join_token, __join_index}`, remembering
to increment `ActiveBranchCount` *before* the contributing branch decrements its own. `ExpectedCount`
stays the full item count, so quorum arithmetic is unchanged. **Do not add `MaxConcurrency` to
`ParallelForEachConfig` until it is honoured** — a config field that is silently ignored is worse than
an absent one.

**Symptom (user).** A `ParallelForEach` (or today's `ForEach`) over a 10,000-element collection creates
10,000 branch rows and 10,000 dispatcher entries in one tick, saturating the engine for every tenant.

**Root cause.** `ForEachConfig` and `ParallelForEachConfig` each contain exactly one property,
`Collection`. `BranchLoop`'s `Fork` handling creates every child unconditionally.

**Fix.** Add `MaxConcurrency int?` to `ParallelForEachConfig` and honour it — the simplest correct
implementation is a windowed fan-out: create the first N children, and have the Join aggregator (or a
new counter on the aggregator row) release the next item as each completes. Independently, add a
hard `MaxFanOut` ceiling in `WorkflowEngineOptions` that fails the node with a clear error rather
than accepting an unbounded collection.

**Acceptance.** A fan-out over a large collection is bounded, and exceeding the ceiling produces a
descriptive node failure rather than engine saturation.

---

# C. Authoring and lifecycle API gaps

## WF-11 🟠 Normal outcomes on "start run" return HTTP 500

**Status:** ✅ Fixed 2026-08-06.

`WorkflowEngineClient` no longer throws when a run isn't started — it reports a `StartRunOutcome`
(`Started`, `Duplicate`, `Queued`, `Dropped`, `NoManualTrigger`) on `StartRunResponse`, and the
controller maps each to its own status:

| Outcome | HTTP | Body / code |
|---|---|---|
| `Started` | 200 | the run |
| `Duplicate` (idempotency-key retry) | 200 | the **original** run, not `Guid.Empty` |
| `Queued` | 202 | the request is held behind an active run |
| `Dropped` | 409 | `RUN_DROPPED_BY_CONCURRENCY_POLICY` |
| `NoManualTrigger` | 409 | `WORKFLOW_HAS_NO_MANUAL_TRIGGER` |
| deprecated workflow | 409 | `WORKFLOW_DEPRECATED` |

The deprecated check is explicit in the controller: `GetCurrentByRefIdAsync` returns the latest
version **regardless of `IsEnabled`**, so a deprecated workflow resolves fine but its triggers were
deregistered — the caller would otherwise get an opaque failure. Only a genuine transport/engine fault
now produces a 5xx.

**Tests.** Client-level theory over every engine outcome, plus the started-run case.

**Symptom (user).** `POST /api/workspaces/{ws}/workflows/{ref}/runs` returns **500
`ENGINE_START_ERROR`** for three entirely normal situations:
- the workflow has no manual trigger,
- the concurrency policy `Dropped` the request,
- the concurrency policy `Queued` the request.

A fourth case returns `200 OK` with `RunRefId = Guid.Empty, RunId = 0` — an idempotent duplicate,
which the caller cannot distinguish from a real run.

**Root cause.** `Hosts/Wbskt.Management.Host/Services/Clients/WorkflowEngineClient.cs:35-38` throws
`InvalidOperationException("Manual trigger did not start a run.")` whenever `RunRefId` is null,
collapsing all three outcomes. `WorkflowsController.StartManualRun` catches `Exception` and maps to
`Error.Failure(…)` → 500 (`Hosts/Wbskt.Management.Host/Controllers/Workflow/WorkflowsController.cs:146-151`).
Line 30-33 of the client is the `Idempotent` → empty-guid case.

Related: `GetCurrentByRefIdAsync` resolves via `WorkflowDefinition_GetLatestVersion_By_RefId`, which
does **not** filter on `IsEnabled`. So a *deprecated* workflow passes the controller's ownership check
at line 134, reaches the engine, finds no registration (they were deleted on deprecate), and produces
the same 500.

**Fix.** Map the engine's `TriggerDispatchOutcome` to distinct results:

| Engine outcome | HTTP | Body |
|---|---|---|
| `StartedRun` | 200 | `StartRunResponse` |
| `Queued` | 202 | correlation key + explanation |
| `Dropped` | 409 | `RUN_DROPPED_BY_CONCURRENCY_POLICY` |
| `Idempotent` | 200 | the *existing* run's ref, not `Guid.Empty` — look it up |
| `NoRegistration` | 409 | `WORKFLOW_HAS_NO_MANUAL_TRIGGER` |

Add an explicit deprecated check in the controller before relaying: if `row.IsEnabled == false`,
return 409 `WORKFLOW_DEPRECATED`. Widen `StartRunResponse` (or add a sibling type) to carry the
outcome.

**Tests.** Controller tests for each outcome; a test that starting a run on a deprecated workflow
returns 409, not 500.

**Acceptance.** No normal user action produces a 5xx from this endpoint.

---

## WF-12 🟠 No validate / dry-run endpoint

**Status:** ✅ Fixed 2026-08-06.

`POST /api/workspaces/{workspaceRef}/workflows/validate` (permission `workflows.create`, same as
publishing — it is an authoring operation and it reveals which rules a definition breaks). Returns
`WorkflowValidationResponse` with `IsValid` and **every** issue, warnings included, each carrying
severity, code, message and the offending `nodeId`.

An invalid definition is a **200 with `IsValid: false`**, not an HTTP error — the request was answered
successfully; the answer is "here is what's wrong".

**Publish messages improved alongside.** They previously joined bare sentences; each error is now
`[CODE] message (node <id>)`, so a publish failure points at what to fix. The fully structured form
comes from the validate endpoint.

**Symptom (user).** The only way to find out whether a definition is valid is to publish it — which
creates an immutable version and re-registers triggers. A builder UI cannot show live validation, and
an author cannot check a draft.

**Root cause.** `WorkflowValidator` is invoked only inside
`WorkflowDefinitionService.PublishAsync` (`Hosts/Wbskt.Management.Host/Services/Workflow/WorkflowDefinitionService.cs:47`).

**Fix.** Add `POST /api/workspaces/{workspaceRef}/workflows/validate` under `Permissions.WorkflowsCreate`
that runs the validator and returns the full `ValidationResult` — **all issues, both errors and
warnings, structured** (code, severity, message, nodeId). Note the publish path currently discards
warnings and flattens errors into a single joined string (line 50-54); the new endpoint should return
the structured list, and publish should return it too rather than a concatenated message.

**Tests.** Controller test: invalid definition returns 200 with a populated issue list (validation is
the *result*, not an error); valid definition returns an empty list.

**Acceptance.** A UI can validate a draft without publishing, and publish failures name the offending
node.

---

## WF-13 🟠 Deprecate is one-way; no pause/resume, rename, rollback, or delete

**Status:** ◐ Partial — reinstate and rollback landed 2026-08-06. Rename and delete were **decided
against**, deliberately; see below.

**`POST {refId}/reinstate`** (permission `workflows.delete`, same as deprecating — it is the inverse
of that operation). Deprecating **deregisters the triggers**, so flipping `IsEnabled` back is not
enough: the workflow would read as published and never fire. Reinstate re-registers them, which also
re-seeds schedules from their cron. Registration happens *before* enabling, so a failure leaves the
row still disabled — the state the caller already had — and if enabling then fails the registrations
are undone, because registrations without an enabled definition would fire a workflow the operator
believes is switched off.

**`POST {refId}/rollback/{version}`** (permission `workflows.create`) republishes an earlier version's
definition **as a new version**. The old row is untouched, so history stays append-only and the runs
of every version keep pointing at the definition they actually ran. It goes through `PublishAsync`,
so a rollback is validated, versioned, registered and compensated on failure exactly like any other
publish — which matters, because **a definition published before a validation rule existed may no
longer be valid**, and rolling back to it should fail loudly rather than reinstate a broken workflow.

**Decisions taken (not gaps):**
- **No separate enable/disable axis.** `deprecate` already *is* disable; adding a parallel pair would
  have created two overlapping notions of "off". Reinstate simply makes deprecate reversible.
- **No rename.** Name and description live in the versioned definition. Editing them in place would
  make a published version mutable, which the whole model is built to avoid — publish a new version.
- **No delete.** Runs reference `WorkflowDefinitions.Id` by foreign key; deleting a definition would
  orphan its history. (`DeleteUnreferencedAsync` exists solely as publish compensation and refuses any
  row a run references.)

**Symptom (user).** The only lifecycle verb is `POST {refId}/deprecate`. There is no way to:
- **re-enable** a deprecated workflow (must republish),
- **pause** a workflow temporarily (deprecate is the only lever, and it deletes trigger registrations
  and scheduled fires),
- **rename** or edit the description without publishing a whole new version,
- **roll back** to version N,
- **delete** a workflow or a version.

**Root cause.** `WorkflowsController` exposes exactly five verbs. `WorkflowDefinition_UpdateIsEnabled`
takes a `BIT` but only `DeprecateAsync` calls it, always with 0
(`Wbskt.Workflow/Providers/WorkflowDefinitionProvider.cs`). The SP is capable of both directions; the
service is not.

**Fix.** Decide the intended lifecycle model first, then implement. A minimal coherent set:

- `POST {refId}/enable` / `POST {refId}/disable` — flips `IsEnabled` **and** correspondingly calls
  `ITriggerRegistrationService.OnDeprecatedAsync` / `OnPublishedAsync`, so pausing genuinely stops
  triggers and resuming re-registers them (including re-seeding `ScheduledFires` from the cron).
  Permission: `workflows.execute` (operating state) or `workflows.create` — pick and document.
- `POST {refId}/rollback/{version}` — republishes an existing version's `DefinitionJson` as a new
  version. Keeps the append-only model intact, which is the right call.
- **Rename**: either accept that name/description live in the versioned definition (document it), or
  add `PATCH {refId}` updating `Name`/`Description` on the current row only. The former is more
  consistent with the immutable-version design.
- **Delete**: probably shouldn't exist — runs reference `WorkflowDefinitions.Id` by FK. If a purge is
  needed, it must be a cascading admin operation, not a user verb. Document the decision either way.

**Acceptance.** An operator can pause and resume a workflow without republishing, and the documented
lifecycle matches the available verbs.

---

## WF-14 🟠 No workspace-wide run list

**Status:** ✅ Fixed 2026-08-06.

`GET /api/workspaces/{workspaceRef}/runs` (permission `workflows.read`), backed by
`Run_ListBy_WorkspaceId.sql`. Runs carry no workspace of their own — it lives on the definition — so
the procedure joins through `WorkflowDefinitionId`, which WF-23's `IX_Runs_WorkflowDefinitionId_Id`
covers. Because the procedure scopes by workspace itself there is no per-workflow ownership check to
repeat in the service, and a test asserts none is attempted.

**Cursor bug fixed at the same time.** Both list paths now fetch `top + 1` and use the extra row's
presence to decide whether to return a cursor. The old rule — "a full page means there's more" —
returned a cursor when the results landed exactly on the page boundary, so **every client fetched one
empty page at the end of every listing**. Shared `BuildPage` helper; regression test pins the
exact-boundary case.

**Symptom (user).** Runs are queryable only per-workflow
(`GET workflows/{workflowRefId}/runs`). A workspace "recent activity" or "what's failing right now"
view is impossible without N calls.

**Root cause.** `Run_ListBy_WorkflowRefId.sql` is the only list SP, and
`WorkflowRunQueryService.ListByWorkflowAsync` is the only list path.

**Fix.** New `Run_ListBy_WorkspaceId.sql` joining `Runs → WorkflowDefinitions` on `WorkflowDefinitionId`
filtered by `WorkspaceId`, with the same `@StatusFilter` / `@Top` / `@CursorId` shape. New endpoint
`GET /api/workspaces/{workspaceRef}/runs`. Requires the index from WF-23.

**Also fix while here** — the cursor is subtly wrong in
`WorkflowRunQueryService.ListByWorkflowAsync:45`: `nextCursor = runs.Count == top ? rows.Last().Id : null`
returns a non-null cursor on an exactly-full final page, so clients always fetch one empty page. Use
the `top + 1` fetch-and-trim pattern the history controller already uses correctly
(`WorkflowHistoryController.cs:54-57`).

**Acceptance.** A dashboard can list a workspace's runs across all workflows in one paged call.

---

# D. Triggers

## WF-15 🟠 Trigger filter expressions are plumbed but hardcoded to null

**Status:** ✅ Fixed 2026-08-07.

`Filter` (a `WorkflowExpression`) is now on `ClientTriggerConfig` and `WebhookTriggerConfig`, serialized
onto `TriggerRegistrations.FilterExpression` at publish, and evaluated in
`TriggerDispatcher.FilterPassesAsync` **before** the concurrency enforcer — a filtered event must not be
able to queue, drop or cancel anything, because as far as the trigger is concerned it never arrived.

The filter is copied onto the registration row rather than read from the definition at dispatch time:
the dispatcher matches registrations by key and would otherwise have to load and deserialize a whole
definition per candidate just to decide to discard the event.

**No branch exists yet**, so the evaluation context is a shim over the payload — `$trigger` resolves,
branch-local state is empty, and the ids that only mean something inside a run are zero.

**Fails closed.** A filter that throws, or that yields a non-boolean, is treated as *not matching* and
logged at warning. A gate that cannot be evaluated has not been passed, and starting the run anyway
would defeat the point of configuring one.

**Filtered events are reported, not swallowed.** New `TriggerDispatchOutcome.Filtered` appears in
WF-32's per-registration list, so "I fired the webhook and nothing happened" has an answer.

**Tests.** Filter rejects / filter matches / unparseable filter fails closed, plus registration-service
cases pinning that the expression round-trips structurally onto the row and that a workflow with no
filter still stores null.

**Symptom (user).** Every event matching a trigger key starts a run. There is no way to say "only when
`status == 'active'`" — the user must start a run and immediately end it, burning credits and
polluting run history.

**Root cause.** `Wbskt.Workflow/Runtime/TriggerRegistrationService.cs:93` sets
`FilterExpression = null` — under a nine-line comment that describes precisely what the field is for.
The column exists on `TriggerRegistrations`, the entity carries it, nothing populates or reads it.

**Fix.** Per remediation plan §3.7:
- Add `Filter` (a `WorkflowExpression`) to `ClientTriggerConfig` and `WebhookTriggerConfig`.
- Copy it to the registration row at publish.
- In `TriggerDispatcher.DispatchAsync`, evaluate it against the event payload **before** the
  concurrency check; false → skip that registration and log at debug.

**Depends on** WF-02 (the evaluator must handle comparisons) and needs a payload-backed
`BranchContext` shim since there is no branch yet at dispatch time.

**Acceptance.** A webhook trigger with a filter starts a run only for matching payloads.

---

## WF-16 🟠 Webhook triggers have no secret

**Status:** ✅ Fixed 2026-08-07. **⚠ DACPAC redeploy required** — new
`TriggerRegistrations.WebhookSecret` column.

Optional `Secret` on `WebhookTriggerConfig`, stored on the registration row (nullable
`WebhookSecret NVARCHAR(200)`), presented by the caller in **`X-Wbskt-Secret`**. A registration without
one stays open, so every webhook published before this keeps working unchanged.

**The secret never enters the payload.** The original sketch said to fold it into the relayed body, but
the payload is persisted as the run's trigger data and rendered in its history trace — a credential
written there is readable forever. It travels as a header on the relay and as a non-positional
`InboundEvent.Secret` property in process, so it is never serialized into workflow state.

**Compared in fixed time** (`CryptographicOperations.FixedTimeEquals`), so the check cannot be used to
recover the secret a character at a time.

**No oracle.** The public callback still answers an opaque 202 either way, and the engine's own inbound
response is backend-network-only. A mismatch is recorded as `TriggerDispatchOutcome.SecretMismatch` in
the per-registration list for operators, and logged at warning.

**Checked before the concurrency enforcer**, for the same reason as the filter: an unauthenticated call
must not be able to queue or drop a legitimate caller's run.

**Not done:** the same-workspace duplicate-path question below is still open. Two workflows in one
workspace sharing a path both fire; with WF-32 landed the response now at least *reports* both, so the
behaviour is visible rather than surprising. Whether it should be a validator error is a product
decision, not a defect.

**Tests.** Mismatch, missing-when-required (the upgrade hazard — adding a secret must not keep letting
old callers through), match, and no-secret-means-open; plus controller tests that the header reaches
`InboundEvent.Secret` and never the payload.

**Symptom (user).** Anyone who learns the public callback URL
(`POST api/callbacks/webhook/{workspaceRef}/{path}`) can fire the workflow. The only protection is
URL secrecy, per-IP rate limiting, and the body cap.

**Root cause.** `WebhookTriggerConfig` has `Path`, `Method`, `CorrelationKey`, `ConcurrencyPolicy` —
no secret field.

**Note on scoping:** the workspace-collision half of remediation plan §3.5 **is already fixed** —
trigger keys are `webhook:{workspaceRef}:{path}` (`TriggerRegistrationService.cs:43`) and the
controller/resolver agree. Two workflows in the *same* workspace sharing a path still both fire; decide
whether that is intended fan-out or should be a validator error (WF-19).

**Fix.** Per remediation plan §3.5's secret half:
- Add optional `Secret` to `WebhookTriggerConfig`; store it on the registration row (new nullable
  `WebhookSecret NVARCHAR(200)` column + SP updates).
- `PublicCallbackController` forwards an `X-Wbskt-Secret` header into the relayed payload;
  `TriggerDispatcher` compares after registration lookup (`registration.WebhookSecret == null ||
  registration.WebhookSecret == provided`); mismatch → skip the registration and log.
- Keep the response uniformly 202 either way — do not let the secret check become an oracle.

**Acceptance.** A webhook trigger with a configured secret ignores unsigned calls, and the response is
indistinguishable from a match.

---

## WF-17 🟠 Invalid cron publishes the workflow, then throws

**Status:** ✅ Fixed 2026-08-06 — both halves.

1. **Validation** (WF-19 item 6): `INVALID_CRON` rejects a bad expression before any row is inserted,
   using the same `CronParser` the runtime ticker uses so the two cannot disagree.
2. **Compensation**: everything after the insert now runs inside a try; on failure
   `CompensateFailedPublishAsync` deregisters whatever the failed attempt created, deletes the
   inserted row via the new guarded `WorkflowDefinition_DeleteUnreferencedById`, and **restores the
   superseded version** (re-enable + re-register). This covers *any* post-insert failure, not just
   cron — a transient DB error while registering triggers included.

The delete is deliberately narrow: it only removes a row that **no runs reference**, so published
history can never be deleted. If runs already exist the delete is a no-op and the situation is logged
explicitly. Compensation is fully guarded throughout — the original failure is what the caller sees,
never a secondary error from the rollback.

Restoring the previous version re-registers with the **real** `workspaceRef`, not a placeholder:
webhook trigger keys are workspace-scoped, so anything else would mint keys nothing could match.

**Symptom (user).** Publishing a schedule-triggered workflow with a malformed cron returns an error —
but the definition row is **already inserted**. The workflow now exists, is the current version, has
no scheduled fire, and never runs. Republishing bumps the version again.

**Root cause.** `WorkflowDefinitionService.PublishAsync` inserts the row (line 75) and *then* calls
`_triggerRegistrationService.OnPublishedAsync` (line 96), which throws
`InvalidOperationException($"Schedule trigger … has an invalid cron expression …")` from
`TriggerRegistrationService.CreateScheduleRegistrationAsync`. There is no transaction spanning the
two, and the validator does not check cron.

**Fix.** Two layers:
1. Add a cron validity rule to `WorkflowValidator` (WF-19 item 6) using the same `CronParser` the
   registration service uses, so publish is rejected before any insert.
2. Make the publish path atomic, or at minimum compensating: if `OnPublishedAsync` throws, delete the
   just-inserted row and un-deprecate the previous version before returning the error.

**Acceptance.** A workflow with an invalid cron is rejected at validation, and no partial publish is
possible.

---

# E. Validator

## WF-18 🟠 Duplicate `(node, port)` edges throw at runtime

**Status:** ✅ Fixed 2026-08-06 — validator error `DUPLICATE_PORT_EDGE`, naming the node and port and
pointing at Fork as the way to branch.

**Symptom (user).** Two edges from the same output port. Publish succeeds. At runtime the branch dies
with an unhandled `InvalidOperationException` from LINQ (`Sequence contains more than one element`).

**Root cause.** `BranchLoop.ResolveNextNodeId` uses `SingleOrDefault` over matching edges. Nothing
prevents duplicates at publish. The owner's locked decision (remediation plan, Context §3) is to
**reject at publish**, not to implement multi-edge fan-out.

**Fix.** Validator error `DUPLICATE_PORT_EDGE` when two edges share `(From.NodeId, From.PortId)`.

**Acceptance.** Duplicate outbound edges fail publish with a message naming the node and port.

---

## WF-19 🟠 Five more missing publish-time checks

**Status:** ✅ Fixed 2026-08-06 — all six items landed, plus the optional-`error`-port change.

| Rule | Code | Notes |
|---|---|---|
| 1. Unimplemented / unknown kinds | `NODE_KIND_NOT_IMPLEMENTED`, `UNKNOWN_NODE_KIND` | **Closes WF-01's residual gate.** |
| 2. `ContinueOnError` without an `error` port | `CONTINUE_ON_ERROR_WITHOUT_ERROR_PORT` | `GetExpectedPorts`' extra-port check now tolerates an optional `error` output on **any** `action:*`, not just `action:webhook`. |
| 3. `ParallelForEach` with no downstream `Join` | `PARALLEL_FOREACH_WITHOUT_JOIN` | Reuses `WorkflowGraph.FindDownstream<JoinNode>` from WF-03 — no second graph walk. A `Fork` without a Join is explicitly still legal, and a test pins that. |
| 4. `OnFailure.TargetNodeId` not in the definition | `ONFAILURE_TARGET_NOT_FOUND` | |
| 5. Correlation sanity | `SUSPICIOUS_CORRELATION_KEY`, `CONCURRENCY_POLICY_WITHOUT_CORRELATION_KEY` | Warnings, not errors. |
| 6. Cron validity | `INVALID_CRON` | Via `CronParser.TryParse`, so publish and the runtime ticker agree on which formats are accepted. **This is the validation half of WF-17.** |

**How the kind set stays honest.** `NodeKind` gained `All`, `NotYetImplemented` (currently
`action:email`, `action:telegram`) and `Executable = All - NotYetImplemented`. The validator rejects
anything outside `Executable`. `NodeExecutorRegistryTests.Every_executable_kind_has_a_registered_executor`
builds the real DI container and asserts every executable kind has an executor, and that no executor
is registered for a kind missing from `All` — so the set cannot drift from the engine. **Finishing
WF-08 is now: implement the executors, remove the two entries from `NotYetImplemented`.**

**⚠ Test fixture changed.** `greenhouse-workflow.json` (shared by seven publish-mechanics tests) used
`action:email` twice, so it was correctly rejected by rule 1 — that fixture described a workflow that
would have failed at runtime. Both nodes are now `action:toast`, which is implemented as of WF-01.

**Tests.** 14 new `WorkflowValidatorRuleTests` cases — positive and negative for each rule, both cron
formats, and the Fork-needs-no-Join case.

---

The validator originally had six rules: null definition, shape (`WorkflowRefId`, `Version`), duplicate
node ids, edge endpoints/ports, expected-port set, `WaitForHttp` token strength, plus two warnings
(no triggers, orphan nodes). The `WaitForHttp` token rule is well-reasoned and should stay as the
model for config-level checks.

Missing, each of which currently produces silent runtime breakage:

1. **Unimplemented node kinds** → error. Catches WF-01 and WF-08 at publish. (See WF-01 for how to
   expose the implemented set to `Abstraction` without a DI reference.)
2. **`ContinueOnError` without an `error` port** → error. Today the outcome silently does nothing.
   Note `GetExpectedPorts` only grants `error` to `action:webhook`
   (`WorkflowValidator.cs:184-188`); extend the extra-port check to tolerate an optional `error`
   output on any `action:*`.
3. **`ParallelForEach` with no `Join` in its `body` subgraph** → error. WF-03 landed a runtime
   `PFE_NO_JOIN` failure for this; the validator rule moves the diagnosis to publish time. **Reuse
   `WorkflowGraph.FindDownstream<JoinNode>`** (`Wbskt.Workflow.Abstraction/Models/WorkflowGraph.cs`),
   which WF-03 added for exactly this — do not write a second graph walk. Note a `Fork` without a
   downstream `Join` is *legal* and must not be flagged.
4. **`OnFailure.TargetNodeId` not present in the definition** → error.
5. **Correlation sanity** → warnings: `CorrelationKey` set but neither `$trigger.`-prefixed nor a
   plain constant; `ConcurrencyPolicy != AllowParallel` with a null `CorrelationKey` ("policy will
   behave as AllowParallel").
6. **Cron validity** → error, via `CronParser` (see WF-17).

Optionally: duplicate webhook `path` within one workspace (see WF-16).

**Tests.** One `WorkflowValidatorTests` case per rule, positive and negative.

**Acceptance.** Each listed condition fails publish with a specific error code and node id. Cross-ref
remediation plan §3.6.

---

# F. Database

## WF-21 🟠 `Bookmark_DeleteOrphans` omits `Faulted`

**Status:** ✅ Fixed 2026-08-06 — `N'Faulted'` added to the terminal list, with a comment noting that
every terminal status must be listed.

`Databases/Wbskt.Database/StoredProcedures/Bookmark_DeleteOrphans.sql` deletes bookmarks whose run is
in `('Succeeded','Failed','Cancelled','PartiallyFailed','OutOfCredits')`. **`Faulted` is missing.**
Bookmarks belonging to faulted runs are never collected — the timer scheduler keeps claiming and
attempting to resume branches of a dead run, forever.

**Fix.** Add `N'Faulted'` to the list. One-line change; cross-ref remediation plan §4.2.

**Acceptance.** Orphan GC collects bookmarks for every terminal status.

---

## WF-22 🟠 Publish version authority is split between C# and the SP

**Status:** ✅ Fixed 2026-08-06.

- **The procedure is now the single authority.** `WorkflowDefinition_Publish` stamps the version and
  `isEnabled` into `DefinitionJson` with `JSON_MODIFY`, under the same `HOLDLOCK` that computed the
  version — so the row's `Version` column and the JSON's `version` cannot disagree. The service no
  longer pre-computes anything; it passes `Version = 0` and reads the real value back off the
  inserted row for its response.
- **The bare catch is narrowed.** `catch (Exception) { existing = null; }` became
  `catch (Exception ex) when (IsNotFound(ex))` (`KeyNotFoundException`/`NotFoundException`). This was
  security-relevant: a transient DB error used to masquerade as "no such workflow", which skipped the
  **workspace-ownership check** entirely and let a publish land in a workspace the caller does not
  own.

**Tests.** Three new cases — the version comes from the inserted row (a stale read saying v1 while the
procedure lands on v9 reports 9, and the submitted row carries no pre-computed version); a
`TimeoutException` on lookup fails the publish instead of inserting; and the rollback case shared with
WF-17.

**⚠ Deployment note.** `WorkflowDefinition_Publish` changed and
`WorkflowDefinition_DeleteUnreferencedById` is new — **DACPAC redeploy required**.

**Symptom.** Under concurrent publishes of the same `RefId`, the row's `Version` column and the
`version` property inside `DefinitionJson` can permanently disagree. Anything reading the version out
of the JSON (the definition cache, the engine's run-start path) then sees a different number from the
API.

**Root cause.** `WorkflowDefinition_Publish.sql` computes `@NextVersion` under `HOLDLOCK, UPDLOCK` —
correctly. But `WorkflowDefinitionService.PublishAsync` *also* computes `nextVersion` in C#
(`Hosts/Wbskt.Management.Host/Services/Workflow/WorkflowDefinitionService.cs:67`) from a prior
non-locking read, and bakes it into `DefinitionJson` at line 84.

**Fix.** Per remediation plan §5.3:
- In the SP, before the INSERT:
  `SET @DefinitionJson = JSON_MODIFY(JSON_MODIFY(@DefinitionJson, '$.version', @NextVersion), '$.isEnabled', CAST(1 AS BIT))`.
  Verify the JSON property casing matches `JsonSerializerDefaults.Web` output (`version`, `isEnabled`).
- The service reads the real version from the inserted row for its response DTO instead of trusting
  its own computation.

**While here** — narrow the bare `catch (Exception) { existing = null; }` at line 62 to the provider's
not-found exception (`KeyNotFoundException`), so a transient DB error cannot skip the
workspace-ownership check on line 69. That is a security-relevant catch.

**Acceptance.** Concurrent publishes produce strictly increasing versions with matching row and JSON
values, and a DB blip cannot bypass the ownership check.

---

## WF-23 🟠 Four missing indexes

**Status:** ✅ Fixed 2026-08-06 — five added, one changed.

- `IX_Runs_WorkflowRefId_Id (WorkflowRefId, Id DESC) INCLUDE (Status)` — the run-history page. The
  only existing `Runs` index was filtered to non-terminal statuses, so it could not serve a query
  that is mostly *about* terminal runs.
- `IX_Runs_WorkflowDefinitionId_Id` — supports WF-14's workspace join. SQL Server does not index
  foreign keys automatically.
- `IX_Runs_WorkflowRefId_CorrelationKey_Active` **changed** to include `TriggerNodeId`, so it fully
  covers `Run_GetActiveBy_Correlation`.
- `IX_IdempotencyKeys_CreatedAt` — the hourly GC sweep, on the fastest-growing table in the schema.
- `IX_PendingTriggerEvents_EnqueuedAt` — the TTL sweep; the existing index leads with
  `WorkflowRefId` and cannot serve it.

**⚠ Not measured.** These are reasoned from the query shapes, not from execution plans — the DB
integration tests no-op without a live SQL Server. Worth confirming with real plans at scale.

---


1. **`Runs` — the user-facing run list has no covering index.** `Run_ListBy_WorkflowRefId.sql` does
   `WHERE WorkflowRefId = @x [AND Status = @s] [AND Id < @cursor] ORDER BY Id DESC`. The only index on
   `Runs` is `IX_Runs_WorkflowRefId_CorrelationKey_Active`, filtered to non-terminal statuses — it does
   not serve this query at all. The scan grows with every completed run. **This is the query behind
   the runs page.** Add `IX_Runs_WorkflowRefId_Id ON dbo.Runs (WorkflowRefId, Id DESC) INCLUDE (Status)`
   (or a `(WorkflowRefId, Status, Id DESC)` variant if the status filter dominates).
2. **`IdempotencyKeys` — no `CreatedAt` index.** `IdempotencyKey_DeleteExpired` scans by it hourly, and
   this is the fastest-growing table in the schema (one row per inbound event *plus* one per
   non-side-effect-free action attempt). Add `IX_IdempotencyKeys_CreatedAt`.
3. **`PendingTriggerEvents` — no `EnqueuedAt` index** for `PendingTriggerEvent_DeleteExpired`. The
   existing `IX_PendingTriggerEvents_RunKey` leads with `WorkflowRefId`, so the TTL sweep scans.
4. **`Runs` active index doesn't cover `Run_GetActiveBy_Correlation`**, which filters
   `WorkflowRefId + TriggerNodeId + CorrelationKey`. Extend the filtered index to
   `(WorkflowRefId, TriggerNodeId, CorrelationKey)`.

If WF-14 lands, also add a workspace-scoped path — either an index supporting the
`Runs → WorkflowDefinitions` join, or denormalize `WorkspaceId` onto `Runs`.

**Acceptance.** Each named query has a seek plan against a table with realistic row counts.
Cross-ref remediation plan §4.1.

---

## WF-24 🟡 Unbounded `Warn`/`Error` history growth

**Status:** ✅ Fixed 2026-08-07 — two-window retention. Routine entries go after
`HistoryRetentionWindow` (30 days); `Warn`/`Error` entries are kept until the new
`HistoryRetentionWindowElevated` (default 365 days) — far longer, but no longer forever. They were
previously excluded from collection outright. A null elevated cutoff preserves keep-forever
behaviour for any caller that does not supply one.

---


`HistoryEvent_DeleteForRetiredRuns.sql` deletes events for runs completed before the cutoff **except**
those with `Severity IN ('Warn','Error')` — which are kept forever. On a failure-heavy workspace
`HistoryEvents` grows without bound.

**Fix.** Add a second, much longer retention window for `Warn`/`Error` (e.g.
`HistoryRetentionWindowElevated`, default 1 year) rather than infinite, and delete beyond it.

---

## WF-25 🟡 Three dead stored procedures

**Status:** ✅ Fixed 2026-08-07 — all three deleted.

`WorkflowDefinition_GetAllEnabled` and `ClientCapabilities_GetBy_ClientId` had zero references.
`Run_CountByStatus` was resolved the other way from what this item suggested: WF-26 built purpose-made
stats procedures rather than composing this one, leaving it genuinely dead, so the procedure, its
provider method, its interface member and ~15 test-double implementations were removed. The only
"usage" was a strict-mock setup that asserted nothing.

---


Verified zero references anywhere in the solution:
- `WorkflowDefinition_GetAllEnabled.sql`
- `ClientCapabilities_GetBy_ClientId.sql`
- `Run_CountByStatus.sql` — the provider method `RunProvider.CountByStatusAsync:145` exists, but
  **nothing in production calls it**; only tests do. Note WF-26 wants exactly this shape, so consider
  keeping it and wiring it up rather than deleting.

**Fix.** Delete the first two; decide on the third alongside WF-26.

---

## WF-36 🟡 `INT` primary keys modelled as `long`

**Status:** ☐ Deferred by design — **document only, do not action.** Reviewed 2026-08-07 and the
deferral still holds; what follows is the detail a future migration will want.

`Runs.Id` and `Branches.Id` are `INT` in the database while the code models them as `long`, so every
provider call that takes a run or branch id narrows with `checked((int))`.

**What that actually buys and costs.** The `checked` cast is the useful part: at 2.1 billion runs the
engine will throw `OverflowException` at the boundary rather than silently wrap and write to the wrong
run. So the ceiling is loud, not silent — which is why this is 🟡 and not a correctness bug. The cost
is that the `long` in the signatures is a promise the schema does not keep, and every one of those
casts is a line a reader has to check.

**Why migrating is invasive.** `Runs.Id` and `Branches.Id` are referenced by foreign key from
`Branches`, `Bookmarks`, `HistoryEvents`, `RunCounters`, `JoinAggregators` and `IdempotencyKeys`, and
`HistoryEvents` clusters on `(RunId, HistoryEventId)` — widening it rewrites the largest table in the
schema and every index on it. That is an offline migration on a table sized by retention, not a column
alter.

**When to revisit.** Before the run count is within an order of magnitude of `INT.MaxValue`, or at any
point the schema is being rebuilt for another reason. Until then the honest position is: the types
disagree, the disagreement is contained by `checked`, and it is written down here.

Cross-ref remediation plan §4.4.

---

# G. Observability and analytics

## WF-26 🟠 No user-facing analytics endpoint at all

**Status:** ✅ Fixed — per-workflow stats 2026-08-07, workspace rollup the same day.
**⚠ DACPAC redeploy required** — two new procedures.

**`GET /api/workspaces/{ws}/stats?from=&to=`** (permission `workflows.read`, last 30 days by default),
backed by `Run_GetStatsBy_WorkspaceId` and `Run_GetStatsPerWorkflowBy_WorkspaceId`. Both scope by
workspace themselves — joining through `WorkflowDefinitionId`, as WF-14's list does — so there is no
per-workflow ownership check to repeat and no way for another workspace's workflow to appear.

**What a cross-workflow success rate means — the decision this item was left open on.** The rate is
**run-weighted**: succeeded ÷ finished across every workflow, which is the literal answer to "what
fraction of the work in this workspace succeeded". It is therefore dominated by whichever workflow runs
most, and a busy workflow at 99% *will* hide a quiet one at 0%. Averaging per-workflow rates instead
was rejected: it lets a workflow with two runs count as much as one with two hundred thousand. Both
mislead alone, so the response ships a **per-workflow breakdown** (highest volume first) beside the
headline, which is where the hidden failure is visible — the same summary-plus-detail shape as WF-32.
A test pins exactly that case: a workspace reading 98% overall with a 0% workflow plainly listed.

`SuccessRate` is nullable everywhere, per-workflow and overall, for the reason already established:
`0` reads as "everything failed" rather than "nothing to report".

**Not built:** a time-bucketed series for sparklines. It is a different query shape (grouped by day),
nothing else depends on it, and no caller has asked — worth adding when a dashboard actually needs it.

**`GET /api/workspaces/{ws}/workflows/{refId}/stats?from=&to=`** (permission `workflows.read`,
defaulting to the last 30 days) answers "how is this workflow doing?":

| | |
|---|---|
| **Outcome counts** | per terminal status, plus in-flight |
| **Durations** | p50 / p95 / max / avg, over **completed runs only** — counting in-flight runs as zero would make a busy workflow look fast |
| **Success rate** | succeeded ÷ **finished**. In-flight runs are excluded from the denominator, or an active workflow reads as broken |
| **Top failures** | the error codes that actually occur, by node, with counts and last-seen |
| **Slowest nodes** | per-node avg/max duration and failure count |

Three procedures: `Run_GetStatsBy_WorkflowRefId`, `Run_GetTopFailuresBy_WorkflowRefId`,
`Run_GetNodeTimingsBy_WorkflowRefId`.

**This is where WF-29 pays off.** The slowest-nodes breakdown reads the `durationMs` WF-29 put on
every node outcome, and the failure buckets read `errorCode` from `NodeFailed` payloads. It answers
"which step in *my* workflow is slow" — the question the Prometheus histogram structurally cannot,
because it is tagged by node *kind*, not node id. **That closes most of WF-27's practical value**;
what remains there is workspace tagging on the metrics themselves.

**Deliberate decisions:**
- The window is on `CreatedAt` — "runs *started* in this period". Windowing on completion would make
  a long run appear or vanish depending on when it happened to finish.
- `SuccessRate` is **nullable**. When nothing has finished, `0` would read as "everything failed"
  rather than "nothing to report".
- Every aggregate is read defensively: an empty window aggregates to `NULL`, not `0`, so a workflow
  with no runs reports zeroes instead of throwing.

**⚠ Not built: the workspace-level rollup.** `GET /api/workspaces/{ws}/stats` would need its own
procedures (the joins differ) and a decision about what a cross-workflow "success rate" even means
when workflows have wildly different volumes. Left out rather than guessed at.

**Verification.** These are the most SQL-heavy procedures added in this pass — `PERCENTILE_CONT`,
`JSON_VALUE`, `TRY_CAST`. They were **exercised against a deployed database by the repo owner
(2026-08-07, reported passing)**; they were *not* run in the authoring session, where the integration
fixture could not reach a SQL Server. The service logic is separately unit-tested against mocked
providers.

**Symptom (user).** There is no way to answer "how is this workflow doing?" — no success rate, no
average duration, no failure breakdown, no run counts over time. The only aggregate anywhere is the
Prometheus scrape, which is operator-facing and not workspace-scoped.

**Root cause.** No analytics controller or service exists in `Hosts/Wbskt.Management.Host`. Every
run-related read is a list or a single-item fetch.

**Fix.** The data is already there — `Runs.Status`, `StartedAt`, `CompletedAt`, plus
`HistoryEvents.EventKind/Severity`. A first cut is roughly one SP plus one endpoint:

- `Run_GetStatsBy_WorkflowRefId.sql` — counts by status, p50/p95/max duration
  (`DATEDIFF(ms, StartedAt, CompletedAt)`), and top failure error codes, over a `@FromUtc`/`@ToUtc`
  window.
- `GET /api/workspaces/{ws}/workflows/{refId}/stats?from=&to=` under `workflows.read`.
- A workspace-level rollup (`GET /api/workspaces/{ws}/stats`) for the dashboard, once WF-14's
  workspace-scoped run access exists.
- Optionally a time-bucketed series (runs/day by status) for a sparkline.

`Run_CountByStatus.sql` (WF-25) is the closest existing primitive.

**Acceptance.** A workspace can see per-workflow success rate, duration distribution, and top failure
reasons without scraping Prometheus.

---

## WF-27 🟠 Metrics are not workspace-scoped, and node timings are not per-node

**Status:** ✅ Fixed 2026-08-07 — both halves, though the second is answered differently from how the
item proposed.

**Workspace tagging.** `runs_started_total`, `runs_completed_total` and `credits_consumed_total` now
carry `workspace_id`. Without it these series could only be read as a fleet total — an operator could
not answer "how much is this tenant using" or "is one workspace responsible for the failure spike".
`RunStarter` takes it from the definition row and `RetryExecutor` from the branch context; only
`RunFinalizer` needed a new dependency (the definition cache), and since this only labels a counter, a
lookup failure degrades to `0` rather than derailing finalization.

**Node timings — resolved, but not by adding a `node_id` tag.** One time series per node per workflow
is unbounded cardinality, which is exactly the thing that takes a Prometheus server down. Per-node
timings instead come from the history stream via WF-26's `Run_GetNodeTimingsBy_WorkflowRefId`, which
is scoped to one workflow and one window and so has no cardinality problem at all. **The user-facing
question — "which step in my workflow is slow" — is answered**; the metric deliberately stays coarse
for alerting. This is recorded as a comment on `RecordNodeDuration` so nobody "fixes" it later by
adding the tag.

**Symptom.** Even with the Prometheus data, a tenant cannot see their own numbers, and "which step in
*my* workflow is slow" is unanswerable.

**Root cause.** In `Wbskt.Workflow/Telemetry/WorkflowMetrics.cs`:
- `RecordRunStarted` / `RecordRunCompleted` / `RecordCreditsConsumed` tag only `workflow_ref` —
  **no `workspace_id` anywhere in the file**.
- `RecordNodeDuration` tags `node_kind` + `outcome` — the *kind*, not the node id. Two Logic nodes in
  the same workflow are indistinguishable.

**Fix.** Add a `workspace_ref` tag to the run/credit metrics (`BranchContext.WorkspaceId` is already
threaded through; `RunStarter` and `RunFinalizer` both have the definition row). For node timings,
decide: adding `node_id` as a tag risks Prometheus cardinality blowup, so prefer **recording durations
into the history stream** (WF-29) and computing per-node timings in the analytics SP (WF-26), leaving
the metric coarse for alerting.

**Acceptance.** Operators can slice run counts by workspace; users can see per-node timings through
the API.

---

## WF-28 🟠 Credit accounting is a stub

**Status:** ✅ Fixed 2026-08-07.

`CreditCostOptions` prices each node kind, bound from `WorkflowEngine:CreditCosts` — **configuration,
not compiled in**, because pricing is an operator decision that changes without a release. Anything not
overridden keeps `CreditCostOptions.BuiltIn`; a kind absent from both falls back to `DefaultCost`
rather than costing nothing.

**The shape of the default table is the point.** Outbound I/O (`action:webhook`, `email`, `telegram`)
is 5, in-process messaging 1, bookkeeping (logic, variable, join, the parking nodes) 0.1. A webhook
occupies a connection, waits on somebody else's server and can retry; a local variable set is a
dictionary write. Charging both `1.0` made `CreditBudget` a node-execution ceiling wearing a billing
label — a thousand cheap control nodes and a thousand outbound calls were indistinguishable to the
operator paying for them.

**Retries are already attempt-weighted**: the charge sits inside `RetryExecutor`'s attempt loop, so
three attempts at a webhook cost three webhooks. That needed no change — it just now means something.

**This also closes WF-29's deferred credit record.** `creditsCharged` travels on the `NodeCompleted` /
`NodeFailed` payload, summed across attempts, via a `CreditChargeNotification` callback (the same shape
as `NodeRetrying`, since `RetryExecutor` has no history provider). **Deliberately not a row per
charge** — that would roughly double history write volume for a number that belongs on the outcome
event anyway.

**Tests.** Outbound costs more than bookkeeping; configuration overrides the built-in; an unpriced kind
falls back to the default; and every executable kind prices above zero, so nothing can become invisible
to both the budget and the meter.

**Symptom.** `CreditBudget` on a workflow is effectively a node-execution ceiling, not a cost control.
`wbskt_workflow_credits_consumed_total` is a node counter wearing a billing label.

**Root cause.** `Wbskt.Workflow/Runtime/DefaultCreditCostCalculator.cs:8` —
`// TODO: Phase 10 will implement actual cost calculation`; `Calculate` returns `1.0m` for every node
regardless of kind or result.

**Fix.** Define a cost model per node kind (an outbound webhook is not the same cost as a local
variable set), make it configuration-driven rather than compiled in, and charge on the actual work
done (e.g. duration- or attempt-weighted for retried actions). The atomic charge path
(`RunCounters_TryCharge` + `OutOfCredits` terminal status) is already correct — only the calculator is
a placeholder.

**Acceptance.** Credit consumption differentiates node kinds, and the metric means something.

---

## WF-29 🟠 History trace is missing key events

**Status:** ◐ Partial — everything except **arrivals that produce no run** landed (2026-08-06, plus the
inbound enrichment and credit record 2026-08-07). What remains is a schema decision, stated at the
bottom; it is deliberately not guessed at.

**Inbound arrivals that produce a run are now in the trace** (2026-08-07). `RunStarted`'s payload
gained `channelKind`, `matchKeys` and `receivedAt` alongside the inbound event id and correlation key,
so a trace can answer "what fired this?" rather than only "something did". A separate
`InboundEventReceived` row was considered and rejected — it would say the same thing one line earlier,
at the cost of a row per run. A bookmark wake was already covered by `BranchResumed`'s dispatch reason.

**The trigger body is deliberately not recorded**, which settles the `[RJ]` TODO that sat on that line.
It is caller-controlled and unbounded — the public callback caps a *request* body, not the history
table — and it can carry whatever the caller sends, credentials included. The run's trigger payload is
already persisted on the run, which is where to read it from.

**Credit charges landed with WF-28.** `creditsCharged` travels on the `NodeCompleted`/`NodeFailed`
payload, summed across attempts. Now that costs differ per kind it carries information; a row per
charge was rejected as roughly doubling history write volume for a number that belongs on the outcome
event.

**What landed.**
- **`durationMs` on every node outcome** — the `Stopwatch` value was already measured and thrown
  away. Deriving it by subtracting the `NodeStarted` timestamp only works while both rows survive
  retention, and silently includes retry backoff. This is what per-node timing analytics (WF-26)
  reads.
- **`BranchResumed`** — a branch waking from a bookmark logged as `BranchStarted`, making a
  parked-then-woken branch indistinguishable from a fresh one. Carries the dispatch reason.
- **`BranchFailed`** — `NodeFailed` alone said which node broke, not that the branch ended nor how
  the failure was handled; `FailBranch`, `FailRun` and `Compensate` were indistinguishable. Carries
  the error, the `onFailure` outcome and whether compensation ran.
- **`NodeRetrying`** — retries were invisible: a node retried four times looked like one slow node.
  Carries attempt, max attempts, backoff delay and reason. `RetryExecutor` has no history provider,
  so the branch loop passes a `RetryNotification` callback.
- **`CompensationFailed`** — the orchestrator had a bare `catch {}`, so an undo that never happened
  left no trace at all. Now logged and recorded (guarded, since the caller is already handling a
  failure).
- **Severity centralised** in `HistoryEventKind.SeverityFor`. It was an inline ternary that made
  everything except `NodeFailed` `Info` — so `RunFaulted` (the engine failing) and `BranchCancelled`
  were both filed as routine. Severity is not cosmetic: retention keeps `Warn`/`Error` far longer,
  so a wrong severity discards the record of a failure. All emitters now use the shared constants.

**⚠ Still open: a durable record of arrivals that produce *no* run** — filtered, secret mismatch,
dropped, queued, or matching nothing. These cannot go in `HistoryEvents`, whose clustered key leads
with a `NOT NULL RunId`, so they need their own table. **Three things make this an owner decision
rather than wiring**, and guessing at any of them would be worse than the log line that records them
today:

1. **Access.** An arrival that matched nothing has no workspace to scope it to, so a per-workspace
   endpoint over such a table would leak one workspace's arrivals to another. Either it is an
   operator-only table with no user-facing route, or it needs a scoping rule invented for it.
2. **Volume.** It is one insert per inbound event, on the hottest path in the engine — the same path a
   chatty telemetry trigger hammers. That is a real cost to accept deliberately, with a retention
   window chosen alongside it.
3. **It is no longer the only answer to the question.** WF-32 made the dispatch result report *every*
   registration's outcome, and WF-15/16 added `Filtered` and `SecretMismatch` to that set — so a caller
   firing a webhook is now told, in the response, exactly why nothing ran. The durable log adds
   after-the-fact forensics, not first-line diagnosis.

Making `RunId` nullable was considered and rejected outright: it is the leading column of the clustered
index, so nullable-leading-key clustering would be paid on every existing per-run history query, to
store rows that are not run history.

**Symptom (user).** The run trace cannot explain *why* a node took 40 seconds, whether it was retried,
or when an inbound event arrived. A UI rendering the trace has to diff `NodeStarted`/`NodeCompleted`
timestamps client-side to get durations.

**Currently emitted** (`BranchLoop`, `RunStarter`, `RunFinalizer`, `RunCancellationService`,
`CompensationOrchestrator`): `RunStarted`, `BranchStarted`, `NodeStarted`, `NodeCompleted`,
`NodeFailed`, `BranchParked`, `BranchCompleted`, `BranchCancelled`, `RunCancellationRequested`,
`CompensationExecuted`, `RunFaulted`, `RunFinalized`.

**Missing:**
- **Inbound events** — `Wbskt.Workflow/Runtime/InboundHub.cs:38` carries an explicit TODO:
  *"Append engine-host inbound history entry per design §4.3/§6.1 when Phase 8 history wiring lands."*
  Signals and wakes therefore never appear in the trace.
- **Retry attempts** — `RetryExecutor` emits nothing; a node retried 4 times looks like one slow node.
- **Bookmark resumed** — a branch parks (`BranchParked`) and then simply continues.
- **Credit charged** — no per-node cost record.
- **`BranchFailed`** — the fail path never appends one (remediation plan §5.2), and severity mapping is
  an inline ternary in `BranchLoop.AppendEventAsync:427` (`NodeFailed` → `Warn`, everything else →
  `Info`), so `BranchCancelled` and `RunFaulted` are both logged as `Info`.
- **Node duration** — not on the `NodeCompleted` payload, though `BranchLoop` already has the
  `Stopwatch` value at line ~193.

**Fix.** Add the missing kinds; put the duration on the `NodeCompleted` payload; centralize
kind→severity in a small static map. Watch the write volume — history is written synchronously per
event, so batch where a single node produces several.

**Acceptance.** A run trace shows inbound arrivals, retries, resumes, and per-node durations without
client-side inference.

---

## WF-30 🟡 `FlusherLag` gauge is hardcoded to zero

**Status:** ✅ Fixed 2026-08-07 — deleted. History writes are synchronous, so there is no flusher and
no lag; a permanently-zero gauge reads as healthy forever.

---


`Wbskt.Workflow/Telemetry/WorkflowMetrics.cs:36` —
`FlusherLag = _meter.CreateObservableGauge("wbskt_workflow_history_event_flusher_lag", () => { return 0L; });`

History writes are synchronous now, so there is no flusher and no lag. A permanently-zero gauge is
worse than no gauge — it will read as healthy on a dashboard forever. **Delete it.**

---

# H. Runtime architecture and hygiene

## WF-31 🟠 Cancellation state is cached per-host with no cross-host invalidation

**Status:** ✅ Fixed 2026-08-07 — all four markers. **A new, separate defect was found while doing it;
it is written up as [WF-37](#wf-37--a-runs-cancellationtoken-can-never-be-cancelled).**

**The event path was already end-to-end** — the management host publishes
`WorkflowRunCancellationRequestedEvent` and the engine host consumes it — **but the consumer never
touched the engine's cache.** `IsCancellationRequestedAsync` reads through a 10-second per-host
`IMemoryCache`, and the only place that wrote to it was the tail of `RequestCancellationAsync`, which
the consumer never reached: the management host had already made the transition, so the engine's call
short-circuited on `if (!transitioned) return false`. New `IRunCancellationService.MarkCancellationRequested`
(cache + token, no database) is now the consumer's **first** action, before any round trip.

**`RequestCancellationAsync` no longer treats "did not transition" as "nothing to do."** It now
distinguishes the two reasons that can happen:

- **Already `Cancelling`** — another host got there first. The local cleanup still runs. This matters
  because the engine host is the one that owns bookmarks, branches and the finalizer; skipping it left
  a cancelled run holding live bookmarks, which `Run_GetStuck` deliberately excludes, so the reaper
  could not collect it either.
- **Terminal** — the only case that returns `false`.

**`:92` — cancelling a run that is not `Running`.** A parked run *is* `Running` (only its branch is
`Waiting`), so that case always worked. The real hole was `Failing`: a run whose first branch failed
while its siblings keep going could not be cancelled at all, silently. The transition now tries
`Running → Cancelling` and then `Failing → Cancelling`. No schema change — the existing
`Run_TransitionStatus` is called twice rather than gaining a status list.

**`:120` — the recovery path.** `RunRecoveryService` called only `CancelCts` for a run found in
`Cancelling`, leaving its bookmarks and waiting branches behind; the branches it re-dispatched would
unwind but a parked sibling kept `ActiveBranchCount` above zero forever, and the reaper skips any run
holding a bookmark — so the run could never terminate. It now re-applies the cancellation (idempotent
on an already-`Cancelling` run). A `Failing` run still gets only its token cancelled — `Failing` is not
cancelled, and turning it into a cancellation would be wrong.

**Idle-finalization moved into the service.** The engine consumer used to carry its own
"if `ActiveBranchCount <= 0`, finalize" block. That logic belongs with the cleanup it completes, so it
is now `RunCancellationService.FinalizeIfIdleAsync`, which means the recovery path gets it too. It is
guarded: a counters read that fails must not turn a successful cancel into a reported failure. The
consumer is down to two calls and three of its four dependencies are gone.

**Republish guard.** The event is published **only when this call made the transition**. Without that,
the already-`Cancelling` path would echo the event back to the host that sent it, forever.

**Also fixed, user-visible.** `WorkflowRunQueryService.CancelAsync` discarded the boolean and always
returned success — cancelling a finished run reported that it had been cancelled. It now returns 409
`RUN_NOT_CANCELLABLE`, which the widened `false`-means-terminal contract makes meaningful.

**Tests.** Six new `RunCancellationServiceTests` cases (cancel a `Failing` run; cleanup on the
already-`Cancelling` path with no duplicate history event and no republish; publish exactly once on a
real transition; `MarkCancellationRequested` visible immediately against a run row that still reads
`Running`; finalize when idle; don't finalize while a branch runs), rewritten consumer tests pinning
mark-before-request, a recovery test separating the `Cancelling` and `Failing` paths, and a controller
test for the 409.

**⚠ Residual, narrow.** A host crashing *between* the status transition and the bookmark delete leaves
a run `Cancelling` with live bookmarks and no running branch. Recovery only walks runs that have
running branches, so nothing sweeps it and the reaper excludes it. Closing that needs a way to
enumerate `Cancelling` runs (a new procedure); it is not reachable by any non-crash path.

**Symptom.** A cancel issued through the Management host is not seen by the Engine host until its
10-second cache entry expires and it re-reads the DB. Cancellation is advertised as cooperative, but
the latency floor is invisible to the user.

**Root cause.** The owner's own markers in `Wbskt.Workflow/Runtime/RunCancellationService.cs`:
- `:88` — *"cancel internally uses a cache but it lives in WMH and WEH separately."*
- `:89` — *"cancellation must be passed to WEH from WMH through events."*
- `:92` — *"this will transition only if the run currently is in 'Running' — what about requesting
  cancellation for runs that are waiting/bookmarked."*
- `:120` — *"what about the cancellation from the RunRecoveryService? don't we need to delete bookmarks
  of those runs too?"*

`WorkflowRunCancellationRequestedEvent` and its consumer
(`Hosts/Wbskt.Workflow.Engine.Host/InboundAdapters/WorkflowRunCancellationRequestedEventConsumer.cs`)
already exist — verify whether the Management host publishes it on the cancel path, and whether the
consumer invalidates the local cache. If it does, items :88/:89 may be partly resolved and the markers
are stale.

**Fix.** Confirm the event path end-to-end; make the consumer invalidate the engine's cache
immediately. Separately, handle the parked-run case at `:92` — a run whose only branches are `Waiting`
needs its bookmarks deleted and branches cancelled, not just a status transition.

**Acceptance.** Cancelling a parked run takes effect without waiting for cache expiry, and leaves no
live bookmarks.

---

## WF-32 🟠 Multi-registration dispatch reports only the first run

**Status:** ✅ Fixed 2026-08-07.

**What landed.** `TriggerDispatchResult` gained `Registrations` — one
`TriggerRegistrationDispatch(RegistrationId, WorkflowRefId, Outcome, RunId?, CorrelationKey)` per
matched registration, in processing order — plus a `StartedRunIds` convenience. The dispatcher builds
that list as it goes and *derives* the summary from it, so the two cannot disagree; the running
`aggregateOutcome`/`firstStartedRunId` folding is gone.

`WorkflowRefId` is on each entry deliberately: a registration id is opaque to an API caller, and the
question a webhook caller actually has is "which workflows did my call fire?"

**The summary stays lossy, on purpose.** `Outcome` is `StartedRun` if any registration started a run,
else `Queued`, else `Dropped`, else `NoRegistration`; `RunId` is the first started run. Collapsing N
outcomes into one value cannot be information-preserving, so the fix is not a cleverer fold — it is
that the detail now exists alongside it. Both are documented on the record. A new
`TriggerDispatchOutcome.Mixed` was considered and rejected: it would break every caller's switch while
telling them less than the list already does.

**Controllers.** `InboundManualController` and `InboundWebhookController` project the list into a
`Registrations` array (`registrationId`, `workflowRefId`, `outcome`, `runRefId`, `runId`,
`correlationKey`) via a shared `InboundDispatchProjection`, keeping their existing top-level
`Outcome`/`RunRefId`/`RunId` fields unchanged so nothing that reads them breaks. Run ref ids come from
the projection rather than a second lookup. **The signal and wake controllers were left alone** — those
channels only ever resolve bookmarks (nothing publishes a trigger registration on them), so their list
would always be empty; each carries a comment saying so rather than an empty array in the response.

**Tests.** Three dispatcher cases (fan-out reports every registration with distinct run ids; a dropped
registration stays visible next to a started one — the exact case the old fold erased; the bookmark
path reports no registrations) and two controller cases. `Docs/API.Endpoints.md` §4 documents the
response shape.

**Symptom.** One inbound event matching several trigger registrations starts several runs, but the API
response names only one, arbitrarily.

**Root cause.** `Wbskt.Workflow/Runtime/TriggerDispatcher.cs:123-131`, with the owner's markers:
*"re-think aggregation. this is wrong/meaningless aggregation data"* and *"TODO: properly output
aggregated results. currently, this only returns run id of the first started run."* The
`aggregateOutcome` folding at lines 96-110 also loses information — a mix of `Dropped` and `StartedRun`
reports only `StartedRun`.

**Fix.** Change `TriggerDispatchResult` to carry a per-registration result collection
(`registrationId`, `outcome`, `runId?`, `correlationKey`) plus a derived summary outcome. Update the
four inbound controllers to project it — `InboundWebhookController` and `InboundManualController`
currently return a single `RunId`/`RunRefId`.

**Acceptance.** An event fanning out to N registrations reports all N outcomes.

---

## WF-33 🟡 Hardcoded branch worker limit; no failure containment in the pump

**Status:** ✅ Fixed 2026-08-06.

**What landed.** `WorkflowEngineOptions.BranchWorkerLimit` (default 50) replaces the hardcoded
semaphore, wired through an `[ActivatorUtilitiesConstructor]` + internal test-constructor pair
matching the `MetricsExporter`/`HistoryRetentionGc` pattern already used in this folder, so the
existing three-argument test call sites still compile.

The catch-all now calls `ContainLeakedBranchAsync`: it reloads the branch and acts **only if the row
is still `Active`**, then marks it `Failed` with a `BRANCH_LOOP_ESCAPED` payload, decrements the
counter, and finalizes the run when that was the last branch. The Active-check is what makes it
idempotent — `BranchLoop`'s `EngineFaultException` path already drains its own slot before rethrowing,
so containment correctly does nothing there and cannot double-decrement. Every step is individually
guarded; if containment itself fails the run is left to the `RunReaper`.

**This is the generic containment that limits WF-01 and anything like it.** Cross-ref remediation plan
§5.5.

---

## WF-34 🟡 Resolved markers and small hygiene

**Status:** ✅ Fixed — behavioural items 2026-08-07, the RetryExecutor cleanup and the remaining
markers the same day.

**`RetryOn` is now applied** — this was a real behaviour gap, not tidying. `policy.RetryOn` was
collected and never consulted, so a policy saying "only retry these" retried *everything* transient. A
non-matching failure now returns immediately as non-retryable. An empty list still means "anything".

**Jitter uses `Random.Shared`.** A fresh `Random` per call meant instances created in quick succession
shared a seed, so concurrent retries jittered *identically* — defeating the entire purpose of jitter,
which is to stop simultaneous retries from re-colliding.

**Markers resolved into explanations** rather than deleted, where the question had an answer worth
recording: `PendingTakePort` (a pre-decided exit taken on re-entry without re-running the node), the
`RunCounters` seeding question, the trigger-key/idempotency interaction, the pending-event drain, and
why the publishers are wrapped (so `Wbskt.Workflow` carries no event-bus dependency).

**Markers retagged, not removed**, where the concern is real and tracked: the cancellation ones now
read `TODO(WF-31)` and the dispatch-aggregation ones `TODO(WF-32)`, so they point at the item that
will fix them instead of looking like stray musings. `BaseNode`'s polymorphism note became
`TODO(arch)` with a note that the hand-rolled converter works.

**Completed 2026-08-07.** `RetryExecutor`'s six identical idempotency blocks collapsed into one local
`RecordOutcomeAsync` — 120 lines removed, 36 added. **The duplication was hiding a real finding**: each
block carried an `isSideEffectFree` branch that could never execute, because `idempotencyKeyProvider`
is only ever assigned when the node is *not* side-effect-free. Those branches wrote two rows per
execution that nothing could read. They are gone, and the helper says once what the six blocks each
implied — a provider that does not support this is disabled for the rest of the call rather than
retried into.

**The remaining `[RJ]` markers are resolved into explanations**, none deleted without an answer:
`RunStarter`'s correlation-key fallback (kept because it is a public runtime entry point and a null
correlation key would break every concurrency policy), `RowVersion` (a SQL Server `ROWVERSION` the
engine maps but never compares — recorded as the hook if a branch ever stops being driven by one host
at a time), the publisher wrapper, `TriggerDispatcher`'s bookmark question (bookmarks are matched and
returned on *before* this line, so reaching it means the event resumed nothing), and
`EvaluateCorrelationExpression` — now documented as exactly two forms, with the note that a mistyped
path falls back to the trigger key and so correlates everything together, which is what the validator's
`SUSPICIOUS_CORRELATION_KEY` warning exists to catch. `BookmarkResumer`'s long pasted explanation is
now four lines saying the same thing.

The one marker left in the tree, `IdentityService.cs:10`, is in `Wbskt.Infrastructure` and outside this
report's scope.

---


**Safe to delete** — investigated and confirmed fine, with `EDIT:` follow-ups already in place:
- `Wbskt.Workflow/Runtime/RunStarter.cs:87-88` — RunCounters seeding (resolved: `Run_Create` seeds it).
- `Wbskt.Workflow/Runtime/TriggerDispatcher.cs:46-48` — idempotency/bookmarking (resolved, explained in
  `BookmarkResumer`).

**Cosmetic / low value:**
- `Wbskt.Workflow/Runtime/BranchLoop.cs` ctor assigns `_workflowMetrics` twice (lines 69 and 74).
- `Wbskt.Workflow/Runtime/BranchLoop.cs:124` — *"what is PendingTakePort?"* It is the
  resume-with-a-predetermined-port slot; replace the question with a comment saying so.
- `Wbskt.Workflow/Runtime/RunStarter.cs:113` — `RowVersion = Array.Empty<byte>()`; the column is a
  `ROWVERSION` the code never reads. Either use it for optimistic concurrency on `Branch_Update` or
  drop it from the entity.
- `Wbskt.Workflow/Runtime/RunStarter.cs:125` — `RunStarted` history payload carries only
  `InboundEventId` + `CorrelationKey`, not the trigger payload. Decide (payload size vs.
  debuggability) and remove the TODO.
- `Wbskt.Workflow/Runtime/RunStarter.cs:131`, `RunFinalizer.cs:80,83` — publisher-wrapper questions;
  `IRunStartedPublisher`/`IRunCompletedPublisher` exist so `Wbskt.Workflow` has no event-bus
  dependency. Document the reason and delete the TODOs.
- `Wbskt.Workflow.Abstraction/Models/Nodes/BaseNode.cs:5` — *"move to polymorphic"*. The hand-rolled
  `BaseNodeJsonConverter` works and writes `kind` first; migrating to STJ `[JsonPolymorphic]` is
  optional.
- `RetryExecutor` (remediation plan §5.1): `policy.RetryOn` is never applied; there are unreachable
  side-effect-free branches; six duplicated try/catch blocks; `GetDelay` should use `Random.Shared`.

---

## WF-37 🟠 A run's `CancellationToken` can never be cancelled

**Status:** ✅ Fixed 2026-08-07. Found earlier the same day while doing WF-31; not part of the original
review.

**What landed.** A `RunCancellationTokenRegistry` holds the dictionary and is registered `Singleton`;
the scoped `RunCancellationService` takes it. A `static` field would have fixed the lifetime just as
well and been worse — xUnit runs test classes in one process, so a static registry leaks cancellations
between them. The constructor parameter is optional, so a hand-built test instance gets its own private
registry, which is the isolation a test wants.

`Cancel` on an unknown run id creates the source **already cancelled**, so a cancel arriving before the
branch asks for its token is not lost.

**The behaviour change turned out to be one line, in the other direction than expected.** `BranchLoop`
already classified an `OperationCanceledException` correctly — run cancel → clean `Terminal(Cancelled)`,
host shutdown → leave the branch `Active` for recovery — so the interrupted-node path was already
right. But **`RetryExecutor`'s catch-all swallowed the exception into `EXECUTOR_CRASH`** before it could
reach that classification. It now rethrows when `ct.IsCancellationRequested`, and only then: an executor
throwing `OperationCanceledException` with nothing actually cancelled is a bug in that executor, not a
cancellation, and must not masquerade as one. Both cases are pinned by tests, as is the fact that a
cancelled node is not retried.

Outbound executors needed no change: `WebhookNodeExecutor` and `TelegramNodeExecutor` catch only
`HttpRequestException`, and `EmailNodeExecutor` explicitly excludes `OperationCanceledException`, so
cancellation propagates from all three.

**Tests.** Registry shared across two service instances (the scope case); a cancel that lands before the
token is handed out; `RetryExecutor` rethrowing versus crashing.

---

**Original write-up, for the record:**

**Symptom.** `CancelCts` appears to cancel a running branch's work and does not. Cancellation only ever
takes effect at the *between-nodes* `IsCancellationRequestedAsync` check, so a cancel issued during a
long node — a 60-second webhook, say — is not noticed until that node finishes on its own.

**Root cause.** `RunCancellationService` holds its CTS registry in a **per-instance**
`ConcurrentDictionary` (`_ctsRegistry`), but the service is registered **Scoped**
(`WorkflowServiceCollectionExtensions.cs:36,120`). `BranchExecutionPump` creates a fresh scope per
branch execution and resolves `IBranchLoop` from it (`BranchExecutionPump.cs:55-56`), so
`BranchLoop.RunAsync`'s `GetToken(runId)` (`BranchLoop.cs:89`) mints a token in *that scope's* empty
registry. Every `CancelCts` caller — the event consumer, the recovery service,
`RequestCancellationAsync` — runs in a different scope and therefore cancels a different, unobserved
`CancellationTokenSource`. The registry is process-global by nature; its lifetime says otherwise.

The read-through cache is unaffected: `IMemoryCache` is a singleton (`AddMemoryCache`), which is why
cooperative cancellation works at all today and why WF-31's fix lands correctly.

**Fix.** Move the registry behind a singleton — a small `RunCancellationTokenRegistry` holding the
dictionary, registered `AddSingleton` and injected into the scoped service. Prefer that to marking the
field `static`: a static registry is shared between xUnit test classes running in one process and will
leak cancellations across tests.

**⚠ This is a behaviour change, not just a wiring fix.** Today no node is ever interrupted mid-flight.
Once the token really cancels, in-flight `HttpClient` calls, delays and provider calls will start
throwing `OperationCanceledException` from inside executors, and every one of those paths needs to be
checked for whether it produces a clean `Cancelled` branch or an `EXECUTOR_CRASH`. Do this as its own
item with its own tests; do not fold it into an unrelated change.

**Acceptance.** A cancel issued on any host interrupts the run's in-flight node work, and an
interrupted node ends its branch `Cancelled` rather than failed.

---

## WF-35 🟡 The shipped example generator emits a broken workflow

**Status:** ✅ Fixed — generator 2026-08-06 (WF-02 B2), smoke test 2026-08-07. **The "runs to
`Succeeded`" half of the acceptance criterion was dropped deliberately**; see below.

**What landed.** `Program.BuildExamples()` and `Program.SerializerOptions` are public, and the test
project references the exporter, so `ExportedExampleSmokeTests` validates **exactly what gets
exported** rather than a copy — a copy cannot drift with the original, which is the entire failure mode
this item exists for. Each example is asserted to publish cleanly, **and to still validate after a JSON
round trip**: what ships is the file, not the builder output, so a converter that loses a node's config
would leave the in-memory definition valid and the shipped file broken. That is precisely the shape of
the original defect.

**⚠ "Executes to a terminal `Succeeded`" is not tested, and should not be.** The shipped examples are
not self-completing by design: `LinearHappyPath` contains a one-minute `Delay`,
`ErrorHandlingAndCompensation` waits up to an hour on an external signal, and all three send client
messages to devices that do not exist in any harness. Driving them to `Succeeded` would require a
controllable clock, an injected signal and stubbed device/webhook transports — at which point the test
asserts that the stubs work, not that the examples do. The validation and round-trip assertions catch
the regression class that actually occurred; the execution assertion would have caught nothing and cost
a fragile harness. **If it is wanted, it belongs on a purpose-built self-completing example** added for
that purpose, not on the illustrative ones.

`Tools/Wbskt.Workflow.Exporter/Program.cs` `BuildBranchingWorkflow` calls
`AddLogicGate("event.value > 100", …)`. Per WF-02 that condition can never evaluate — the exported
`BranchingWithLogicAndForEach.json` is a workflow that fails at runtime. Anyone using the examples as
a starting point inherits the bug.

**Fix.** Regenerate the examples once WF-02 lands, and add a smoke test that every exported example
passes `WorkflowValidator` **and** executes to a terminal `Succeeded` in the E2E harness.

---

# Suggested order

> **Historical.** This was the plan before any of it was done, and every wave below has now been worked
> through. It is kept because the dependency reasoning is still the best explanation of *why* the items
> landed in the order they did. For what is actually left, see the handoff block at the top.

Dependencies are real here — this order avoids rework.

**Wave 1 — stop the bleeding (P0, independent)**
1. **WF-01** — contain the executor lookup + resolve toast. Small, self-contained, removes a permanent
   run leak.
2. **WF-33** — pump containment (pairs naturally with WF-01).
3. **WF-20**, **WF-21** — two SP one-liners.
4. **WF-03** — Join failure contributions. Self-contained but touches schema + `BranchLoop`.

**Wave 2 — make the product usable (the headline gap)**
5. **WF-02 B1** — evaluate `Binary`/`Unary`/`Function`.
6. **WF-06** — `$shared` + templates (needs the evaluator injected with a scope factory).
7. **WF-02 B2** — authoring surface for conditions.
8. **WF-07** — Variable ops (SPs already exist; independent, can slot anywhere).

**Wave 3 — control-flow semantics** *(WF-04 introduces `RemoveKeys`, which WF-05 needs)*
9. **WF-04** — sequential ForEach.
10. **WF-05** — Delay re-arm.
11. **WF-10** — fan-out caps.

**Wave 4 — fail early instead of late**
12. **WF-18**, **WF-19** — validator rules. Several depend on earlier waves (item 1 needs WF-01's
    implemented-kinds set; item 3 needs WF-03's BFS helper; item 6 unblocks WF-17).
13. **WF-17** — atomic publish + cron validation.
14. **WF-22** — version authority + narrowed catch.

**Wave 5 — API surface**
15. **WF-11** — start-run error semantics.
16. **WF-12** — validate endpoint (needs WF-19's rules to be worth calling).
17. **WF-13** — lifecycle verbs.
18. **WF-14** + **WF-23** — workspace run list and its index.

**Wave 6 — analytics**
19. **WF-29** — history events (the data source for everything below).
20. **WF-26** — stats SP + endpoints.
21. **WF-27**, **WF-28**, **WF-30** — metric tags, credit model, delete the fake gauge.

**Wave 7 — remaining**
22. **WF-15**, **WF-16** — trigger filters and secrets (filters need WF-02).
23. **WF-09** — sub-workflow inputs.
24. **WF-31**, **WF-32** — cancellation propagation, dispatch aggregation.
25. **WF-08** — email/telegram, if they are in scope at all.
26. **WF-24**, **WF-25**, **WF-34**, **WF-35** — hygiene.
27. **WF-36** — deferred; document only.

---

## Relationship to the existing remediation plan

`Docs/Workflow.Engine.V3.Remediation.Plan.md` Phases 1–2 are **done and verified**. Its Phases 3–6
remain accurate against current code and overlap this document as follows:

| Plan section | This document |
|---|---|
| §3.1 `$shared`/templates | WF-06 (**note:** the plan understates the scope — `Binary`/`Unary`/`Function` are also unimplemented, see WF-02) |
| §3.2 Variable Increment/Decrement | WF-07 |
| §3.3 Sequential ForEach + Delay | WF-04, WF-05 |
| §3.4 Join failure handling | WF-03 |
| §3.5 Webhook scoping + secret | WF-16 (**scoping half already fixed**; secret half open) |
| §3.6 Validator additions | WF-18, WF-19 |
| §3.7 Trigger filters | WF-15 |
| §4.1 Indexes | WF-23 |
| §4.2 SP fixes | WF-20, WF-21 |
| §4.3 Match-key consistency | *not re-verified in this review — check `BranchLoop.GetWakeConditionMatchKey` vs `ClientPayloadReceivedConsumer` before assuming it is still open* |
| §4.4 INT→BIGINT | WF-36 |
| §5.1–5.6 Code hygiene | WF-33, WF-34 |
| §6 Design doc reconciliation | still open — see the plan |

**Not covered by the plan, new in this review:** WF-01, WF-02 (beyond §3.1's scope), WF-09, WF-10,
WF-11, WF-12, WF-13, WF-14, WF-17, WF-24, WF-25, WF-26, WF-27, WF-28, WF-29, WF-30, WF-32, WF-35.
