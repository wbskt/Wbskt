# Workflow Engine — Gap & Issue Report

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
| WF-07 | 🟠 | Nodes | ◐ Variable node supports only `Set`; shared `Set` can fail under contention |
| WF-08 | 🟠 | Nodes | `action:email` and `action:telegram` are unimplemented stubs |
| WF-09 | 🟠 | Nodes | Sub-workflow cannot receive input from its parent |
| WF-10 | 🟠 | Nodes | No fan-out concurrency cap on `ForEach`/`ParallelForEach` |
| WF-11 | 🟠 | API | ✅ Normal outcomes on "start run" return HTTP 500 |
| WF-12 | 🟠 | API | ✅ No validate / dry-run endpoint |
| WF-13 | 🟠 | API | Deprecate is one-way; no pause/resume, rename, rollback, or delete |
| WF-14 | 🟠 | API | ✅ No workspace-wide run list |
| WF-15 | 🟠 | Triggers | Trigger filter expressions are plumbed but hardcoded to null |
| WF-16 | 🟠 | Triggers | Webhook triggers have no secret |
| WF-17 | 🟠 | Triggers | ✅ Invalid cron publishes the workflow, then throws — leaving it scheduleless |
| WF-18 | 🟠 | Validator | ✅ Duplicate `(node, port)` edges throw at runtime instead of failing at publish |
| WF-19 | 🟠 | Validator | ✅ Five more missing publish-time checks |
| WF-20 | 🟡 | Database | ✅ `PendingTriggerEvent_DequeueNextBy_…_Correlation` dropped `TriggerNodeId` (was dead code — deleted) |
| WF-21 | 🟠 | Database | ✅ `Bookmark_DeleteOrphans` omits `Faulted` |
| WF-22 | 🟠 | Database | ✅ Publish version authority is split between C# and the SP |
| WF-23 | 🟠 | Database | ✅ Four missing indexes, one of them on the user-facing run list |
| WF-24 | 🟡 | Database | Unbounded `Warn`/`Error` history growth |
| WF-25 | 🟡 | Database | Three dead stored procedures |
| WF-26 | 🟠 | Analytics | No user-facing analytics endpoint at all |
| WF-27 | 🟠 | Analytics | Metrics are not workspace-scoped and node timings are not per-node |
| WF-28 | 🟠 | Analytics | Credit accounting is a stub — every node costs exactly 1.0 |
| WF-29 | 🟠 | Analytics | History trace is missing inbound, retry, resume and charge events |
| WF-30 | 🟡 | Analytics | `FlusherLag` gauge is hardcoded to zero |
| WF-31 | 🟠 | Runtime | Cancellation state is cached per-host with no cross-host invalidation |
| WF-32 | 🟠 | Runtime | Multi-registration dispatch reports only the first run started |
| WF-33 | 🟡 | Runtime | ✅ Branch worker limit is hardcoded; pump has no failure containment |
| WF-34 | 🟡 | Hygiene | Resolved `[RJ]:` markers and duplicate assignments |
| WF-35 | 🟡 | Hygiene | ◐ Shipped example generator emits a workflow that fails at runtime |
| WF-36 | 🟡 | Runtime | `INT` primary keys modelled as `long` with checked casts |

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

**Status:** ◐ Partial — `Increment`/`Decrement` landed and `Set` is fixed (2026-08-06).
`CompareAndSet` is **blocked on a config change**, see below.

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

**Status:** ☐ Open. Note WF-01 landed `ToastNodeExecutor`, so these two are now the only
unimplemented `action:*` kinds — and `WorkflowBuilder` gained `AddToast`, leaving `AddEmail`/`AddTelegram` as the remaining builder gaps.

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

`HistoryEvent_DeleteForRetiredRuns.sql` deletes events for runs completed before the cutoff **except**
those with `Severity IN ('Warn','Error')` — which are kept forever. On a failure-heavy workspace
`HistoryEvents` grows without bound.

**Fix.** Add a second, much longer retention window for `Warn`/`Error` (e.g.
`HistoryRetentionWindowElevated`, default 1 year) rather than infinite, and delete beyond it.

---

## WF-25 🟡 Three dead stored procedures

Verified zero references anywhere in the solution:
- `WorkflowDefinition_GetAllEnabled.sql`
- `ClientCapabilities_GetBy_ClientId.sql`
- `Run_CountByStatus.sql` — the provider method `RunProvider.CountByStatusAsync:145` exists, but
  **nothing in production calls it**; only tests do. Note WF-26 wants exactly this shape, so consider
  keeping it and wiring it up rather than deleting.

**Fix.** Delete the first two; decide on the third alongside WF-26.

---

## WF-36 🟡 `INT` primary keys modelled as `long`

`Runs.Id` and `Branches.Id` are `INT` while the code models them as `long` with `checked((int))` casts
throughout. Migration is invasive (FKs everywhere) and was explicitly deferred by the owner
(remediation plan §4.4). Recorded here as a known ceiling — revisit before production scale.

---

# G. Observability and analytics

## WF-26 🟠 No user-facing analytics endpoint at all

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

`Wbskt.Workflow/Telemetry/WorkflowMetrics.cs:36` —
`FlusherLag = _meter.CreateObservableGauge("wbskt_workflow_history_event_flusher_lag", () => { return 0L; });`

History writes are synchronous now, so there is no flusher and no lag. A permanently-zero gauge is
worse than no gauge — it will read as healthy on a dashboard forever. **Delete it.**

---

# H. Runtime architecture and hygiene

## WF-31 🟠 Cancellation state is cached per-host with no cross-host invalidation

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

## WF-35 🟡 The shipped example generator emits a broken workflow

**Status:** ◐ Partial — the generator was fixed as part of WF-02 B2 (it now emits a structured
`BinaryExpression`, verified by running it). **Remaining:** the smoke test that every exported example
passes `WorkflowValidator` *and* executes to a terminal `Succeeded` in the E2E harness.

`Tools/Wbskt.Workflow.Exporter/Program.cs` `BuildBranchingWorkflow` calls
`AddLogicGate("event.value > 100", …)`. Per WF-02 that condition can never evaluate — the exported
`BranchingWithLogicAndForEach.json` is a workflow that fails at runtime. Anyone using the examples as
a starting point inherits the bug.

**Fix.** Regenerate the examples once WF-02 lands, and add a smoke test that every exported example
passes `WorkflowValidator` **and** executes to a terminal `Succeeded` in the E2E harness.

---

# Suggested order

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
