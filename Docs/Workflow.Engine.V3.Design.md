# Wbskt Workflow Engine V3 — Design

**Status:** Draft, in progress (Sections 1 and 2 complete; Sections 3–7 outlined only)
**Date:** 2026-05-23
**Namespace:** `Wbskt.Workflow.Engine.Host.V3` (fresh; no carry-over from V1/V2)

## Context

Wbskt is an IoT automation builder. The Workflow Engine has to handle a wide spectrum:

- **Real-time, short-lived reactive flows** — device event → a few nodes → done in milliseconds.
- **Long-running orchestrations** — flows that sleep for hours or days, survive process restarts, can be paused/resumed/cancelled.
- **Multi-tenant user-facing automation platform** — many workspaces, many definitions, visible run history is a product feature.
- **Mission-critical IoT command/control** — must-deliver semantics, retries, compensation when a step fails partway.

This document defines the concepts for V3. The existing V1 (`Hosts/Wbskt.Workflow.Engine.Host/Services/WorkflowEngine.cs`) and V2 (`Hosts/Wbskt.Workflow.Engine.Host/V2/`) implementations are **not** carried forward; V3 starts from a blank page in a new `V3` namespace.

---

## Locked architectural decisions (the eight load-bearing choices)

These are the cross-cutting decisions all sections are built on. Each is a deliberate trade-off — captured here so future contributors can see *why* the design looks the way it does, not just *what* it says.

1. **Instance-per-trigger + workflow-scoped shared variables (hybrid).**
   Each external trigger creates a fresh, isolated **Run** with its own RunId, state, and history. Runs die when their work completes. Cross-run aggregate state (counters, debouncers, sliding windows, "SMS sent today") lives in a separate **SharedVariables** store, scoped to the workflow definition, accessed via atomic operations.
   *Rejected: V2's "long-lived runtime per definition" model (no run identity, hard to cancel/trace).*

2. **Hybrid durability: snapshot + history events + idempotency keys.**
   The engine snapshots the run state after every node completion (durability) and appends a slim history event log per run (observability + audit). Action invocations reserve an idempotency key before invoking; on resume, re-invocation uses the same key so downstream systems can dedupe.
   *Rejected: pure event-sourcing (Temporal-style determinism cost), boundaries-only persistence (no mission-critical safety).*

3. **Uniform Bookmark with polymorphic WakeCondition; separate code paths for trigger delivery vs bookmark resume.**
   Both "trigger started a new run" and "a sleeping branch is waking up" are inbound events; the data shape is uniform (`WakeCondition` is a discriminated union: `Timer | Signal | DevicePropertyChange | DeviceTelemetryMatch | Manual | ChildRunCompleted`). The routing code paths are kept separate for clarity — a Trigger creates a run, a Bookmark wake resumes an existing branch.

4. **Per-correlation-key concurrency policy, declared on the trigger node.**
   `AllowParallel` (default) / `Queue` / `CancelExisting` / `DropIfRunning`. The correlation scope is an expression evaluated against the trigger payload (e.g., `$trigger.deviceId`). Different triggers in the same workflow can have different policies.

5. **Error model: RetryPolicy + OnFailure + RunStatus aggregation; Compensate opt-in.**
   Per-node `RetryPolicy` (attempts, backoff, retryable error filter); per-node `OnFailure` (`FailBranch` default | `FailRun` | `Continue` | `Compensate`). Run-level status aggregates branch outcomes (`Completed | Failed | PartiallyFailed | Cancelled | Faulted | OutOfCredits`). Compensation is a deliberate opt-in for saga semantics, not the default.

6. **Pin runs to their definition version; GC unreferenced versions.**
   Every Run records `WorkflowRefId + Version` and uses that version forever. Editing a definition produces a new version. New triggers go to the latest enabled version. Old versions are storage-eligible for GC only when zero runs reference them AND they are not the latest.

7. **Loops + sub-workflows.**
   Two distinct node kinds: **`ForEach`** (sequential — body loops back to the ForEach node via a graph back-edge, iterator state in branch-local storage) and **`ParallelForEach`** (fan-out — emits N concurrent branches via a new `Fork` result variant). **`Join`** is the partner of ParallelForEach, using cohort tags to count expected arrivals. **Sub-workflows** are implemented as **child runs** — parent branch posts a Bookmark with `WakeCondition: ChildRunCompleted(childRunId)`.

8. **Single-host first, interface boundaries that allow leader/standby later.**
   V1 implementation: one engine host, in-process dispatch via `Channel<BranchPointer>`. The dispatch boundary is an interface (`IRunDispatcher`) so a SQL-backed work queue (`SELECT ... FOR UPDATE SKIP LOCKED`) can be substituted later. The Bookmark scheduler is already SQL-backed (it has to be — durable timers can't live in memory).

---

## Section 1 — Authoring Layer

This section defines the **static, immutable shape** of a workflow — what the user designs and saves. Nothing here knows about running, scheduling, or persistence. These types are pure description.

### 1.1 Node

A unit of work in the graph. A Node is **data, not behavior** — the executor lives in the runtime layer.

```text
Node {
  NodeId: Guid           // unique within definition
  Kind: string           // discriminator, e.g. "action:command", "control:logic"
  Name: string           // human label for the designer
  Config: object         // kind-specific settings (serializable)
  Ports: PortDefinition[] // declared exits
}
```

The separation lets the same definition be serialized to JSON, edited in a visual designer, validated without instantiating execution machinery, and versioned cleanly. **Definitions are portable; executors are deployed.**

### 1.2 Port

A named exit on a node, identified by `(NodeId, PortId)`. `PortId` is a string scoped to its node — e.g., `"out"`, `"true"`, `"false"`, `"body"`, `"done"`, `"completed"`, `"timeout"`.

Why named ports instead of "next node":
- Branching, fan-out, and conditional flow need labels for "which way did execution go?"
- Executors report `ActivatedPortIds`; the engine resolves the edges leaving those ports.

### 1.3 Edge

A directed connection: `(sourceNodeId, sourcePortId) → (targetNodeId, targetPortId)`. Edges belong to the definition, not to either node.

- Multiple edges can leave the same port → **fan-out** (one activation spawns multiple downstream branches).
- Multiple edges can enter the same node → **fan-in** (only meaningful for `Join`; for other nodes, each inbound edge is an independent activation).

### 1.4 Graph

Implicit; `(Nodes, Edges)` is the graph. No separate `Graph` class. The runtime builds derived indexes (e.g., `edgesBySourceNode`) at load time.

**Cycles are allowed.** A user can draw any topology, including back-edges. `ForEach` relies on this (its body's tail loops back to the ForEach node).

### 1.5 WorkflowDefinition

```text
WorkflowDefinition {
  WorkflowRefId: Guid                   // stable across versions
  Version: int                          // monotonically increasing per refId
  WorkspaceId: int
  Name, Description, IsEnabled
  Nodes: Node[]
  Edges: Edge[]
  SharedVariableSchema: SharedVarDecl[] // see 1.7
  CreatedAt, PublishedBy
}
```

- `(WorkflowRefId, Version)` is the unique identity of any single, immutable definition.
- "Editing" a workflow produces a **new version**, not a mutation.
- `IsEnabled` controls whether **new triggers** can start runs on this version. It does not affect in-flight runs (they continue) or bookmarks (they resume).

### 1.6 Node families

Structural metadata for designer UX, validation, and policy defaults. **Not** a separate inheritance hierarchy at runtime — every node implements the same `INodeExecutor` interface.

| Family | Purpose | Example kinds | Constraint |
|---|---|---|---|
| **Trigger** | Starts a Run. Output ports only. | `trigger:device`, `trigger:schedule`, `trigger:webhook`, `trigger:manual` | Soft warning if a definition has none |
| **Control** | Influences flow without external side effects (mostly). | `control:logic`, `control:foreach`, `control:parallelForEach`, `control:join`, `control:delay`, `control:variable`, `control:subWorkflow` | Pure w.r.t. outside world (except `variable`/`subWorkflow`) |
| **Action** | Performs external side effects. | `action:command`, `action:email`, `action:webhook`, `action:telegram`, `action:toast` | Carries `RetryPolicy` and `OnFailure` (Section 5) |

### 1.7 SharedVariableSchema

Cross-run state is declared up-front. The schema is the contract for what lives in the workflow-scoped store.

```text
SharedVarDecl {
  Name: string                       // e.g. "smsToday"
  Type: VarType                      // Counter | Number | String | Bool | Json
  Default: object?                   // initial value
  ResetPolicy: ResetPolicy?          // None | DailyAtUtc(time) | OnDefinitionPublish
}
```

Two reasons it's a typed schema, not free-form:
1. **Atomicity by type.** `Counter` exposes only `Increment`/`Decrement`/`Get` backed by atomic SQL UPDATEs. A free-form `object?` store would force read-modify-write loops with retries.
2. **Lifecycle clarity.** `ResetPolicy` is owned by a separate scheduled job, not hidden inside a workflow node.

Accessed in expressions as `$shared.<name>`. Written via `VariableNode { scope: "shared", op, var }`.

### 1.8 Validation philosophy

**The authoring layer validates only what would cause the runtime to crash or behave undefined.** Drafts and incomplete designs are permitted.

**Hard errors (block save/publish):**
1. Edge references a non-existent `NodeId`.
2. Edge references a non-existent `PortId` on its node.
3. Basic shape: `WorkflowRefId` set, `Version >= 1`, etc.

**Soft warnings (designer UI hint, do not block save):**
- Zero trigger nodes (workflow can never start).
- Orphan nodes (no inbound or outbound edges — will never execute).
- Trigger node with no outbound edges (runs start and immediately complete).
- Action node with all output ports unwired.

**Runtime behavior with warnings:**
- Orphan nodes are ignored — not in the executor's reachable set.
- Unwired output ports just terminate the branch when activated (no error, no log noise).
- Definitions with zero triggers stay in storage but never start a run.

**Cycle detection:** *not* enforced — cycles are allowed by design.

### 1.9 Credits (authoring layer)

Each node execution consumes credits. Credits double as a billing primitive **and** as a safety net for runaway cycles.

- **No `creditCost` field on the Node or Definition.** The UI never exposes a credits knob.
- The host has a system-wide `ICreditCostCalculator`:
  ```
  decimal Compute(string nodeKind, NodeConfig config)
  ```
- Default implementation returns a flat per-kind baseline. Config-aware logic (e.g., webhook timeout multiplier) can be added without touching any definition.
- The billing model (workspace credit pool, reservation, refund-on-failure) is deferred to a separate design pass.

### 1.10 Worked example — greenhouse workflow

Demonstrates how Nodes, Ports, Edges, and SharedVariables compose. Everything below is pure data.

```jsonc
{
  "workflowRefId": "...",
  "version": 1,
  "name": "Vent control + escalation",
  "sharedVariableSchema": [
    { "name": "smsToday", "type": "Counter", "default": 0, "resetPolicy": "DailyAtUtc(02:00)" }
  ],
  "nodes": [
    { "nodeId": "T",   "kind": "trigger:device",
      "config": { "deviceRef": "sensor-A", "event": "telemetry",
                  "correlationKey": "$trigger.deviceId",
                  "concurrencyPolicy": "CancelExisting" } },
    { "nodeId": "G1",  "kind": "control:logic",
      "config": { "condition": "$trigger.temperature > 35" } },
    { "nodeId": "OV",  "kind": "action:command",
      "config": { "deviceRef": "$trigger.deviceId", "command": "OpenVent" } },
    { "nodeId": "D",   "kind": "control:delay",
      "config": { "duration": "00:10:00" } },
    { "nodeId": "G2",  "kind": "control:logic",
      "config": { "condition": "$trigger.temperature > 35" } },
    { "nodeId": "SMS", "kind": "action:notify",
      "config": { "channel": "sms", "to": "ops@x", "body": "Vent didn't cool" } },
    { "nodeId": "INC", "kind": "control:variable",
      "config": { "scope": "shared", "op": "increment", "var": "smsToday" } },
    { "nodeId": "G3",  "kind": "control:logic",
      "config": { "condition": "$shared.smsToday >= 2" } },
    { "nodeId": "CALL","kind": "action:notify",
      "config": { "channel": "phone", "to": "ops@x" } }
  ],
  "edges": [
    { "from": ["T",  "out"],    "to": ["G1",  "in"] },
    { "from": ["G1", "true"],   "to": ["OV",  "in"] },
    { "from": ["OV", "out"],    "to": ["D",   "in"] },
    { "from": ["D",  "out"],    "to": ["G2",  "in"] },
    { "from": ["G2", "true"],   "to": ["SMS", "in"] },
    { "from": ["SMS","out"],    "to": ["INC", "in"] },
    { "from": ["INC","out"],    "to": ["G3",  "in"] },
    { "from": ["G3", "true"],   "to": ["CALL","in"] }
  ]
}
```

---

## Section 2 — Runtime Layer

This section defines what happens when a trigger fires.

### 2.1 Run

```text
Run {
  RunId: Guid
  WorkflowRefId: Guid
  Version: int                    // pinned at start
  WorkspaceId: int
  CorrelationKey: string?         // computed from trigger
  TriggerContext: TriggerContext  // the raw event that started this run
  Status: RunStatus               // Pending | Running | Completed | Failed | PartiallyFailed | Cancelled | Faulted | OutOfCredits
  StartedAt, FinishedAt
  CreditBudget: decimal           // pulled from workspace quota at start
  CreditsConsumed: decimal        // atomically incremented
  ActiveBranchCount: int          // atomic; reaches 0 → run terminality decided
}
```

- **Born** when a trigger fires and ConcurrencyPolicy permits creation.
- **Dies** when `ActiveBranchCount` reaches 0 and no pending bookmarks/joins remain.
- **Isolated**: RunState is private. Two Runs share *only* SharedVariables (and only via atomic ops).
- `RunId` is the universal handle for every external API (`Cancel`, `GetHistory`, `Resume`) and is carried in every history event and bookmark.

### 2.2 Branch

A concurrent execution path within a Run.

```text
Branch {
  BranchId: Guid
  RunId: Guid
  ParentBranchId: Guid?           // null for root, set for forks
  CurrentNodeId: Guid
  Status: BranchStatus            // Active | Waiting | WaitingAtJoin | Completed | Failed | Cancelled
  LocalVariables: Dict<string, object?>
  LastOutput: object?
  ForkCohortId: Guid?             // tag from a ParallelForEach fan-out, used by Join (see 2.6)
  CreatedAt
}
```

A Run starts with one Branch (at the matched trigger). Fan-out creates more. A Branch is **logical** — its execution can be suspended (Bookmark) and rehydrated later on a different thread (possibly different host in the future).

**Fork semantics:** when a node activates multiple output ports, edge 0 continues on the current Branch; additional edges spawn new Branches that copy `LocalVariables` and `LastOutput` from the parent. After the fork, branches share **nothing** mutable.

**Join semantics:** the `Join` node waits for N branches to arrive. Arriving branches transition to `WaitingAtJoin`; when the cohort is complete, the engine spawns **one** fresh continuation branch leaving Join's `"out"` port.

### 2.3 BranchContext

The **only** thing a node executor sees. Built fresh for each `ExecuteAsync` call; thrown away when the call returns.

```text
BranchContext {
  Run:           RunHandle              // RunId, WorkflowRefId, CorrelationKey, attempt
  Branch:        BranchHandle           // BranchId, ParentBranchId
  TriggerPayload: object                // immutable, from Run
  LastOutput:    object?                // mirrors Branch.LastOutput
  Local:         ILocalScope            // reads/writes → Branch.LocalVariables
  Run state:     IRunScope              // reads/writes → Run.RunState
  Shared:        ISharedScope           // atomic ops → workflow-scoped SQL store
  Cancellation:  CancellationToken      // engine-supplied, NOT persisted
  Logger:        ILogger                // pre-enriched with Run/Branch/Node ids
  Expressions:   IExpressionEvaluator   // engine-supplied
}
```

### 2.4 Why Branch and BranchContext are separate

- **`Branch` is state.** Engine-owned; persisted in snapshots; survives across executions.
- **`BranchContext` is an API surface.** Built per-execution; not persisted; includes ambient services (Logger, CancellationToken) that have no meaning across a Bookmark.

The split is load-bearing for four reasons:
1. **Persistence boundary** — Branch is snapshotted; CancellationToken/ILogger cannot be serialized.
2. **Lifetime mismatch** — Branch lives for the life of the branch (days, across restarts); BranchContext lives for one `ExecuteAsync` call (milliseconds).
3. **Mutation discipline** — `ctx.Local.Set(...)` goes through controlled API (scope rules, history events, atomic ops for Shared) and lands in the Branch's storage.
4. **Author-facing API** — node implementors only ever see BranchContext. Branch is engine-private; can be refactored without breaking any node.

Same pattern Elsa uses (`ActivityExecutionContext` vs persisted activity state) and Temporal uses (workflow context vs history).

### 2.5 State scopes

Four scopes with different visibility, lifetime, and concurrency stories:

| Scope | Visible to | Lifetime | Concurrency | Backing |
|---|---|---|---|---|
| `TriggerPayload` | All branches of this Run | Run | Immutable | Captured at Run start |
| `Local` | This Branch only | Branch (forks copy) | Single-threaded by definition | In-memory dict, snapshotted |
| `Run` | All branches of this Run | Run | Last-writer-wins (locked) | In-memory dict, snapshotted |
| `Shared` | All Runs of this WorkflowRefId | Workflow (across versions) | Atomic ops only | SQL row(s) per declared shared var |

A node never sees: other branches, other runs, other workflows. The runtime is the only bridge.

### 2.6 NodeExecutionResult — discriminated union

```text
NodeExecutionResult = one of:

  Continue {
    ActivatedPortIds: string[]            // 1 = inline, N = fan-out (same data each fork)
    Output: object?                       // becomes the next branch's LastOutput
  }

  Fork {
    PortId: string                        // the port the forks leave through
    Forks: ForkSpec[]                     // N forks, each with its own seed state
  }
  ForkSpec {
    LocalOverrides: Dict<string, object?> // e.g. { item: "device-A", index: 0 }
    Output: object?
  }

  WaitForBookmark {
    WakeCondition: WakeCondition          // engine persists a Bookmark, releases the thread
  }

  Fail {
    Error: NodeError                      // message, type, retryable flag
  }                                       // engine applies RetryPolicy → OnFailure

  Terminal { }                            // this branch ends here, successfully
```

Why discriminated, not a flag bag:
- Forces nodes to pick exactly one outcome — no "succeeded AND waiting" ambiguity.
- The Branch Loop becomes a clean switch — no flag precedence rules.
- Adding new outcomes (e.g., `RaiseSignal`) is a new union member, not a new optional field.

**V2's inline `Delay` is gone.** Any wait, no matter how short, goes through `WaitForBookmark` (the bookmark scheduler can choose to keep very short waits in memory as an optimization — that's an internal detail).

### 2.7 INodeExecutor

```text
interface INodeExecutor {
  string Kind { get; }
  Task<NodeExecutionResult> ExecuteAsync(Node node, BranchContext ctx);
}
```

- Registered **by Kind string**, not CLR type — definitions are JSON, plugin-extension-friendly.
- Owns its retry loop **internally** per node's RetryPolicy. Engine sees the outcome only after retries exhaust.
- Resolved per-execution from a service scope (can take scoped dependencies — SqlConnection, IHttpClientFactory, etc.).

### 2.8 Sequential ForEach — the back-edge pattern

A `ForEachNode` has two output ports: `"body"` and `"done"`. The graph topology has a back-edge from the body's tail to the ForEach node itself:

```text
... ──► ForEach ──body──► [Body nodes...] ──out──► (edge back to ForEach)
            │
            └──done──► [continuation...]
```

The executor manages an iterator counter in `BranchContext.Local` under a node-keyed entry (e.g., `__foreach:<NodeId>:index`):
- More items → set `$local.item` and `$local.index`, increment counter, return `Continue { ports: ["body"] }`.
- No more items → clear iterator state, return `Continue { ports: ["done"] }`.

**ForEach is just an ordinary node from the engine's perspective.** The cleverness is the back-edge plus stateful executor logic. This is why cycles must be allowed in the graph.

### 2.9 ParallelForEach + Join

**ParallelForEach** fans out N branches concurrently, each with a different `$local.item`. Uses the new `Fork` result variant. Each fork carries a `ForkCohortId` (Guid generated at fork time) on its Branch record.

**Join** is the cohort barrier:
- Arrival → branch transitions to `WaitingAtJoin`. Engine tracks per-cohort arrivals on the Run. The branch is logically "done its work" but still counted as part of the Run.
- When the cohort is complete → engine transitions all N waiting branches to `Completed` (each decrementing `ActiveBranchCount`) and spawns one continuation branch leaving `"out"` (incrementing by 1). The continuation gets empty `LocalVariables` and `LastOutput` set to the collected outputs as an array.
- `ActiveBranchCount` timeline for a cohort of K: fork emits `+K` at fan-out; arrivals at Join do **not** decrement; cohort completion decrements `-K` (waiting → Completed) and increments `+1` (continuation), net `-K+1`.

Designer enforces pairing: every `ParallelForEach` must have a matching `Join` in its downstream subgraph; validator can compute this at publish time. Stray (untagged) branches arriving at Join are rejected with a clear error.

### 2.10 Branch Loop (pseudocode)

```text
loop:
  node ← lookup(definition, branch.CurrentNodeId)
  if not found → branch ends (Completed, no edges)

  cost   ← creditCalculator.Compute(node.Kind, node.Config)
  check  ← run.CreditsConsumed + cost > run.CreditBudget
  if check → run → OutOfCredits, cancel siblings, exit

  emit HistoryEvent.NodeStarted
  executor ← registry[node.Kind]
  ctx      ← buildContext(run, branch)
  result   ← executor.ExecuteAsync(node, ctx)          // retries happen inside
  charge credits, snapshot run, emit completion event

  switch result:

    Continue { ports, output }:
      branch.LastOutput ← output
      edges ← edgesLeaving(node, ports)
      if edges = []         → branch ends (Completed)
      if edges = [e]        → branch.CurrentNodeId ← e.target.NodeId; loop
      else                  → fork (edges - 1) new branches with copied state;
                              continue inline with edges[0]

    Fork { portId, forks }:
      edge ← uniqueEdgeLeaving(node, portId)
      cohortId ← newGuid()
      for each f in forks:
        spawn Branch { parent=branch, CurrentNodeId=edge.target.NodeId,
                       LocalOverrides applied, LastOutput=f.Output, ForkCohortId=cohortId }
      end branch (Completed — its job was to fork)

    WaitForBookmark { cond }:
      persist Bookmark { runId, branchId, nodeId, cond }
      branch.Status ← Waiting; persist snapshot
      return                                            // thread released

    Fail { error }:
      apply OnFailure:
        FailBranch → end branch Failed
        FailRun    → cancel siblings, run Failed
        Continue   → end branch Completed (no activated ports)
        Compensate → enqueue compensation node; end branch Failed

    Terminal:
      branch ends (Completed)
```

### 2.11 Branch.Status — first-class enum

```text
BranchStatus = Active | Waiting | WaitingAtJoin | Completed | Failed | Cancelled
```

Persisted on Branch. Used by:
- **Run aggregation**: every branch in `{Completed, Failed, Cancelled}` → finalize RunStatus.
- **Cancellation**: find all branches with `Status ∈ {Active, Waiting, WaitingAtJoin}` for this Run; cancel/delete bookmarks; signal CancellationTokens.
- **Restart recovery**: branches with `Status = Active` in storage but no live dispatcher entry were mid-execution at crash time; re-dispatch them.

### 2.12 Inline-first fan-out

When `Continue { ports }` resolves to N edges:
- Edge 0 continues inline on the current thread (no dispatcher round-trip).
- Edges 1..N-1 are pushed to the dispatcher.

Inline-first wins because workflows have long linear stretches; the alternative round-trips every node through the dispatcher. No semantic difference — both branches still run concurrently — just less overhead.

### 2.13 Cooperative cancellation

The engine owns a `CancellationTokenSource` per Run. The token flows into every `BranchContext` and naturally into node executor calls (`HttpClient.GetAsync(url, ctx.Cancellation)`, etc.).

On `CancelRun(runId)`:
1. Engine flips the Run's CTS.
2. Executing branches' `BranchContext.Cancellation` fires `OperationCanceledException`.
3. The Branch Loop catches it, transitions the branch to `Cancelled`, persists.
4. Bookmarks/joins for that Run are deleted.

Stuck nodes that ignore the token are not killed (no thread-abort). The branch shows `Active` with a `CancellationRequestedAt` timestamp; ops dashboards can flag long-running unresponsive branches.

### 2.14 Shared variable atomic ops

`ISharedScope` exposes a typed API, not a generic `Get/Set`:

```text
ISharedScope:
  Task<long>   Increment(var, delta = 1, ct)
  Task<long>   Decrement(var, delta = 1, ct)
  Task<T?>     Get<T>(var, ct)
  Task         Set<T>(var, value, ct)                      // last-writer-wins
  Task<bool>   CompareAndSet<T>(var, expected, newValue, ct) // optimistic CAS
```

Backing: one SQL row per `(WorkflowRefId, VarName)` with a typed value column. Counter ops → atomic `UPDATE ... SET value = value + @delta`. Authors don't write retry loops.

`ResetPolicy` is implemented by a separate scheduled job operating on the store directly — not via a workflow run.

### 2.15 ActiveBranchCount and Run terminality

The Run row owns an atomic `ActiveBranchCount`:
- **+1** when a branch is created (root, fork, join continuation).
- **-1** when a branch reaches `Completed`, `Failed`, or `Cancelled`.
- **No change** for `Waiting` or `WaitingAtJoin`.

When `ActiveBranchCount = 0` AND there are no pending Bookmarks/Joins for this Run → the engine aggregates branch statuses into `RunStatus` and marks the Run terminal.

The decrement is an atomic SQL `UPDATE ... SET ActiveBranchCount = ActiveBranchCount - 1 OUTPUT inserted.ActiveBranchCount` so the "who completes last" race is decided by the database — important when we go multi-host.

---

## Section 3 — Bookmarks (outline only)

To be detailed.

- `Bookmark { BookmarkId, RunId, BranchId, NodeId, WakeCondition, CreatedAt }` persisted in SQL.
- `WakeCondition` variants: `Timer(at) | Signal(name, correlationData) | DevicePropertyChange | DeviceTelemetryMatch | Manual(token) | ChildRunCompleted(childRunId)`.
- **BookmarkScheduler** — background service polling due Timer bookmarks; single-host today, promotable later.
- **BookmarkResumer** — routes incoming signals/device events to bookmarks (resume path), distinct from TriggerDispatcher (start-new-run path).
- Resume rehydrates the branch from snapshot and re-dispatches it.

## Section 4 — Triggers & inbound (outline only)

To be detailed.

- Trigger kinds: `trigger:device` (telemetry, property change), `trigger:schedule` (CRON), `trigger:webhook`, `trigger:manual`.
- `TriggerKey` (e.g., `client:<deviceRefId>`, `webhook:/orders/kochi`, `timer:system`).
- `TriggerDispatcher` resolves matching trigger nodes via the runtime registry, computes `CorrelationKey`, applies `ConcurrencyPolicy`, creates new Runs.

## Section 5 — Error model and run lifecycle (outline only)

To be detailed.

- `RetryPolicy` (per node — attempts, backoff, retryable filters; long waits between retries promote to Bookmark).
- `OnFailure` (`FailBranch` default | `FailRun` | `Continue` | `Compensate(nodeId)`).
- `RunStatus` aggregation (`Completed | Failed | PartiallyFailed | Cancelled | Faulted | OutOfCredits`).
- `CancelRun(runId, reason)` API.
- `IdempotencyKey` per action invocation, persisted before invoking.

## Section 6 — Durability and versioning (outline only)

To be detailed.

- `RunSnapshot` — full run state after each node completion.
- `HistoryEvent` — append-only log; powers audit and observability.
- `WorkflowVersionStore` — every published definition version persisted; in-flight runs reference theirs.
- Version GC — eligible when zero runs reference AND not the latest.

## Section 7 — Engine infrastructure (outline only)

To be detailed.

- `WorkflowEngine` (top-level service): owns TriggerDispatcher, BookmarkScheduler, RunDispatcher, RuntimeRegistry, persistence services.
- `RuntimeRegistry`: in-memory `(WorkflowRefId → latest Version)` for new triggers; `(WorkflowRefId, Version) → definition` for resumes.
- `IRunDispatcher`: interface; V1 impl = in-process `Channel<BranchPointer>`; future = SQL-backed `SELECT … FOR UPDATE SKIP LOCKED`.
- `NodeExecutorRegistry`: strategy registry keyed by node-kind string.
- `ExpressionEvaluator`: root paths `$trigger`, `$state`/`$run`, `$local`, `$output`, `$shared`.
- EventBus integration: `HistoryEvent`s also published to RabbitMQ (`Wbskt.Events.WorkflowEngine`) for external observability dashboards.

---

## Glossary (quick reference)

| Term | One-line definition |
|---|---|
| WorkflowDefinition | Immutable, versioned blueprint `(WorkflowRefId, Version, Nodes, Edges, SharedVarSchema)`. |
| Node | A unit in the graph — pure data with a `Kind` discriminator; behavior lives in `INodeExecutor`. |
| Port | A named exit on a node. |
| Edge | A directed wire `(srcNode, srcPort) → (tgtNode, tgtPort)`. |
| Run | One isolated execution of a definition, started by one trigger event. Has `RunId`, status, history. |
| Branch | A concurrent execution path within a Run. Has `BranchId`, position, status, local vars. |
| BranchContext | The per-execution API surface handed to a node executor; bridges to Branch/Run/Shared state. |
| TriggerPayload | The raw event that started the Run; immutable. |
| LocalVariables | Branch-private k/v dict; copied on fork. |
| RunState | Run-private k/v dict; shared across branches of one Run. |
| SharedVariables | Workflow-scoped store of declared, typed vars; atomic ops only. |
| Bookmark | Durable suspension point `(RunId, BranchId, NodeId, WakeCondition)`. |
| WakeCondition | What wakes a Bookmark: Timer / Signal / DevicePropertyChange / DeviceTelemetryMatch / Manual / ChildRunCompleted. |
| Trigger | A node that starts a Run when its external condition is met. |
| TriggerKey | String used to fan inbound events to matching trigger nodes (e.g. `client:<guid>`). |
| CorrelationKey | Expression value used to scope `ConcurrencyPolicy`. |
| ConcurrencyPolicy | AllowParallel / Queue / CancelExisting / DropIfRunning, per correlation key. |
| RetryPolicy | Per-node attempts/backoff/retryable-filter config; retries inside the node executor. |
| OnFailure | What the branch does after retries exhaust: FailBranch / FailRun / Continue / Compensate. |
| RunStatus | Completed / Failed / PartiallyFailed / Cancelled / Faulted / OutOfCredits. |
| BranchStatus | Active / Waiting / WaitingAtJoin / Completed / Failed / Cancelled. |
| HistoryEvent | Append-only audit/observability event. |
| RunSnapshot | Persisted state of a run at a moment in time. |
| Credits | Per-node-execution cost; safety net + billing primitive. |
| Fork | A `NodeExecutionResult` variant that creates N branches with per-branch seed state (ParallelForEach uses this). |
| Join | A node that gates on a cohort of forked branches arriving; emits one continuation branch. |
| ForkCohortId | Guid tag on a Branch identifying which fan-out it belongs to; used by Join. |
| Sub-workflow | A child Run kicked off by the parent; parent waits via `ChildRunCompleted` bookmark. |
