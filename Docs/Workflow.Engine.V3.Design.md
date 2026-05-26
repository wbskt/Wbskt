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

## Section 3 — Bookmarks

Section 1 was *what the user designs*. Section 2 was *what happens between nodes*.
Section 3 is *what happens **at** a node that wants to wait* — and the inbound
routing that wakes those waits.

Every long-running, durable, mission-critical scenario depends on this section.
The V2 pattern of holding a `Task.Delay` in memory is gone; even short waits go
through a Bookmark.

Four concepts: **Bookmark → WakeCondition → BookmarkScheduler → BookmarkResumer**,
plus the **inbound routing** that funnels every external event through them.

### 3.1 Bookmark — the durable suspension record

A Bookmark is a row in SQL that says "this branch is sleeping at this node, and
here is what should wake it up". When the engine sees a `WaitForBookmark`
result from an executor, it creates one of these rows and **releases everything**
— the executing thread, the BranchContext, the in-memory branch state — except
what is already snapshotted on the Branch row.

```text
Bookmark {
  BookmarkId:        Guid             // engine-generated; the wake handle
  RunId:             Guid             // parent run (FK)
  BranchId:          Guid             // which branch to resume (FK)
  NodeId:            Guid             // the node that is waiting; resume re-enters here
  WorkflowRefId:     Guid             // denormalized for fast lookup
  Version:           int              // denormalized; pinned with the run
  WakeCondition:     WakeCondition    // polymorphic; how this bookmark gets woken
  MatchKey:          string?          // computed lookup key (see 3.2); null for Timer
  TimeoutAt:         DateTime?        // optional; null = no timeout
  OnTimeoutPortId:   string?          // which port to take if timeout fires first
  CreatedAt:         DateTime
  // Indexed by: (RunId), (MatchKey WHERE NOT NULL), (TimeoutAt WHERE NOT NULL)
}
```

Non-obvious points:

- **`NodeId` is where we resume, not the next node.** Resume re-enters the
  same node that requested the wait. The executor inspects the resumed branch
  state and decides which port to take (typically `Continue { ports: ["completed"] }`).
  This keeps the wake/decide/port logic inside the executor where it belongs.

- **Bookmark holds no payload.** When a signal arrives, its payload is written
  onto the Branch (`Branch.LastOutput` or a known `Local` key) and then the
  bookmark is consumed. The bookmark is metadata about the wait, not a buffer.

- **`TimeoutAt` is a first-class column**, not nested inside `WakeCondition`.
  The Scheduler polls `WHERE TimeoutAt <= now`; pulling the column out avoids
  per-row JSON parsing on every poll. See 3.5 for why this is also the unified
  TTL race mechanism.

- **`WorkflowRefId` / `Version` denormalized** so resumes do not need to JOIN
  through Run to know which definition to load. Hot-path optimization.

### 3.2 WakeCondition — the six variants

`WakeCondition` is a polymorphic JSON column on the Bookmark row. Each variant
contributes a string `MatchKey` (computed at bookmark creation) which is what
the Resumer indexes on.

#### `Timer(at: DateTime)`

Simplest. Wakes at an absolute time. Used by `DelayNode` (relative duration
converted to absolute) and by any "schedule retry attempt at" inside a node's
retry loop when the wait is long.

- `MatchKey = null` — timers are polled by `TimeoutAt`; no Resumer lookup needed.

#### `Signal(name: string, correlation: string)`

External event with a stable name and a correlation identifier. Used by
`AwaitSignal` (`name="operator-ack", correlation="<runId>"`) and by
`WaitForHttp` (`name="http", correlation="<minted token>"`).

- `MatchKey = "signal|{name}|{correlation}"` — O(1) index hit on the Resumer's
  hot path.

#### `DevicePropertyChange(deviceRefId: Guid, propertyName: string?)`

Wakes when a device's reported property changes. `propertyName = null` means
"any property". Used by control nodes that wait for the side-effect of an
action sent to a device (e.g., "send OpenVent → wait for `state.vent == open`").

- `MatchKey = "prop|{deviceRefId}|{propertyName ?? "*"}"`
- Resumer on a property-change event checks **both** the exact key and the
  device-wildcard key (`prop|{deviceRefId}|*`). Two index hits, cheap.

#### `DeviceTelemetryMatch(deviceRefId: Guid, propertyName: string, value: JsonValue)`

Wakes when a device publishes telemetry where `propertyName == value` (equality
comparison).

- `MatchKey = "telemetry|{deviceRefId}|{propertyName}|{value}"` — pure index hit;
  no per-bookmark expression evaluation.
- **v1 restriction (locked):** equality only. Full expression matching
  (`$trigger.temperature < 30`) is deferred. If it becomes necessary later, we
  add a second variant `DeviceTelemetryExpression(deviceRefId, expression)`
  with a coarser MatchKey (`"telemetry|{deviceRefId}"`) and per-bookmark eval.
  Keeping v1 equality-only means inbound telemetry routing stays O(1).

#### `Manual(token: string)`

A URL-callable wake handle. The token is an opaque Guid. The Resumer matches
HTTP POSTs to `/wake/{token}` against this. Used by `WaitForHttp` (anonymous,
self-minted token) and approval-style flows where ops gets an emailed link.

- `MatchKey = "manual|{token}"`
- Tokens are **one-shot Guids minted per wait-node execution; never reused**.
  This is the idempotency boundary (see 3.6).

#### `ChildRunCompleted(childRunId: Guid)`

Wakes when a specific sub-workflow run reaches a terminal status. Used by
`SubWorkflowNode` — parent posts this bookmark, child finishes, engine fires it.

- `MatchKey = "child|{childRunId}"`
- The wake is fired internally by the engine when the child terminates, fed
  through the same Resumer to keep the code path uniform.

### 3.3 BookmarkScheduler — the timer service

A single background service per host. Its job is narrow: **wake bookmarks whose
`TimeoutAt` has passed**.

```text
Loop forever:
  rows ← SELECT TOP 100 BookmarkId, RunId, BranchId, NodeId,
                       WakeCondition, OnTimeoutPortId
          FROM Bookmarks
          WHERE TimeoutAt <= SYSUTCDATETIME()
          ORDER BY TimeoutAt ASC

  for each row:
    if WakeCondition is Timer:
      // primary wake — the timer IS the wake condition
      Resumer.ResumeViaBookmark(row, payload=null, takingPort=null)
    else:
      // TTL companion — timeout raced the real condition and won
      Resumer.ResumeViaBookmark(row, payload=null, takingPort=row.OnTimeoutPortId)

  if rows empty:
    sleep min(time-to-next-TimeoutAt, 1s)
```

Design points:

- **One service per host today.** When we move to leader/standby (decision #8),
  a SQL lease decides which host runs the scheduler; others sit idle.
- **Batch-claim semantics.** Each poll claims rows with `UPDATE … OUTPUT … WHERE …`
  or equivalent (`SELECT … WITH (UPDLOCK, READPAST)` on SQL Server). Even
  briefly-overlapping schedulers cannot double-process a row.
- **No precision claims.** "Wait 1 hour" means "wake roughly 1 hour from now,
  ± poll interval (~1s)". IoT flows do not need sub-second timer precision.
- **Scheduler never calls executors directly** — it goes through
  `Resumer.ResumeViaBookmark`, same path as event-driven wakes. One resume path.

### 3.4 BookmarkResumer — event router and resume entry point

Two responsibilities:

1. **Inbound event routing** (`MatchInbound`) — incoming external events are
   matched against bookmarks. If a match exists, resume. If not, fall through
   to the TriggerDispatcher.
2. **Bookmark consumption + branch rehydration** (`ResumeViaBookmark`) — once
   a bookmark is claimed (by either path), this is what actually resumes the
   branch.

#### 3.4.1 `MatchInbound` — the routing decision

```text
function MatchInbound(event):
  // event has: kind, matchKey(s), payload, deviceId?
  // e.g. { kind: "signal", matchKey: "signal|operator-ack|R-42", payload: {…} }

  // 1) Look up live bookmarks
  bookmarks ← SELECT * FROM Bookmarks WHERE MatchKey IN (event.matchKeys)

  if bookmarks not empty:
    for each b in bookmarks:
      ResumeViaBookmark(b, payload=event.payload, takingPort=null)
    return  // *** do NOT fall through to triggers ***

  // 2) Fall through to trigger path
  TriggerDispatcher.Dispatch(event)
```

This is **decision #3 ("uniform shape, separate code paths") made concrete**.
The Resumer owns the bookmark-or-trigger decision. The TriggerDispatcher never
has to know that bookmarks exist; it is called only when no bookmark claimed
the event.

**Locked rule (Q2):** *match wins, no fall-through.* An event that wakes any
bookmark cannot also start a new run from the same event. This is the safe
default: it prevents a generic catch-all trigger (e.g., `webhook path="*"`) from
accidentally activating on every callback-URL invocation. If a workflow truly
wants to both wake AND start a new run from the same event, that is modeled
explicitly with an action node inside the workflow, not implicitly by sibling
triggers.

The symmetric rule:
- Bookmark match → resume → DONE; triggers are not consulted.
- No bookmark match → triggers are consulted; may or may not start a new run.

#### 3.4.2 `ResumeViaBookmark` — the resume itself

```text
function ResumeViaBookmark(bookmark, payload, takingPort):

  // 1) Atomically claim/delete the bookmark
  rows ← DELETE FROM Bookmarks
         OUTPUT deleted.*
         WHERE BookmarkId = @bookmark.BookmarkId

  if rows empty:
    return  // somebody else already consumed it; drop silently

  // 2) Load run + branch state
  run    ← LoadRun(bookmark.RunId)
  branch ← LoadBranch(bookmark.BranchId)

  // 3) Check run is still runnable
  if run.Status != Running:
    return  // run got cancelled/failed while we were sleeping; drop

  // 4) Reattach payload to branch state
  if payload != null:
    branch.LastOutput = payload
  if takingPort != null:
    // Timeout path: skip the executor, jump straight to next edge resolution
    branch.PendingTakePort = takingPort
  branch.Status = Active

  // 5) Persist and re-dispatch
  PersistBranch(branch)
  RunDispatcher.Dispatch(new BranchPointer {
      RunId    = bookmark.RunId,
      BranchId = bookmark.BranchId,
      NodeId   = bookmark.NodeId
  })
```

Subtle points:

- **Atomic `DELETE … OUTPUT`** is the claim. If the row is gone, somebody else
  got there first; this call returns zero rows and drops cleanly. The
  Scheduler and the Resumer race for the same row when a TTL fires "at the
  same time" as the real signal — exactly one wins.
- **`takingPort` separation.** Regular wake (the wait condition fired)
  re-enters the node so it can decide. Timeout wake skips the node entirely;
  the engine just walks `OnTimeoutPortId`. The node executor's happy path is
  not muddied with timeout branches.
- **Run-status guard** is cheap insurance for cancellation races.

### 3.5 TTL companion timers — the unified race

Every Bookmark carries `TimeoutAt` + `OnTimeoutPortId` as first-class columns,
not as a sibling Timer bookmark row. **One bookmark per wait, regardless of
whether a TTL is set.** (This was Q1; chosen over the sibling-rows alternative.)

For `AwaitSignal { name:"operator-ack", ttl:1h, onTimeout:"timeout" }`, the
engine creates one row:

```text
Bookmark {
  WakeCondition:   Signal { name:"operator-ack", correlation:"R-42" }
  MatchKey:        "signal|operator-ack|R-42"
  TimeoutAt:       now + 1h
  OnTimeoutPortId: "timeout"
}
```

Two paths can wake it:

- HTTP signal arrives → Resumer.MatchInbound → `DELETE WHERE BookmarkId = …`
  → 1 row returned → resume via `Continue { ports:["completed"] }` from the
  executor.
- Clock hits TimeoutAt → Scheduler poll → `DELETE WHERE BookmarkId = …`
  → 1 row returned → resume via `takingPort = "timeout"` (Scheduler passes it).

The single row can only be DELETEd once. Whichever side gets the row first
wins; the other gets zero rows and drops cleanly. **One row, one DELETE, one
atomic race.**

For a pure `DelayNode { duration:10m }`:

```text
Bookmark {
  WakeCondition:   Timer { at: now + 10m }
  MatchKey:        null            // timer only; Resumer never looks up
  TimeoutAt:       now + 10m       // duplicate of Timer.at, denormalized
  OnTimeoutPortId: null            // null = regular resume, executor decides
}
```

Yes, `TimeoutAt` duplicates `Timer.at` for this case (~10 bytes/row redundancy).
The alternative — JSON-parsing `WakeCondition` on every Scheduler poll — is far
more expensive. The duplication is the right trade.

For `AwaitSignal` with no timeout: `TimeoutAt = null`; Scheduler never sees it;
only the Resumer can wake it.

**Net effect:** one table, one DELETE-with-OUTPUT semantic, one resume entry
point — handles every wait pattern in the engine.

#### Why not sibling-row TTLs?

The alternative would be: a Signal bookmark + a Timer bookmark pointing at the
same Branch/Node, racing each other. Pros: naturally extensible to "wait for
any of N conditions". Cons: every wake must transactionally delete its
siblings; introduces a grouping concept; doubles row count per wait; more
failure modes.

Our entire node catalog (`Delay`, `WaitForHttp`, `AwaitSignal`,
`WaitForDeviceProperty`, `WaitForChildRun`) has at most one happy-path
condition + optional timeout. Sibling-row flexibility is theoretical, not
utilized. If we ever need true N-way races, we add siblings *then* as an
opt-in pattern next to the single-row pattern.

### 3.6 Idempotency for duplicate inbound signals

Locked: **one-shot tokens are the idempotency boundary; no explicit dedup
window.**

Scenario: a flaky HTTP caller retries `POST /wake/T1` three times in 50ms
because their socket timed out.

- Call #1 → Resumer.MatchInbound finds bookmark `bk-T1` → `DELETE` returns 1
  row → resume.
- Call #2 → Resumer finds zero bookmarks → falls through to TriggerDispatcher
  → no trigger matches `/wake/T1` → silently dropped.
- Call #3 → same as #2; dropped.

Already idempotent: duplicates are no-ops.

The one corner case is "could a freshly-posted bookmark reuse the same MatchKey
as a just-deleted one, so duplicate calls wake the wrong wait?" Answer: no,
because tokens (`Manual`/`WaitForHttp` correlations) are minted per node
execution as fresh Guids; reuse is impossible. For named signals
(`AwaitSignal name="operator-ack"`), the correlation is typically the RunId or
a per-node Guid, which is also non-reusable across a wait re-arming.

If a future use case needs to reuse a stable correlation key across rapid
re-arming (rare), we add a small in-memory LRU of consumed BookmarkIds; not a
v1 requirement.

### 3.7 Cancellation and bookmark cleanup

When `CancelRun(runId)` is invoked (Section 5 territory, but the bookmark
mechanics belong here):

```text
1) Run.Status ← Cancelling
   Flip the Run-level CancellationTokenSource
   (cooperative cancel for Active branches)

2) DELETE FROM Bookmarks WHERE RunId = @runId
   (indexed by RunId → cheap; all this run's bookmarks gone in one statement)

3) Branches in Waiting / WaitingAtJoin transition to Cancelled
4) Wait for Active branches to observe the CTS and transition
5) When ActiveBranchCount = 0:
     Run.Status ← Cancelled
     emit RunCancelledEvent
```

After step 2, any event that *would have* matched a deleted bookmark gets zero
rows from the Resumer → falls through to TriggerDispatcher → if no Trigger
matches either, dropped. Clean shutdown with no orphan wakes.

### 3.8 The full inbound routing diagram

This is the engine seen from the outside. Every inbound event — RabbitMQ
message, HTTP webhook, scheduler tick, child-run-completed notification —
funnels through this:

```text
                ┌─────────────────────────────────────────┐
                │             Inbound Event               │
                │  { kind, matchKey, payload, deviceId? } │
                └────────────────────┬────────────────────┘
                                     │
                                     ▼
                            ┌──────────────────┐
   timer tick ──────────────►   InboundHub     │
   signal HTTP ─────────────►  (normalizer:    │
   device telemetry ────────►  build matchKeys)│
   device prop change ──────►                  │
   child run completion ────►                  │
                            └────────┬─────────┘
                                     │
                                     ▼
                       ┌──────────────────────────┐
                       │   BookmarkResumer        │
                       │   .MatchInbound(event)   │
                       └───┬──────────────────┬───┘
                           │                  │
                  match    │                  │  no match
              (1+ rows)    ▼                  ▼
                   ┌──────────────┐    ┌────────────────────┐
                   │ for each b:  │    │ TriggerDispatcher  │
                   │ Resume       │    │ .Dispatch(event)   │
                   │ ViaBookmark  │    │ - resolve triggers │
                   │ (delete row, │    │ - eval correlation │
                   │  rehydrate,  │    │ - apply conc. pol. │
                   │  re-dispatch)│    │ - create new Run   │
                   └──────┬───────┘    └─────────┬──────────┘
                          │                      │
                          ▼                      ▼
                  ┌───────────────┐      ┌───────────────┐
                  │ RunDispatcher │      │ RunDispatcher │
                  │ (Branch Loop) │      │ (Branch Loop) │
                  └───────────────┘      └───────────────┘

  Out-of-band:
    BookmarkScheduler ─poll TimeoutAt─► ResumeViaBookmark (same path as above)
```

Two arrows leave the Resumer; never both. The arrow taken is decided by
whether there is a live bookmark with the event's MatchKey. **That is the
entire "is this a wake or a new run?" question.**

### 3.9 What this enables / what it costs

**Enables:**

- Long waits (days, weeks) with zero in-memory cost.
- Survive restarts: Scheduler picks up where it left off; bookmarks live in SQL.
- Approval / escalation / human-in-the-loop flows naturally.
- Sub-workflow waits (parent posts a `ChildRunCompleted` bookmark).
- The same routing path serves time-based waits AND event-based waits AND HTTP
  callbacks AND child-run completion — **one mechanism, six variants**.

**Costs:**

- Every wait pays one SQL INSERT + one DELETE per wait (~1ms each at moderate
  load).
- The Scheduler polls SQL on a 1-second cadence by default. Tunable trade-off:
  latency vs DB load.
- Very-short delays (< 100ms) carry SQL round-trip overhead. If this matters,
  an in-memory fast path for sub-second Timer bookmarks is an easy follow-on;
  not in v1.

### 3.10 The two new wait-style control nodes

These motivated the Q&A that opened Section 3; documenting them here for
completeness. Both belong to the `control:` node family.

#### `control:waitForHttp`

Anonymous mid-graph HTTP wait. Mints a one-shot callback URL on first
execution.

- **Config:** `{ ttl: duration, onTimeout: "timeout" | "fail" }`
- **Ports:** `completed`, `timeout`
- **Behavior:**
  1. On first execution: mint a fresh Guid `token`, compute
     `callbackUrl = $"https://wbskt/api/v1/workflows/wake/{token}"`, store on
     `$local.__callbackUrl` (available to downstream nodes via expression for
     emailing/templating).
  2. Return `WaitForBookmark { WakeCondition: Manual(token), TimeoutAt: now + ttl, OnTimeoutPortId: "timeout" }`.
  3. On resume via callback: payload is the request body; take port `completed`.
  4. On resume via timeout: take port `timeout` (Scheduler-driven, executor not
     re-entered).

#### `control:awaitSignal`

Named mid-graph signal wait. Useful when the caller knows a stable signal name
(e.g., `"operator-ack"`).

- **Config:** `{ signalName, correlation: <expression>, ttl: duration?, onTimeout: portId? }`
- **Ports:** `completed`, `timeout` (if `ttl` configured)
- **Behavior:** Evaluate the correlation expression against current branch
  state at execution time (typically `$run.runId`). Post
  `WaitForBookmark { WakeCondition: Signal(signalName, correlation), TimeoutAt?, OnTimeoutPortId? }`.

The two nodes share the same engine machinery; the difference is purely
authoring ergonomics. `waitForHttp` is for "I need a callback URL right now";
`awaitSignal` is for "I'm waiting for a known external event with a name I
chose".

### Section 3 locked decisions

- **One bookmark per wait** with first-class `TimeoutAt` + `OnTimeoutPortId`
  columns. Single-row atomic race. (Q1)
- **Resumer match wins; no fall-through to TriggerDispatcher on match.** An
  event is either a wake or a new run, never both. (Q2)
- **`DeviceTelemetryMatch` is equality-only in v1.** Full expression matching
  deferred behind a separate variant if needed. (Q3)
- **One-shot tokens are the idempotency boundary;** no explicit dedup window.
  (Q4)
- **Single Scheduler + Resumer per host** today; leader/standby with SQL lease
  later (decision #8).
- **Cancellation deletes all of a run's bookmarks in one indexed DELETE.**

## Section 4 — Triggers & Inbound

Sections 1–3 built up to this moment. We now know:

- A workflow definition contains **trigger nodes** (graph roots) as data in the
  JSON definition (Section 1).
- A trigger node has a `correlationKey` expression on its config.
- The Resumer hands events to the TriggerDispatcher only when no bookmark
  claimed them (Section 3).

Section 4 fills in the remaining half: **how does an inbound event find the
matching trigger node(s), and what happens between "match found" and
`Run.Status = Running`?**

Six concepts: **TriggerRegistration → TriggerKey → InboundHub event shapes →
TriggerDispatcher pipeline → CorrelationKey full mechanics → ConcurrencyPolicy
enforcement**, plus the v1 trigger families.

### 4.1 Trigger Node vs Trigger Registration — the two-tier model

This is the conceptual split that makes everything else efficient.

**Trigger Node** — a node in `WorkflowDefinition.Nodes`. Pure data. JSON. Lives
wherever the definition lives.

```jsonc
{
  "id": "n-trigger-1",
  "kind": "trigger:device",
  "config": {
    "deviceRefId": "dev-greenhouse-thermo-01",
    "propertyName": "temperature",
    "correlationKey": "$trigger.deviceId"
  },
  "ports": [{ "id": "out" }]
}
```

**Trigger Registration** — a row in a `TriggerRegistrations` SQL table created
when the workflow definition is published. The indexed lookup row that says
"this published workflow has this trigger armed".

```text
TriggerRegistration {
  RegistrationId:   Guid
  WorkflowRefId:    Guid
  Version:          int
  TriggerNodeId:    Guid         // which node in the definition
  Kind:             string       // e.g. "trigger:device"
  TriggerKey:       string       // computed at publish time (see 4.2)
  CorrelationExpr:  string?      // copied from node config; nullable
  ConcurrencyJson:  string       // policy as JSON
  Enabled:          bool         // disabled while paused/draining versions
  CreatedAt:        DateTime
  // Indexed by: (TriggerKey WHERE Enabled = 1), (WorkflowRefId)
}
```

Why two tiers:

- The TriggerDispatcher needs **O(1) lookup**: "given this inbound event, what
  trigger nodes match?" Searching every workflow definition's JSON on each
  event is impossible. The `TriggerRegistration` row with an indexed
  `TriggerKey` solves this.
- Publishing does the **expensive work once:** parse the definition, find all
  trigger nodes, compute their TriggerKeys, insert rows.
- Version pinning (decision #6) works naturally because the registration
  carries `Version`; an inbound event finds the right pinned definition.

**Lifecycle:**

- `PublishWorkflow(def)` → for each trigger node, INSERT a registration row.
- "Supersede with new version" → `UPDATE Enabled = 0` on prior-version rows.
- `DeleteWorkflow(refId)` → cascade-DELETE registrations (allowed once no live
  runs reference the workflow, per decision #6).

### 4.2 TriggerKey — the indexed lookup string

Computed at publish time from the trigger node's `kind` + config.
**Identity-shaped, not value-shaped** — does not depend on event payload.

| Trigger node | TriggerKey |
|---|---|
| `trigger:device { deviceRefId: D, propertyName: P }` | `device\|D\|P` |
| `trigger:device { deviceRefId: D }` (any property) | `device\|D\|*` |
| `trigger:webhook { path: "/water-start", method: "POST" }` | `webhook\|POST\|/water-start` |
| `trigger:schedule { cron: "0 6 * * *" }` | `schedule\|<workflowRef>\|<triggerNodeId>` |
| `trigger:signal { name: "ops-emergency" }` | `signal\|ops-emergency` |
| `trigger:event { topic: "billing.invoice.created" }` | `event\|billing.invoice.created` |

Two details:

- **Schedule triggers are workflow-scoped.** Each scheduled trigger is unique
  to its workflow; there is no "shared bus" of scheduled fires. The Ticker
  service (see 4.4) emits synthetic events with this exact key.
- **Wildcards.** `device|D|*` lets a trigger fire on any property change for a
  device. The InboundHub computes **both** `device|D|<propName>` and
  `device|D|*` as candidate match-keys; the Dispatcher looks up registrations
  matching any candidate key.

The TriggerKey is identity, not data. The `correlationKey` expression — which
**does** depend on event payload — is evaluated *after* TriggerKey lookup, not
as part of it. That separation is what keeps lookup O(1).

### 4.3 InboundHub event shapes

The InboundHub is the normalizer in front of `Resumer → TriggerDispatcher`.
Every inbound source has an adapter that produces a uniform event:

```text
InboundEvent {
  Kind:        string         // "device-property" | "device-telemetry" | "http"
                              // | "schedule" | "signal" | "event" | "child-run"
  MatchKeys:   string[]       // 1..N candidate keys
                              // (e.g. ["device|D|P", "device|D|*"])
  Payload:     JsonElement    // arbitrary; what triggers/wakes see as $trigger.*
  DeviceRefId: Guid?          // populated for device events; otherwise null
  Source:      string         // diagnostic — "rabbitmq", "http", "ticker", …
  ReceivedAt:  DateTime
}
```

`MatchKeys[]` (an array, not a single key) is what makes wildcards work
cleanly: a single device-property-change event produces two candidates and the
engine checks both against bookmarks AND against trigger registrations. One
inbound event, multiple potential matches, one lookup mechanism.

**Adapters (one per source):**

- `RabbitMqInboundAdapter` — consumes MassTransit topics (device-property,
  device-telemetry, child-run-completed, generic `event` topics).
- `HttpInboundAdapter` — ASP.NET Core endpoint. Wake URLs (`/wake/{token}`)
  produce `Kind: "http", MatchKeys: ["manual|{token}"]`. Webhook URLs
  (`/hooks/{path}`) produce `Kind: "http", MatchKeys: ["webhook|{method}|{path}"]`.
- `TickerInboundAdapter` — schedule-driven; emits synthetic events at cron fire
  times with `Kind: "schedule"`. (See 4.4 for the table backing it.)
- `SignalInboundAdapter` — for both internal (engine-emitted) and external
  (operator-emitted) signals; produces `Kind: "signal"`.

#### The Ticker service (the schedule adapter's backing store)

**Locked decision (Q7):** scheduled triggers are driven by a dedicated table
that mirrors the Bookmark pattern.

```text
ScheduledFire {
  ScheduledFireId:  Guid
  RegistrationId:   Guid       // FK → TriggerRegistration
  Cron:             string     // e.g. "0 6 * * *"
  NextFireAt:       DateTime   // computed from Cron + last fire time
  // Indexed by: (NextFireAt)
}
```

On `PublishWorkflow` with a scheduled trigger, the Ticker inserts a row with
the first computed `NextFireAt`. The Ticker service polls
`WHERE NextFireAt <= SYSUTCDATETIME()`, claims rows atomically (same
`UPDATE…OUTPUT` pattern as the BookmarkScheduler), synthesizes an
`InboundEvent { Kind: "schedule", MatchKeys: ["schedule|<wf>|<node>"], Payload: { firedAt: now } }`,
and computes/writes the next `NextFireAt` for the row.

Why this and not in-memory cron lists:

- Zero lag on publish — first fire is computed and persisted up front.
- Survives restart — `NextFireAt` is durable.
- Leader/standby promotable later — same SQL-lease pattern as BookmarkScheduler.
- Identical operational shape to the BookmarkScheduler (decision #8 friendly).

### 4.4 TriggerDispatcher — the pipeline

The function the Resumer calls when no bookmark matched.

```text
function TriggerDispatcher.Dispatch(event):

  // 1) Lookup all enabled registrations matching any candidate key
  registrations ← SELECT * FROM TriggerRegistrations
                  WHERE Enabled = 1 AND TriggerKey IN (event.MatchKeys)

  if registrations empty:
    DropEvent(event, reason: "no matching trigger")
    return

  // 2) For EACH matching registration, run the pipeline independently
  //    (one inbound event can fan to multiple workflows)
  for each reg in registrations:
    RunDispatchPipeline(reg, event)


function RunDispatchPipeline(reg, event):

  // 2a) Load the definition at the registered version
  def         ← LoadWorkflowDefinition(reg.WorkflowRefId, reg.Version)
  triggerNode ← def.FindNode(reg.TriggerNodeId)

  // 2b) Kind-specific filter (cheap, in-process)
  //     e.g. trigger:device with `propertyName` AND a `valueFilter` config
  if !triggerNode.MatchesFilter(event.Payload):
    EmitHistoryEvent(TriggerDispatchSkipped { RegId: reg.RegistrationId, Reason: "filter" })
    continue

  // 2c) Evaluate correlationKey expression against payload
  correlationValue ← null
  if reg.CorrelationExpr != null:
    try:
      correlationValue ← ExpressionEvaluator.Eval(reg.CorrelationExpr,
                                                  trigger: event.Payload)
    catch ex:
      EmitHistoryEvent(TriggerDispatchFailed { RegId: reg.RegistrationId, Error: ex })
      continue
  fullCorrelationKey ← (reg.WorkflowRefId, reg.TriggerNodeId, correlationValue)

  // 2d) Apply ConcurrencyPolicy (see 4.6)
  decision ← ConcurrencyEnforcer.Decide(reg, fullCorrelationKey)
  switch decision:
    case CreateRun:        // continue below
    case QueueRun:         EnqueuePending(reg, correlationValue, event.Payload); return
    case CancelExisting:   CancelExisting(reg, fullCorrelationKey); // then continue
    case DropEvent:        EmitHistoryEvent(TriggerDropped { … }); return

  // 2e) Create the Run
  run ← Run {
    RunId              = new Guid,
    WorkflowRefId      = reg.WorkflowRefId,
    Version            = reg.Version,
    CorrelationKey     = correlationValue,
    CorrelationKeyFull = fullCorrelationKey,
    TriggerNodeId      = reg.TriggerNodeId,
    TriggerPayload     = event.Payload,
    Status             = Running,
    CreatedAt          = now,
    CreditBudget       = LookupBudgetForWorkflow(reg.WorkflowRefId)
  }
  PersistRun(run)

  // 2f) Seed initial branch
  //     IMPORTANT (Q6): we do NOT seed LastOutput from the trigger payload.
  //     The first downstream node accesses the payload via $trigger.*.
  //     $output is reserved for "what the previous *node* emitted".
  branch ← Branch {
    BranchId   = new Guid,
    RunId      = run.RunId,
    NodeId     = <first downstream node off triggerNode.out>,
    Status     = Active,
    LocalVars  = {},
    LastOutput = null
  }
  PersistBranch(branch)

  // 2g) Hand off to RunDispatcher (Section 2's Branch Loop)
  RunDispatcher.Dispatch(BranchPointer { run.RunId, branch.BranchId, branch.NodeId })

  // 2h) Lifecycle event
  EmitHistoryEvent(RunStarted {
    RunId         = run.RunId,
    WorkflowRefId = reg.WorkflowRefId,
    Version       = reg.Version,
    TriggerNodeId = reg.TriggerNodeId,
    CorrelationKey= correlationValue,
    ReceivedAt    = event.ReceivedAt
  })
```

Notes:

- **One event can fan to many workflows.** Two workflows both subscribing to
  `signal|ops-emergency` each get their own Run. Each runs the full pipeline
  independently (its own correlation eval, its own concurrency decision).
- **Failure inside the pipeline is per-registration.** A bad correlation
  expression for workflow A does not stop workflow B from starting. Failures
  emit a `TriggerDispatchFailed` history event scoped to the registration.
- **The first executed node is the trigger node's downstream edge target, not
  the trigger node itself.** Trigger nodes have no `INodeExecutor`; they are
  metadata-only. The first executor invocation is whatever is wired off
  `triggerNode.out`.

### 4.5 CorrelationKey — the full mechanics

**Where it's defined:** On the **trigger node's config**, as an expression
string in our `$`-prefixed expression language.

```jsonc
{
  "kind": "trigger:device",
  "config": {
    "deviceRefId": "dev-greenhouse-thermo-01",
    "correlationKey": "$trigger.deviceId"
  }
}
```

**When it's evaluated:** At dispatch time, by
`TriggerDispatcher.RunDispatchPipeline`, against the inbound event payload.
The expression must be re-evaluated per event because different events can
produce different correlation values from the same workflow.

**What it can reference:** Only `$trigger.*` (the inbound payload) and
constants. It cannot reference `$shared`, `$local`, or `$run` (the run does
not exist yet). The expression evaluator validates this at publish time and
rejects bad expressions before the registration row is inserted.

**What it represents:** A scoping identifier for the ConcurrencyPolicy. "All
runs of this workflow on this trigger node with the same correlation value
are 'in scope' for the policy."

**The full identity key:** `(WorkflowRefId, TriggerNodeId, correlationValue)`.
All three are required:

- `WorkflowRefId` — prevents Workflow A's correlation key from colliding with
  Workflow B's.
- `TriggerNodeId` — prevents two triggers in the same workflow from sharing
  concurrency scope unexpectedly (e.g., a `device` trigger and a `signal`
  trigger both evaluating to `"deviceX"` should not queue against each other).
- `correlationValue` — the runtime-evaluated expression result.

**Stored on the Run row** as both the raw value (`Run.CorrelationKey`, indexed
for fast lookup) and the composite full key (used for policy enforcement).
The index lets us answer "find all running runs with this correlation" in
O(log n).

**Null/empty correlation handling:**

If `correlationKey` is absent or evaluates to null/empty → the run has no
correlation, and **ConcurrencyPolicy is effectively `AllowParallel`
regardless of what is declared**. We do not silently serialize all runs of a
workflow just because someone forgot the expression. A publish-time soft
warning flags the combination ("policy is `Queue` but no correlationKey is
set; this may behave as AllowParallel").

**If the expression throws** at dispatch time → `TriggerDispatchFailed` event
for that registration only; other registrations matching the same event
continue normally.

### 4.6 ConcurrencyPolicy enforcement

Policy is declared on the trigger node (locked decision #4). Four values:

| Policy | When a new event arrives and an in-scope run exists |
|---|---|
| `AllowParallel` (default) | Start the new run; ignore the existing one. |
| `Queue` | Persist a `PendingTriggerEvent` keyed to the correlation; start it when the current run finishes. |
| `CancelExisting` | Cancel the existing run(s), then start the new one. |
| `DropIfRunning` | Drop the new event; emit `TriggerDropped`. |

**Enforcement query (the hot path):**

```sql
SELECT RunId, Status
FROM Runs
WHERE WorkflowRefId = @wf
  AND TriggerNodeId = @triggerNodeId
  AND CorrelationKey = @correlationValue
  AND Status = 'Running'
```

The indexed read is the entire "do I have a conflict?" check.

#### `Queue` semantics

When the policy resolves to `Queue`, the inbound payload is preserved:

```text
PendingTriggerEvent {
  PendingId:        Guid
  RegistrationId:   Guid     -- which trigger registration
  CorrelationKey:   string?  -- the scoping key
  Payload:          JsonElement
  EnqueuedAt:       DateTime
  // Indexed by: (RegistrationId, CorrelationKey, EnqueuedAt ASC)
}
```

When a Run reaches a terminal status, run finalization does:

```text
On run terminal:
  pending ← SELECT TOP 1 * FROM PendingTriggerEvents
            WHERE RegistrationId = @reg AND CorrelationKey = @key
            ORDER BY EnqueuedAt ASC

  if pending:
    DELETE that pending row
    TriggerDispatcher.RunDispatchPipeline(reg, synthetic event from pending)
```

**Locked design point:** `Queue` is FIFO per `(RegistrationId, CorrelationKey)`.
Cross-correlation ordering is neither guaranteed nor meaningful.

#### `CancelExisting`

```text
For each existing run with matching correlation:
  CancelRun(run.RunId, reason: "preempted by new trigger")

// Default v1 behavior: start the new run immediately, in parallel with cancellation drain.
// Cancellation is cooperative (Section 2); a misbehaving Active branch must
// not be able to wedge the new run.
```

Tunable in v2 if "wait for existing to fully drain before starting new" is
needed.

#### `DropIfRunning`

```text
EmitHistoryEvent(TriggerDropped {
  RegistrationId, CorrelationKey,
  Reason: "DropIfRunning",
  Payload: event.Payload (truncated)
})
return
```

Always emit the history event — operators need to see "we dropped this
because another was running" for debugging.

### 4.7 Trigger families for v1

Six kinds, all share the dispatcher pipeline:

| Kind | TriggerKey shape | Source adapter | Typical use |
|---|---|---|---|
| `trigger:device` | `device\|<refId>\|<propName or *>` | RabbitMqInboundAdapter | "When this device's temperature changes" |
| `trigger:webhook` | `webhook\|<METHOD>\|<path>` | HttpInboundAdapter | "When `/water-start` is POSTed" |
| `trigger:schedule` | `schedule\|<workflowRef>\|<triggerNodeId>` | TickerInboundAdapter | "Every day at 6am" |
| `trigger:signal` | `signal\|<name>` | SignalInboundAdapter | "When ops emits 'emergency' signal" |
| `trigger:event` | `event\|<topic>` | RabbitMqInboundAdapter | "When `billing.invoice.created` is published" |
| (not a trigger) `ChildRunCompleted` | — | — | Parent waits via Bookmark, never a Trigger |

The last row is for symmetry: child-run completion is never a Trigger because
it is always tied to a specific parent run. It only ever wakes a Bookmark.

### 4.8 Edge cases

#### Late-binding: workflow published while events are in flight

Events that arrive before the registration insert commits will not match.
Acceptable — publishing is an explicit operator action, not part of the event
stream. Documented behavior: "events arriving within ~publish-commit latency
of a new workflow may not fire it; retry from the source if needed".

#### Trigger filter mismatch vs correlation throw

Distinct outcomes:

- **Filter no-match** (`triggerNode.MatchesFilter` returns false): silent drop
  with `TriggerDispatchSkipped` event at debug level. Expected behavior.
- **Correlation expression throws**: `TriggerDispatchFailed` event at error
  level. The expression is buggy; operators need visibility.

#### Burst / throttling

Not in v1. If a device sprays 1000 property-change events per second, we
start 1000 runs (subject to ConcurrencyPolicy). The `Queue` / `DropIfRunning`
policies are the user's first-class throttle mechanism. Engine-level
throttling, if needed later, slots in as a Section 7 (infrastructure) feature
— a leaky-bucket pre-filter on the InboundHub — without changing this section.

#### Disabled / paused workflows

`Enabled = 0` on the registration makes it invisible to the dispatcher. No
separate "paused" concept needed. To re-enable, flip the bit.

### 4.9 The first node and `$trigger` vs `$output`

**Locked decision (Q6):** the trigger does NOT seed `Branch.LastOutput` from
the inbound payload.

- The inbound payload is reachable as **`$trigger.*`** (immutable for the
  lifetime of the run).
- The first downstream node sees **`$output = null`** because no prior node
  has emitted yet. It cannot accidentally read "the payload" via `$output`.
- After the first node executes, `$output` becomes that node's output, and so
  on through the branch.

This keeps the semantics clean:

- `$trigger` = what started this run, immutable.
- `$output` = what the immediately previous node in this branch emitted.
- `$local` = branch-private vars.
- `$shared` = workflow-scoped atomic vars (Section 1).
- `$run` = run-private vars (counters etc.).

The first-node author writes `$trigger.deviceId` to get the device ID, never
`$output.deviceId`. The distinction is taught once and never confused again.

### Section 4 locked decisions

- **Two-tier model:** static trigger nodes in definitions, indexed
  `TriggerRegistration` rows at publish time. (4.1)
- **TriggerKey is identity-only**, not data-dependent. Wildcards via multi-key
  lookup. (4.2)
- **One `InboundEvent` shape** with `MatchKeys[]`, normalized by per-source
  adapters. (4.3)
- **Scheduled triggers go through the unified Resumer → Dispatcher path** via
  a `ScheduledFires` table + Ticker adapter. (Q7, 4.3)
- **CorrelationKey** is an expression on the trigger node's config; evaluated
  at dispatch time; full identity is `(WorkflowRefId, TriggerNodeId, value)`.
  (4.5)
- **Null correlation → effective `AllowParallel`**, with publish-time soft
  warning. (4.5)
- **Concurrency policies:** `AllowParallel` (default) / `Queue` (FIFO per
  correlation) / `CancelExisting` / `DropIfRunning`. (4.6)
- **`CancelExisting` starts the new run in parallel with cancellation drain**
  (tunable). (4.6)
- **First downstream node sees `$output = null`;** trigger payload is
  `$trigger.*` only. (Q6, 4.9)
- **One inbound event can fan to multiple workflows;** each registration runs
  an independent pipeline. (4.4)

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
| Bookmark | Durable suspension point `(RunId, BranchId, NodeId, WakeCondition, MatchKey?, TimeoutAt?, OnTimeoutPortId?)`. |
| WakeCondition | What wakes a Bookmark: Timer / Signal / DevicePropertyChange / DeviceTelemetryMatch / Manual / ChildRunCompleted. |
| MatchKey | Indexed string on a Bookmark computed from its WakeCondition; the Resumer's lookup key. |
| BookmarkScheduler | Background service that polls `Bookmarks WHERE TimeoutAt <= now` and drives timeouts/timer wakes. |
| BookmarkResumer | Routes inbound events to bookmarks (resume) or falls through to TriggerDispatcher (start-new-run). |
| InboundHub | Event normalizer that produces `(kind, matchKeys, payload)` for the Resumer. |
| `control:waitForHttp` | Anonymous mid-graph HTTP wait; mints a one-shot `/wake/{token}` URL. |
| `control:awaitSignal` | Named mid-graph signal wait with an authored correlation expression. |
| Trigger | A node that starts a Run when its external condition is met. |
| TriggerRegistration | Indexed SQL row created at publish time; the dispatcher's lookup record. |
| TriggerKey | Identity-shaped string used to index trigger registrations (e.g. `device\|D\|P`). |
| InboundEvent | Normalized inbound shape `{Kind, MatchKeys[], Payload, DeviceRefId?, Source, ReceivedAt}`. |
| TriggerDispatcher | Pipeline that turns a matched event into a new Run (or queues/cancels/drops per policy). |
| Ticker | Service that fires scheduled trigger events from a `ScheduledFires` table. |
| ScheduledFire | Row holding `(RegistrationId, Cron, NextFireAt)` polled by the Ticker. |
| CorrelationKey | Expression value used to scope `ConcurrencyPolicy`; full identity = `(WorkflowRefId, TriggerNodeId, value)`. |
| ConcurrencyPolicy | AllowParallel / Queue / CancelExisting / DropIfRunning, per correlation key. |
| PendingTriggerEvent | Queued inbound payload waiting for current-correlation run to finish (Queue policy). |
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
