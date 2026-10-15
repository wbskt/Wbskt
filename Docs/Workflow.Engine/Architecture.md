# Workflow engine architecture

How the engine host is put together and how a run moves through it. For what authors can build,
see [Features.md](Features.md); for the patterns behind the code, see
[Design.Patterns.md](Design.Patterns.md).

## 1. Projects

| Project | Holds |
|---|---|
| `Wbskt.Workflow.Abstraction` | The definition model (nodes, edges, expressions), node configs, `NodeKinds` descriptors, `WorkflowValidator`, `WorkflowEngineOptions`, entity and status types, branch-state key names. Shared by the management host and the engine. |
| `Wbskt.Workflow` | The runtime: `Runtime/` (dispatcher, loop, retry, result handlers, finalizer, recovery), `Channels/` (one class per trigger channel), `NodeExecutors/` (one executor per node kind), `Integrations/`, `Providers/` (SQL access), `Telemetry/`. Registered by `AddWorkflowEngine`. |
| `Wbskt.Workflow.Builder` | `WorkflowBuilder`, a fluent C# API for building definitions in tests and examples. |
| `Hosts/Wbskt.Workflow.Engine.Host` | The engine process: inbound adapters (bus consumers), controllers, hosted services, leadership, middleware. |
| `Models/Wbskt.Workflow.Engine.Contracts` | The HTTP contracts of the engine's inbound API, shared with the management host. |
| `Hosts/Wbskt.Management.Host/Services/Workflow` | Publishing, versioning, rollback and trigger registration (`WorkflowLifecycleService`, `TriggerRegistrationService`). |
| `Tools/Wbskt.Workflow.Exporter` | Writes the example workflows to JSON with the builder. |

## 2. Where the engine sits

* The **Socket host** holds device connections. It publishes `ClientMessageReceivedEvent`,
  `ClientConnectedEvent`, `ClientDisconnectedEvent` and command acks or failures on RabbitMQ.
* The **Management host** is the only HTTP caller of the engine. It calls the engine's inbound API
  for manual runs, signals and integration seal/test (`WorkflowEngineClient`, with the shared API key
  header). Public webhook and wake callbacks land on the management host's `PublicCallbackController`,
  which answers `202` and sends an `InboundCallback` command over the bus. Run cancellation is a
  `CancelWorkflowRun` command, so the engine stays the only writer of run state.
* The **Engine** reads and writes SQL Server for everything: definitions, registrations, runs,
  branches, bookmarks, history, idempotency keys, shared variables and its own leader lease.
* Outbound, the engine sends device commands through `IDeviceCommandSender` (the same path the
  console uses) and calls webhooks, SMTP, Telegram, Discord and ntfy directly.

## 3. Leadership: one active engine

Engine instances run **active/standby**. Exactly one instance (the leader) consumes messages,
executes branches and runs background workers; the others wait.

* `LeaderElectionService` holds the `engine-leader` row in `dbo.Leases` (`Lease_TryAcquire`),
  renewing every 10 s with a 30 s TTL, and releases it on shutdown. A leader that fails to renew
  stops the application rather than risk two leaders.
* `LeadershipState` is the in-memory view of that lease. `LeadershipGate` reports active only when
  this instance is leader, the bus has started, and the lease is valid with a 5 s safety margin.
* `EngineLeadershipCoordinator` starts MassTransit only after this instance becomes leader (the
  default MassTransit hosted service is removed), then runs startup recovery. If recovery fails it
  logs Critical and rethrows, which stops the host.
* `LeaderOnlyMiddleware` answers `503 ENGINE_NOT_LEADER` on `[InboundEndpoint]` endpoints of a
  standby. `InboundApiKeyMiddleware` checks `X-Wbskt-Api-Key` against `Engine:InboundApiKey` on
  every endpoint except the health probes. In Production an unset key fails every call; in
  Development it turns the check off.
* `/healthz/ready` includes leadership, so the load balancer only routes to the leader.
* Every periodic worker derives from `LeaderPeriodicWorker` and skips its tick unless the gate is
  active.

## 4. Inbound path: from event to run

```
bus consumer / controller ─► IInboundHub ─► TriggerDispatcher ─┬─► BookmarkResumer (wake a waiting branch)
                                                               └─► registrations ─► RunStarter (new run)
```

### 4.1 Adapters

| Adapter | Source | Channel |
|---|---|---|
| `ClientPayloadReceivedConsumer` | `ClientMessageReceivedEvent` | `client`; also records hold state via `ClientHoldRecorder`. A message sent more than 30 s before it was received is treated as late. |
| `ClientConnectedConsumer`, `ClientDisconnectedConsumer` | presence events | Not dispatched directly: `ClientPresenceParker` records presence checks that `ClientPresenceTicker` later fires. |
| `CommandOutcomeConsumer` | `ClientCommandAckedEvent`, `ClientCommandFailedEvent` | `command` (wakes a `clientMessage` waiting for its ack). |
| `InboundCallbackConsumer` | `InboundCallback` command | `webhook` (start) or `http-wake` (wake). |
| `CancelWorkflowRunConsumer` | `CancelWorkflowRun` command | Calls `RunCancellationService`. |
| `WorkflowDefinitionChangedConsumer` | `WorkflowDefinitionChangedEvent` | Clears the trigger registration cache and cached definitions. |
| `InboundManualController` | `POST api/inbound/manual/{workflowRefId}?idempotencyKey=` | `manual` |
| `InboundSignalController` | `POST api/inbound/signal/{runRefId}/{signalName}` | `signal` |
| `IntegrationsController` | seal / test | Not a trigger: seals integration secrets and probes them. |

`InboundHub` waits until startup recovery is Ready before dispatching, so no event races recovery.

### 4.2 `TriggerDispatcher`

For each inbound event:

1. **Resolve keys.** The channel builds the event's match key; the default correlation key is
   resolved.
2. **Wake first.** `BookmarkResumer.MatchInboundAsync` claims `inbound-event:{id}` in
   `IdempotencyKeys` (a repeat delivery is a no-op; a claim marked Failed can be reclaimed),
   claim-deletes every bookmark with the match key, writes the payload into the branch as `__wake`
   (and `__wake_node`), and dispatches the branch with reason `BookmarkResumed`. Wake-only channels
   (`signal`, `http-wake`, `child-completed`, `command`) stop here.
3. **Load registrations.** For the `client` channel this goes through `TriggerRegistrationCache`
   (`LoadUnlessKnownEmptyAsync`): most device messages match no workflow, and a cached "no
   registrations" answer saves a database read. The cache is generation-stamped, cleared by
   `WorkflowDefinitionChangedEvent`, and entries live 1 minute at most.
4. **Per registration:**
   * evaluate the correlation expression (a `$trigger.` path or a constant);
   * check the webhook secret (`X-Wbskt-Secret`, compared as SHA-256 hashes in fixed time);
   * evaluate the filter (an error counts as "no match": filters fail closed);
   * claim `trigger-start:{registrationId}:{sha256(eventId)}`, stored as the unique
     `Runs.TriggerStartKey`, so a redelivered event never starts a second run;
   * apply the concurrency policy (`TriggerConcurrencyEnforcer`): `AllowParallel`,
     `DropIfRunning`, `Queue` (to `PendingTriggerEvents`) or `CancelExisting` (cancel every matching
     run, then start);
   * start the run with `RunStarter`.
5. **Outcome** per registration: `StartedRun`, `Queued`, `Dropped`, `Refused`, `Filtered`,
   `SecretMismatch`, `NoRegistration`, `Idempotent` or `ResumedBookmark`.

### 4.3 `RunStarter`

`Run_Start` creates the run, its `RunCounters` row and the root branch in one transaction. The root
branch's local state is `{ "trigger": <payload> }` and it starts on the trigger node, whose
executor stamps `trigger` and `triggeredAt` and continues.

* **Credit budget**: the definition's `creditBudget`, else `DefaultCreditBudgetPerRun` (10 000).
* **Workspace ceiling**: a workspace may have at most `MaxActiveRunsPerWorkspace` (200) active runs;
  `Run_Start` raises error 50026 and the outcome is `Refused`. Child runs, queue replays and
  `CancelExisting` replacements are exempt, because refusing them would strand work already accepted.
* `ParentRunRefId` is set only when the event says it starts a child run.
* `RunStarted` is journaled without the trigger body.

`PendingTriggerEventDrainer` replays one queued event each time a run finishes. It re-mints the
event id as `{id}:drain:{rowId}` so the replay gets its own trigger-start key.

### 4.4 Publishing and trigger registrations

Publishing happens in the management host (`WorkflowLifecycleService`):

1. `WorkflowValidator.Validate` (see [Features.md §13](Features.md#13-validation)) and a check that
   every referenced integration exists in the workspace.
2. `WorkflowDefinition_Publish` inserts the next version under `HOLDLOCK` and enforces
   `Workflows:MaxPerWorkspace` (100).
3. The previous version is deprecated and `TriggerRegistrationService.OnPublishedAsync` deletes and
   re-writes the registrations. Each trigger node's channel produces a `TriggerRegistrationSpec`
   (match key, filter, secret hash); schedule triggers also get a `ScheduledFires` row.
4. If registration fails, the publish is compensated: the new version's registrations are removed,
   the new version is deleted (or left with no triggers if a run already references it), and the
   previous version is restored.
5. `WorkflowDefinitionChangedEvent` and `WorkflowPublishedEvent` are announced.

Rollback publishes an older definition as a new version. Deprecate removes a workflow's
registrations; Reinstate re-enables the version and registers its triggers again.

## 5. Execution: pump, loop, handlers

### 5.1 `BranchExecutionPump`

`ChannelRunDispatcher` is an unbounded in-memory channel of branch work items. `BranchExecutionPump`
(a `BackgroundService`) reads it and runs up to `BranchWorkerLimit` (50) branches at once, each in its
own DI scope. An exception that escapes a branch is contained (`ContainLeakedBranchAsync`): the
branch is ended `BRANCH_LOOP_ESCAPED` so the run can still finalize.

The dispatcher holds no durable state. If the process dies, recovery rebuilds the queue from SQL.

### 5.2 `BranchLoop`

`BranchLoop.RunAsync` drives one branch until it parks, ends or the host stops:

1. Skip the branch if it has already ended. Journal `BranchStarted`, `BranchResumed` or
   `BranchRecovered` depending on the dispatch reason.
2. For each step:
   * **Stop check.** The run's cancellation token every step; the run row on step 0 and every
     `CancellationCheckInterval` (10) steps.
   * **Missing node** ends the branch as completed.
   * **Parked retry.** If the branch was parked for a retry backoff, take `__retry_attempt`.
   * **Pending take-port.** If a bookmark's TTL fired, the branch carries `PendingTakePort`; the
     loop continues out of that port (for example `timeout`) without running the node, and clears
     the node's park keys.
   * **Execute** (`ExecuteNodeAsync`): journal `NodeStarted`, look up the executor in
     `NodeExecutorRegistry`, run it through `RetryExecutor`, refuse a local state larger than
     `MaxBranchLocalStateBytes` (256 KB, `BRANCH_STATE_TOO_LARGE`), journal `NodeCompleted` or
     `NodeFailed` with duration and credits charged.
   * **Dispatch the result** to `BranchResultDispatcher`.
3. Host shutdown leaves the branch `Active` for the next leader's recovery.

### 5.3 Node results and their handlers

An executor returns one `NodeExecutionResult`. `BranchResultDispatcher` picks the one
`IBranchResultHandler` registered for its type.

| Result | Handler does |
|---|---|
| `Continue(port, patch, removeKeys)` | Apply the patch; follow the edge from `port` (a node id when used as `JumpTo`). No edge: decline any join the branch belongs to, then complete the branch. |
| `Fork(children, continuePort, patch)` | One child and no continue port: take it inline. Otherwise derive cohort and child ids deterministically (SHA-256 of branch, node, row version), count the children on the run before creating them (`CreateIfAbsent`, so a replay never duplicates), and dispatch each with reason `ForkChild`. |
| `WaitForBookmark(condition, patch)` | Set the branch `Waiting`, journal `BranchParked`, write one bookmark (match key from the condition's channel, `ExpiresAt`, `TtlPort`), then run the executor's `AfterParked` hook (for example, send the device command only once the bookmark exists). |
| `Fail(code, message, retryable, cause)` | Apply the node's `onFailure` (see [Features.md §7](Features.md#7-failure-handling)), or end the branch Failed. Contribute `failed` to a join; release the branch. |
| `Terminal(reason, output?)` | `Cancelled` cancels the branch. Otherwise decline the join (unless this node is the join), store the run output if given, and complete the branch. |

### 5.4 Retries and idempotency (`RetryExecutor`)

Every executor runs inside `RetryExecutor.RunWithRetryAsync`:

* **Idempotency.** Nodes that are not replay-safe (`INodeExecutor.IsReplaySafe` is false: forks,
  joins, parallel loops, sub-workflows, commands, every action, shared counter writes) get an
  idempotency key per attempt: `action:{runId}:{branchRef}:{nodeId}:{visitToken}:{attempt}`. The
  visit token is the branch row's `RowVersion`, so a loop that visits the same node twice gets two
  keys, while a replay of the same visit after a crash gets the cached outcome.
* **Credits** are charged per attempt (`TryChargeAsync` on the run counters). A resume visit (the
  branch coming back from a bookmark) is free; a retry is not. An empty budget fails the node with
  `OUT_OF_CREDITS`.
* **Retry decision.** One `Decide()` handles both a retryable `Fail` and a thrown
  `TransientNodeException`, using the node's `retry` policy, and journals `NodeRetrying`.
  A delay shorter than `RetryParkThreshold` (5 s) waits in memory; a longer one parks the branch on a
  timer bookmark with `__retry_attempt`, so a long backoff survives failover and holds no worker.
* **Exceptions.** `PermanentNodeException` keeps its error code. Any other exception becomes
  `EXECUTOR_CRASH`. Cancellation is rethrown.

### 5.5 Ending branches and runs

* **`BranchEndings`** ends a branch with a compare-and-set (`Branch_EndIfLive`): only a live branch
  can be ended, so two racers cannot both end it. The winner journals and calls
  `BranchTerminator.ReleaseAsync`.
* **Release** decrements `RunCounters.ActiveBranchCount` (clamped at zero). At zero the run is
  finalized. `BranchTerminator` reaches `RunFinalizer` through a `Lazy<>` to break a DI cycle.
* **`RunFinalizer`** picks the terminal status:

  | Run status when the last branch ends | Branch outcomes | Terminal status |
  |---|---|---|
  | `Cancelling` with `OUT_OF_CREDITS` | any | `OutOfCredits` |
  | `Cancelling` | some cancelled and some completed | `PartiallyFailed` |
  | `Cancelling` | otherwise | `Cancelled` |
  | `Failing` | any | `Failed` |
  | `Running` | failed and completed | `PartiallyFailed` |
  | `Running` | only failed | `Failed` |
  | `Running` | otherwise | `Succeeded` |

  It then sets the terminal status (idempotent), journals `RunFinalized`, drains one queued trigger
  event, publishes `RunCompleted` or `RunFailed`, deletes the run's bookmarks and join aggregators,
  notifies a parent run (`SubWorkflowCompletionHook` sends `child-completed` with status and output)
  and drops the run's cancellation token.

| Statuses | In flight | Terminal |
|---|---|---|
| Run | `Running`, `Failing`, `Cancelling` | `Succeeded`, `Failed`, `PartiallyFailed`, `Cancelled`, `OutOfCredits` |
| Branch | `Active`, `Waiting` | `Completed`, `Failed`, `Cancelled` |

### 5.6 Cancellation and run failure

`RunCancellationService` is the single place that stops a run:

* `RequestCancellationAsync` moves `Running` or `Failing` to `Cancelling` (repeating it on a
  `Cancelling` run still runs the cleanup).
* `FailRunAsync` moves `Running` to `Failing` and journals `RunFailureRequested`. It is used by
  `control:failRun`, `onFailure: FailRun` and `failFast`.
* Both then stop the run's work: cancel its token, delete its bookmarks, cancel its waiting branches
  (`Branch_CancelWaiting`) and release them, and cancel every active child run.

Active branches see the stop at their next stop check and end Cancelled; the last one finalizes the
run.

## 6. Waiting: bookmarks

A bookmark row says a branch waits for a match key, a deadline, or both. Every waiting node kind
uses the same mechanism (see [Design.Patterns.md](Design.Patterns.md#the-parking-pattern)).

* **Inbound wake.** `BookmarkResumer` claim-deletes bookmarks by match key (§4.2).
* **Timer wake.** `BookmarkScheduler` claim-deletes due bookmarks every second. A pure timer
  (delay, retry backoff) resumes the node. A bookmark with a TTL on another condition sets the
  branch's `PendingTakePort` to the node's TTL port (`timeout`), so the loop leaves by that port.
* Whoever deletes the row owns the wake; the other side finds nothing to delete.

## 7. Recovery

`RunRecoveryService.RecoverAsync` runs once each time an instance becomes leader, before any
inbound event is dispatched:

* re-dispatches every `Active` branch with reason `Recovered`;
* re-applies cancellation or failure for runs left in `Cancelling` or `Failing`, including runs
  whose only remaining branches are `Waiting`;
* marks the engine Ready, which opens `InboundHub`.

Waiting branches need nothing: their bookmarks are in SQL and the scheduler picks up any that fell
due while no leader was running.

## 8. Background workers

All run on the leader only.

| Worker | Every | Does |
|---|---|---|
| `BookmarkScheduler` | 1 s | Claims due bookmarks and resumes their branches (or sets the TTL port). |
| `BookmarkOrphanGc` | 5 min | Deletes bookmarks whose branch is gone. Fails a `Waiting` branch with no bookmark (`BOOKMARK_LOST`) once two passes past `StrandedBranchThreshold` (5 min) saw it unchanged. |
| `ScheduledFireTicker` | 1 s | Leases due `ScheduledFires` rows (for `LeaseDurationSeconds`, 120 s), fires the `schedule` channel and advances the next fire time by the cron (Cronos). |
| `ClientPresenceTicker` | 1 s | Resolves presence checks: Fire, Stale or Pending. Gives up after 10 minutes. |
| `ClientHoldTicker` | 1 s | Fires `client-hold` triggers whose filter has held for the configured time. |
| `RunReaper` | 5 min | Fails runs older than `RunStuckThreshold` (30 min) that are not terminal but have no active branch and no bookmark (`REAPER_TIMEOUT`), and finalizes them. |
| `HistoryRetentionGc` | 1 h | Deletes Info history after 30 days and Warn/Error after 365 days, then deletes retired runs. |
| `IdempotencyKeyGc` | 1 h | Deletes idempotency keys older than 24 h. |
| `PendingTriggerEventBacklogReaper` | periodic | Drops queued trigger events older than 24 h. |
| `MetricsExporter` | periodic | Exports the gauge metrics below. |

## 9. Persistence

All state is in SQL Server (`Databases/Wbskt.Database`), accessed through stored procedures by
providers in `Wbskt.Workflow/Providers`.

| Table | Holds |
|---|---|
| `WorkflowDefinitions` | Every published version, as JSON. |
| `TriggerRegistrations` | Match key → definition, with filter and secret hash. |
| `ScheduledFires` | Next fire time per schedule trigger. |
| `ClientPresenceChecks`, `ClientHoldStates` | Pending presence and hold evaluations. |
| `Runs`, `RunCounters` | Run row (status, correlation, `TriggerStartKey`, parent, output) and its counters (active branches, credits). |
| `Branches` | Current node, status, local state JSON, `PendingTakePort`, `RowVersion`. |
| `Bookmarks` | Waiting branches: match key, `ExpiresAt`, TTL port. |
| `JoinAggregators`, `JoinContributions` | Join state per cohort. |
| `IdempotencyKeys` | Inbound-event claims and per-attempt action outcomes. |
| `PendingTriggerEvents` | Events queued by the `Queue` concurrency policy. |
| `SharedVariables` | Workflow-scoped variables (per `WorkflowRefId`). |
| `HistoryEvents` | The run journal shown in the console. |
| `Leases` | The engine leader lease. |
| `Integrations`, `WorkflowIntegrationRefs` | Sealed integration secrets and which workflows use them. |

## 10. History

`HistoryEvents` is the run journal, written in batches. Severity is Warn for failures, retries,
cancellations and abandoned joins.

| Scope | Events |
|---|---|
| Run | `RunStarted`, `RunFinalized`, `RunCancellationRequested`, `RunFailureRequested` |
| Branch | `BranchStarted`, `BranchResumed`, `BranchRecovered`, `BranchParked`, `BranchCompleted`, `BranchFailed`, `BranchCancelled` |
| Node | `NodeStarted`, `NodeCompleted`, `NodeFailed`, `NodeRetrying` |
| Join | `JoinContinuationSpawned`, `JoinAbandoned` |

## 11. Metrics

`WorkflowMetrics` (meter in `Wbskt.Workflow/Telemetry`):

`wbskt_workflow_runs_started_total`, `wbskt_workflow_runs_completed_total`,
`wbskt_workflow_runs_refused_total`, `wbskt_workflow_credits_consumed_total`,
`wbskt_workflow_node_duration_ms`, `wbskt_workflow_branches_active`,
`wbskt_workflow_dispatcher_queue_depth`, `wbskt_workflow_bookmarks_outstanding`,
`wbskt_workflow_bookmarks_overdue`, `wbskt_workflow_pending_triggers`,
`wbskt_workflow_worker_ticks_total`.

## 12. Configuration

`WorkflowEngineOptions`, bound from the `WorkflowEngine` section (`Config/workflow-engine.json`).

| Option | Default | Purpose |
|---|---|---|
| `BookmarkPollInterval` | 1 s | Timer wake resolution. |
| `BookmarkOrphanGcInterval` | 5 min | Orphan and stranded-branch sweep. |
| `StrandedBranchThreshold` | 5 min | How long a waiting branch may have no bookmark. |
| `ScheduleTickInterval`, presence and hold tick intervals | 1 s | Trigger tickers. |
| `LeaseDurationSeconds` | 120 | Lease on due scheduled fires. |
| `RunReaperInterval` / `RunStuckThreshold` | 5 min / 30 min | Stuck-run reaper. |
| History retention interval / window / elevated window | 1 h / 30 d / 365 d | `HistoryRetentionGc`. |
| `PendingTriggerEventTtl` | 24 h | Queued trigger events. |
| `IdempotencyRetentionWindow` | 24 h | Idempotency keys. |
| `DefaultCreditBudgetPerRun` | 10 000 | When a definition sets no budget. |
| `BranchWorkerLimit` | 50 | Concurrent branches. |
| `RetryParkThreshold` | 5 s | Backoffs at least this long park on a bookmark. |
| `CancellationCheckInterval` | 10 | Steps between run-row stop checks. |
| `MaxFanOut` | 1 000 | Parallel-for-each items. |
| `MaxBranchLocalStateBytes` | 256 KB | Local state size limit. |
| `MaxActiveRunsPerWorkspace` | 200 | Workspace run ceiling. |
| `MaxSubWorkflowDepth` | 8 | Nesting limit. |
| `TriggerRegistrationCacheLifetime` | 1 min | 0 turns the cache off. |
| Leader election renew / TTL | 10 s / 30 s | Leader lease. |
| `CreditCosts` | per kind | Node prices; see [Features.md §11](Features.md#11-credits). |
