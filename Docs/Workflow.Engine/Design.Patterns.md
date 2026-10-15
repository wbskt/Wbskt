# Workflow engine design patterns

The engine is built from a small set of patterns. Each section says what the pattern is, where it
lives, and the rule it sets for new code. [Architecture.md](Architecture.md) shows how they fit
together; general repo conventions are in [Coding.Conventions.md](../Coding.Conventions.md).

## Extension points

### One descriptor per node kind

`NodeKinds.All` holds one `NodeKindDescriptor` per kind: its `kind` string, category, model type,
config type, default credit cost and expected ports (fixed, or computed from config for forks,
await-signal and acknowledged client messages). The JSON converter, the validator's port checks, the
price list and `NodeKind.All` all read it, so nothing else keeps a list of kinds.

**Rule.** A new node kind is one descriptor, one model and config, and one `INodeExecutor`
registered in `AddWorkflowEngine`. An architecture test pins one executor per kind, so a kind with no
executor (or two) fails the build.

### One executor per kind, one handler per result

`NodeExecutorRegistry` picks an `INodeExecutor` by kind. The executor only decides; it returns a
`NodeExecutionResult` (`Continue`, `Fork`, `WaitForBookmark`, `Fail`, `Terminal`).
`BranchResultDispatcher` then picks the `IBranchResultHandler` for that result type, and the handler
changes the branch. `BranchLoop` knows neither nodes nor results.

**Rule.** Executors do not write branch or run rows; handlers do not know node kinds. A new result
type needs a `BranchResultHandler<TResult>` or the dispatcher throws naming it.

### A channel owns its keys

Every inbound source is one class in `TriggerChannelRegistry.Default` (`ClientChannel`,
`ScheduleChannel`, `WebhookChannel`, `SignalChannel`, `HttpWakeChannel`, `CommandChannel`, ...). The
same class builds the **registration key** written at publish and the **match key** of an inbound
event or bookmark, so the two can never drift apart. Channels that only wake (`signal`, `http-wake`,
`child-completed`, `command`) have no registration side.

**Rule.** Never format a trigger or bookmark key outside its channel.

## Durability and exactly-once effects

### SQL is the only source of truth

Run, branch, bookmark, join and idempotency state live in SQL. The in-memory dispatcher queue,
leadership state and caches can all be lost; recovery rebuilds work from `Active` branches and
`Waiting` bookmarks.

**Rule.** Anything a new leader would need goes in a row before the work that depends on it.

### Replay-safe or deduplicated

A crash can replay the step a branch was on. Each executor declares `IsReplaySafe(node)`:

* **Replay-safe** (running twice is harmless): passthrough triggers, logic, sequential for-each,
  end, fail-run, delay, await-signal, wait-for-HTTP, local variables and shared `Set`.
* **Deduplicated** (everything else: forks, joins, parallel for-each, sub-workflows, commands, all
  actions, shared `Increment`, `Decrement`, `CompareAndSet`). `RetryExecutor` claims
  `action:{run}:{branch}:{node}:{visitToken}:{attempt}` in `IdempotencyKeys` before running, stores
  the outcome after, and a replay returns the stored outcome instead of running again.

The **visit token** is the branch row's `RowVersion`, which changes every time the branch moves.
Two visits to the same node in a loop get different keys; a replay of one visit gets the same key.

**Rule.** `IsReplaySafe` defaults to false. Return true only when a second run cannot be observed.

### Token as claim

Idempotency rows double as claims. `inbound-event:{id}` is claimed before bookmarks are matched,
so a redelivered event wakes nothing twice. A claim marked Failed can be reclaimed, so a crash
mid-dispatch does not lose the event. `Runs.TriggerStartKey`
(`trigger-start:{registration}:{sha256(eventId)}`) is a unique column, so a redelivered event starts
no second run.

### Claim by delete

A bookmark is claimed by deleting it (`Bookmark_ClaimByRefId` for an inbound match,
`Bookmark_ClaimDue` in the scheduler).
Whoever deletes the row owns the wake; an inbound event and the TTL timer racing for the same
bookmark cannot both resume the branch. Due `ScheduledFires`, presence checks and hold states are
leased by their tickers (`*_LeaseDue`) in the same spirit.

### Compare-and-set endings

`Branch_EndIfLive` ends a branch only if it is still `Active` or `Waiting`. Every path that ends a
branch (completion, failure, cancellation, the stranded-branch sweep, a leaked exception) goes
through `BranchEndings`, and only the winner journals and releases it. `Run_SetTerminal` is likewise
idempotent.

### Count before create

A fork increments the run's `ActiveBranchCount` for all its children **before** creating them, and
creates them with `Branch_InsertIfAbsent`. If the count came after, a fast child could finish, drop
the count to zero and finalize the run while siblings were still being created. Child and cohort ids
are derived deterministically (SHA-256 of branch, node and row version), so a replayed fork finds its
children already there.

### Release is a clamped decrement

`BranchTerminator.ReleaseAsync` decrements the active count, never below zero, and the release that
reaches zero finalizes the run. `BranchTerminator` gets `RunFinalizer` through a `Lazy<>` because the
finalizer's collaborators in turn depend on branch release.

## Waiting

### The parking pattern

Every node that waits derives from `ParkingNodeExecutor` (delay, await-signal, wait-for-HTTP,
sub-workflow, client message with `awaitAck`). The base class owns the protocol; the subclass only
says what to wait for (`ParkPlan`) and what to do when woken (`ResumeStep`).

1. **First visit.** Write a park marker `{ nodeId, waitingOn }` under the node's parked key (and a
   deadline key if it has a TTL), return `WaitForBookmark`, then do the side effect in
   `AfterParked` (send the command, start the child) so a fast reply always finds the bookmark.
2. **Woken.** The wake payload arrives in `__wake` with `__wake_node`. A wake meant for another node
   or an earlier visit is stale: the node keeps waiting for its remaining time and never consumes it.
3. **Resume.** The subclass returns `Leave(port, patch)`, `KeepWaiting` or `StartOver`.
4. **Every exit** removes the park keys (marker, deadline, `__wake`, `__wake_node`), so a loop back
   to the same node starts clean.

TTL expiry does not run the node: the scheduler sets the branch's `PendingTakePort` and the loop
leaves by that port.

**Rule.** A new waiting node derives from `ParkingNodeExecutor`. Do not hand-roll bookmarks.

### Long backoff parks too

A retry delay of at least `RetryParkThreshold` (5 s) becomes a timer bookmark with
`__retry_attempt`, using the same machinery. A short delay waits in memory.

### Reserved branch keys

`BranchStateKeys` lists every key the engine writes into branch state: the `__` prefix (wake,
join tokens, park markers, retry attempt, for-each iterators) plus `trigger`, `joinContributed`,
`joinSucceeded` and `joinFailed`. The validator refuses variables with those names.

**Rule.** New engine-owned state goes under `__` and into `BranchStateKeys`.

## Fan-out and joins

### Join token stack

A fork child carries `__join_token` (its cohort) and `__join_outer` (the enclosing cohort's token),
built by `JoinTokens.ForChild`. When a join converges, `JoinTokens.AfterJoin` pops back to the outer
token, so nested fork/join pairs work without the join needing to know its depth.
`WorkflowGraph.FindJoinForCohort` finds the join at the same nesting level when the fork runs, and the
aggregator row (`JoinAggregator_Initialize`) records the expected count and mode.

### Every member votes

A cohort branch always tells its join how it ended: `succeeded` (reached the join), `failed`
(`FailResultHandler`) or `declined` (ended elsewhere, `JoinDeclines`). `JoinAggregator_Contribute`
decides atomically whether the join converges, waits, or can no longer be met. Without the decline
vote, an `All` join with a member that ended early used to wait forever while the run reported
success.

## Control plane

### Leader gate

Every piece of leader-only work checks `ILeadershipGate`: bus consumers (MassTransit only starts on
the leader), periodic workers (`LeaderPeriodicWorker`), and `[InboundEndpoint]` controllers
(`LeaderOnlyMiddleware`). The gate trusts the lease minus a 5 s margin, so an instance stops working
before another can take over.

### Recovery before traffic

`InboundHub` holds every inbound event until `RunRecoveryService` reports Ready. Events never race
the re-dispatch of branches that were running when the old leader died.

### One writer for run state

The engine is the only process that changes runs. The management host asks: cancellation is a
`CancelWorkflowRun` command, public callbacks are an `InboundCallback` command, and manual runs and
signals are HTTP calls to the engine.

### Generation-stamped cache

`TriggerRegistrationCache` stamps each entry with the cache generation current when its read began.
`WorkflowDefinitionChangedEvent` bumps the generation, so a read that started before a publish is
never served after it. Entries also expire after a minute, which bounds a lost announcement. Only
"known empty" answers short-circuit the database; a cached hit is always re-read.

## Safety defaults

* **Fail closed.** A filter that throws is no match. Every engine endpoint except the health probes
  needs the API key, including controllers someone forgets to mark; in Production an unset key
  makes every call fail (`ENGINE_API_KEY_NOT_CONFIGURED`) rather than open the API.
* **Secrets out of payloads.** Webhook secrets are stored hashed and compared in fixed time;
  integration secrets are sealed (AES-256-GCM, bound to workspace, RefId and type), resolved fresh per
  send, and scrubbed from error messages. `RunStarted` is journaled without the trigger body.
* **Outbound guard.** Webhook steps refuse non-HTTP(S) and private or reserved addresses
  (`OutboundAddressGuard`), follow no redirects, cap the response, and send an integration's
  credential only to its own origin.
* **Bounded everything.** Fan-out, branch state size, sub-workflow depth, credits, active runs per
  workspace, queue age and history age all have limits ([Features.md §15](Features.md#15-limits)).
* **Reject what is not implemented.** Settings the runtime does not honour are refused at publish
  (`COMPENSATION_NOT_SUPPORTED`) rather than silently ignored.
