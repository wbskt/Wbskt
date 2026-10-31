# Workflow engine features

What a workflow author can build, and the rules the engine applies. For how it runs, see
[Architecture.md](Architecture.md).

## 1. Definitions

A workflow definition is JSON (camelCase). Each publish stores a new immutable version.

| Field | Meaning |
|---|---|
| `workflowRefId`, `version` | Identity. `version` is assigned at publish. |
| `name`, `description`, `isEnabled` | Display and on/off. |
| `nodes` | Each node has `nodeId` (GUID), `kind`, `name`, `ports` and a kind-specific `config`. Action nodes may add `retry` and `onFailure`. |
| `edges` | `{ "from": [nodeId, portId], "to": [nodeId, "in"] }`. At most one edge leaves each output port. |
| `sharedVariableSchema` | Declared shared variables: `name`, `type` (`Counter`, `Number`, `String`, `Bool`, `Json`), `default`. |
| `failFast` | When true, the first branch failure fails the whole run. |
| `creditBudget` | Credits this run may spend; default 10 000. |
| `runCompensationOnFailure` | **Not supported.** Publishing with it set is rejected (see [§7](#7-failure-handling)). |

A node's ports must match its kind exactly (`MISSING_PORT`, `EXTRA_PORT`): the expected ports come
from the kind's descriptor in `NodeKinds`. C# code and tests build definitions with
`WorkflowBuilder` (`Wbskt.Workflow.Builder`), which wires ports and edges for you.

## 2. Node catalogue

### 2.1 Triggers

Every run starts at a trigger node. Triggers are free and have one output port, `default`. They
copy the inbound payload into the branch as `trigger` and stamp `triggeredAt`.

| Kind | Config | Starts a run when |
|---|---|---|
| `trigger:client` | `clientRef`, `type` (`*` for any), `correlationKey`, `concurrencyPolicy` (default `Queue`), `filter`, `holdSeconds` | A device sends a message of that type. With `holdSeconds` > 0 it fires only once `filter` has kept matching for that long (needs a filter; at most 24 h), and stays quiet until a message stops matching. |
| `trigger:clientPresence` | `clientRef`, `state` (`Online`/`Offline`), `forSeconds` (default 60, max 24 h), `correlationKey`, `concurrencyPolicy` | A device has stayed in that state for `forSeconds`. |
| `trigger:schedule` | `cron`, `correlationKey` | The cron expression fires (UTC). |
| `trigger:webhook` | `path`, `method`, `correlationKey`, `concurrencyPolicy`, `filter`, `secret` | `POST /api/callbacks/webhook/{workspaceRef}/{path}` on the management host. With a `secret`, the caller must send it in `X-Wbskt-Secret`. |
| `trigger:manual` | `description` | A user starts it from the console or API (`POST .../workflows/{workflowRef}/runs`, with an optional idempotency key). |

### 2.2 Controls

| Kind | Credits | Ports | Config and behaviour |
|---|---|---|---|
| `control:logic` | 0.1 | `true`, `false` | `condition` (an expression). |
| `control:foreach` | 0.1 | `body`, `done` | `collection` (a branch-state path). Sequential: each visit sets `item` and `index` and leaves by `body`; the body leads back to this node; after the last item it leaves by `done`. |
| `control:parallelForEach` | 0.1 | `body`, `empty` | `collection`. One child branch per item (at most 1 000), each with `item`; must reach a join. An empty collection leaves by `empty`. |
| `control:fork` | 0.1 | one per branch name | `branches` (names). One child branch per port. |
| `control:join` | 0.1 | `default` | `mode` (`All`, `Any`, `Quorum`), `quorumCount`. See [§8](#8-forks-and-joins). |
| `control:variable` | 0.1 | `default` | `scope` (`Local`/`Shared`), `op`, `var`, `value`, `expected`. See [§10](#10-variables). |
| `control:delay` | 0.1 | `default` | `duration`. Parks the branch on a timer. |
| `control:awaitSignal` | 0.1 | `default` (+ `timeout` when `ttl` is set) | `signalName`, `correlation` (defaults to the run's RefId; placeholders filled in, see below), `ttl`. Waits for `POST .../runs/{runRef}/signals/{signalName}`; the signal body lands in `signalPayload`. |
| `control:waitForHttp` | 0.1 | `default`, `timeout` | `ttl` (required), `token` (required, at least 24 characters: it is the only thing guarding the public URL). Waits for `POST /api/callbacks/wake/{token}` on the management host; the body lands in `wakePayload`. |
| `control:subWorkflow` | 1.0 | `default` | `workflowRefId`, `correlationKey`, `input` (name → expression). See [§9](#9-sub-workflows). |
| `control:end` | 0.1 | none | Ends the branch successfully. Optional `output` expression becomes the run's output. |
| `control:failRun` | 0.1 | none | `reason`. Fails the whole run. |

### 2.3 Actions

Action nodes accept `retry` and `onFailure` ([§6](#6-retries), [§7](#7-failure-handling)).

| Kind | Credits | Ports | Config and behaviour |
|---|---|---|---|
| `action:clientMessage` | 1.0 | `default`; or `acked`, `failed`, `timeout` with `awaitAck` | `clientRef` (the device's RefId), `type`, `payload` (JSON), `awaitAck`, `ackTimeout` (default 30 s, max 24 h). With `awaitAck` the step parks, then sends the command, and leaves by what became of it; the outcome lands in `command`. A command to an offline, registered device waits in its queue until it connects or the ack timeout passes, so the step leaves by `timeout` if the device stays away; a full queue or an inactive device counts as `failed` at once. |
| `action:toast` | 0.5 | `default` | `title`, `message`. A notification to everyone watching the workspace in the console. |
| `action:webhook` | 5.0 | `default`, `error` | `url`, `method`, `headers` (max 20), `body`, `timeoutSeconds` (default 30, max 60), optional `integrationRef` (an HTTP-auth integration). Writes `status`, `body` and, for a JSON reply, `json`. Responses over 256 KB fail. Redirects are not followed, private and reserved addresses are refused, and an integration's credential is only sent to its own origin. 5xx, timeouts and network errors are retryable; 4xx is not. |
| `action:email` | 5.0 | `default` | `integrationRef` (SMTP), `to`, `subject`, `body`. |
| `action:telegram` | 5.0 | `default` | `integrationRef`, `chatId` (a numeric id or `@channelusername`), `message` (1-4096 characters), `parseMode` (`MarkdownV2` or `HTML`; left out, plain text), `silent`, `messageThreadId` (a forum topic), `disableLinkPreview`. Writes `telegram` = `{ messageId, chatId }`, as Telegram's numbers. A 403 is `TELEGRAM_CHAT_FORBIDDEN` (the bot was blocked or removed), not retryable; 429 is retryable. The integration's `GET .../integrations/{ref}/chats` lists the chats the bot heard from in the last 24 hours. |
| `action:discord` | 5.0 | `default` | `integrationRef`, `message`, `username`, `avatarUrl`, `threadId`, `embeds` (up to 10: `title`, `url`, `description`, `color` as `#RRGGBB`, `fields` of `name`/`value`/`inline`, `footer`, `imageUrl`, `thumbnailUrl`, `timestamp` to show the send time). `message` may be left out when there is an embed. Mentions are never parsed. Waits for Discord to save the message and writes `discord` = `{ messageId, channelId }`. 429 is retryable. |
| `action:ntfy` | 5.0 | `default` | `integrationRef`, `message`, `title`, `priority`, `tags`. |

Action text fields take `{{ path }}` and `{{ $shared.NAME }}` placeholders, filled in from the
branch when the step runs, with the same resolution as a `template` expression (§12):

| Step | Templated fields |
|---|---|
| `action:email` | `to`, `subject`, `body` |
| `action:telegram` | `chatId`, `message` |
| `action:ntfy` | `message`, `title`, `tags` |
| `action:discord` | `message`, `username`, `avatarUrl`, `threadId`; each embed's `title`, `url`, `description`, `footer`, `imageUrl`, `thumbnailUrl` and field `name`/`value` |
| `action:toast` | `title`, `message` |
| `action:webhook` | `url`, header values, `body` |
| `action:clientMessage` | `payload` |

In a JSON field (`payload`, webhook `body`) every string is filled in; property names are not. A
string that is exactly one placeholder takes the value's own JSON type, so `{ "on": "{{heaterOn}}" }`
sends `true`, not `"true"`. A placeholder with no value is sent as empty text and the step still
runs; the run's history gets a `TemplateValueMissing` warning naming the field and the placeholder
(never a value). Line breaks a value brings into `to`, `subject`, a URL, a header or an id become
spaces, so a value cannot add a header. The webhook's address checks run on the filled-in URL, and
its failure messages quote the URL as written. An unclosed or empty placeholder is refused when the
workflow is saved (`TEMPLATE_INVALID`, naming the field); one in a definition saved earlier fails the
step with `EXPRESSION_EVALUATION_ERROR`. `clientRef`, `type` and other settings are used as written.

Wait for signal's `correlation` takes the same placeholders, filled in as one line when the step
parks, so `{{trigger.payload.parentRun}}` waits on that run's signal rather than on the placeholder
text every run would share. It is a key, not a message, so a missing value is not sent as blank: the
step fails with `SIGNAL_CORRELATION_MISSING` (not retryable), as it does when the value fills in to
blank text. On save, `AWAIT_SIGNAL_CORRELATION_INVALID` refuses a correlation that could never fill in
to a value of its own: a brace outside a placeholder, a `$trigger.` path written without `{{ }}`, or a
placeholder that is not a dotted path or `$shared.NAME`. The signal endpoint is addressed by run, so
the filled-in value has to be a run's RefId for a signal to reach it.

Which fields take placeholders is declared once, with `[Templated]` on the config property
(`[Templated(TemplateText.Line)]` for a header, URL or id). The engine's `ActionTemplates` and the
validator both read it through `TemplateFields`, so a new step's config only needs the attribute and
one `FillAsync` call in its executor. The warnings go on the step's `NodeContext.Warnings`; the retry
executor passes on those of the attempt that decided the step, and the branch loop writes them before
the step's outcome event.

## 3. Branch state

Each branch carries a JSON object. It starts as `{ "trigger": <payload> }`; each node's result
patches it. Keys nodes write: `trigger`, `triggeredAt`, `item`, `index`, `status`, `body`, `json`,
`command`, `discord`, `telegram`, `signalPayload`, `wakePayload`, `childResult`, `casSucceeded`, `lastError`,
`joinContributed`, `joinSucceeded`, `joinFailed`, and any local variable.

* Keys starting with `__` are the engine's own (park markers, join tokens, retry attempt, loop
  iterators). A variable may not use one (`VARIABLE_NAME_RESERVED`).
* A branch's state may not exceed 256 KB (`BRANCH_STATE_TOO_LARGE`).
* Child branches of a fork or parallel loop start with a copy of the parent's state.

## 4. Triggers in depth

### 4.1 Correlation

Each run has a correlation key. A trigger's `correlationKey` is either a `$trigger.<path>` into the
payload or a constant. The concurrency policy groups runs by workflow and correlation key. With no
`correlationKey`, the trigger's channel supplies one: device and message type for a device trigger,
device and state for presence, the path for a webhook. So a policy always has a group to apply to.

### 4.2 Concurrency policies

When a trigger matches while a run with the same workflow and correlation key is active:

| Policy | Effect |
|---|---|
| `AllowParallel` | Start another run. |
| `DropIfRunning` | Drop the event. |
| `Queue` (default) | Store the event; when a run finishes, the oldest queued event starts the next run. Queued events expire after 24 h. |
| `CancelExisting` | Cancel every matching run, then start a new one. |

### 4.3 Filters and secrets

* A `filter` is an expression over the payload. A filter that errors counts as no match: filters
  fail closed.
* A webhook `secret` is stored hashed. The caller's `X-Wbskt-Secret` is compared in fixed time.

### 4.4 Delivery guarantees

* An event delivered twice starts at most one run per registration (the trigger-start key is
  unique on the run).
* An event delivered twice wakes a waiting branch at most once (the inbound-event idempotency key).
* Schedule occurrences missed while no leader was running are fired, one after another, when a
  leader returns.

## 5. Waiting

Delay, await-signal, wait-for-HTTP, sub-workflow and acknowledged client messages all **park**:
the branch writes a bookmark and frees its worker, so a workflow may wait minutes or days at no
cost. Parked waits survive restarts and failover.

* A wait with a `ttl` (or `ackTimeout`) leaves by `timeout` when the time is up, without running the
  node again.
* A wake that arrives for an earlier visit of the same node is ignored, and the node keeps waiting
  for its remaining time.
* A waiting branch whose bookmark has vanished is failed with `BOOKMARK_LOST` once two sweeps past
  the 5-minute threshold have seen it unchanged, rather than left waiting forever.

## 6. Retries

An action node's `retry` policy:

| Field | Meaning |
|---|---|
| `strategy` | `Constant`, `Linear` or `Exponential`. |
| `initialDelay` | First delay. |
| `factor` | Growth for `Exponential` (default 2). Must be > 0 (`RETRY_FACTOR_INVALID`). |
| `maxDelay` | Ceiling, applied after jitter. Must be ≥ `initialDelay` (`RETRY_MAX_DELAY_BELOW_INITIAL`). |
| `maxAttempts` | Total attempts including the first. |
| `jitterPct` | Random spread in percent. |
| `retryOn` | Error codes or exception type names to retry; empty means any retryable failure. |

Only failures the node marks retryable are retried (for example a webhook 5xx). Each attempt is
charged. A backoff of 5 s or more parks the branch on a timer, so it survives restarts and holds no
worker. Every attempt is journaled as `NodeRetrying`.

## 7. Failure handling

When a node fails (after its retries), its `onFailure` decides what happens:

| `outcome` | Effect |
|---|---|
| (none) / `FailBranch` | The branch ends Failed. Other branches continue. |
| `FailRun` | The whole run fails: every branch is stopped and child runs are cancelled. |
| `ContinueOnError` | Leave by the node's `error` port (the node must have one, `CONTINUE_ON_ERROR_WITHOUT_ERROR_PORT`). |
| `ContinueAsSucceeded` | Leave by `default` as if it had succeeded. |
| `JumpToNode` | Continue at `targetNodeId` (must exist, `ONFAILURE_TARGET_NOT_FOUND`). |
| `Compensate` | **Not supported**; rejected at publish (`COMPENSATION_NOT_SUPPORTED`). Tracked in #377. |

The continuing outcomes write `lastError` (`errorCode`, `message`) into the branch.

Run-level switches:

* `failFast: true` fails the run on the first branch failure.
* A run that spends its whole credit budget ends `OutOfCredits`.
* A run that is still open after 30 minutes but has no branch that can make progress (nothing
  active, nothing waiting on a bookmark) is failed by the reaper (`REAPER_TIMEOUT`).

How the run's final status is chosen from its branches is in
[Architecture.md §5.5](Architecture.md#55-ending-branches-and-runs). In short: all branches
succeeded → `Succeeded`; some failed and some succeeded → `PartiallyFailed`; all failed or the run
was failed → `Failed`; cancelled → `Cancelled`.

## 8. Forks and joins

* A **fork** starts one branch per named port; a **parallel for-each** starts one branch per item.
  These branches form a **cohort**.
* A **join** downstream at the same nesting level collects the cohort. Its `mode`:
  * `All`: continue once every branch has arrived.
  * `Any`: continue on the first branch to arrive.
  * `Quorum`: continue once `quorumCount` branches have arrived.
* A branch that **fails** still counts as arriving, as a failure: the join can converge, and
  `joinFailed` tells the next node how many failed. When the deciding arrival is a failure, a fresh
  continuation branch carries on past the join.
* A branch that **ends without reaching the join** (an `end` node, or a port with no edge) declines.
  With `All` the join can no longer converge, so the run fails (`JOIN_BRANCH_ENDED`). With `Any` and
  `Quorum` the join carries on without it, and the run fails the same way only once the remaining
  branches can no longer meet the join.
* Exactly one branch continues past the join, carrying `joinContributed`, `joinSucceeded` and
  `joinFailed`. The others end there.
* Joins nest: a fork inside a fork branch has its own join.
* A **fork without a join** is allowed as fire-and-forget fan-out, but every branch of a fork must
  either all reach the same join or none (`FORK_BRANCH_WITHOUT_JOIN`, `FORK_BRANCHES_JOIN_MISMATCH`).
  A parallel for-each always needs a join (`PARALLEL_FOREACH_WITHOUT_JOIN`), and a join must have a
  cohort to collect (`JOIN_WITHOUT_COHORT`).

## 9. Sub-workflows

`control:subWorkflow` starts a run of another workflow and waits for it.

* `input` maps names to expressions; the child's trigger payload is those values. The names
  `parentRunRefId`, `correlationKey` and `depth` are reserved (`SUBWORKFLOW_INPUT_KEY_RESERVED`).
* A workflow cannot call itself (`SUBWORKFLOW_SELF_REFERENCE`); nesting is limited to 8 levels
  (`SUBWORKFLOW_DEPTH_EXCEEDED`).
* The child is started through its manual trigger. Its `control:end` `output` (allowed only in
  workflows with a manual trigger, `END_OUTPUT_WITHOUT_MANUAL_TRIGGER`) comes back to the parent as
  `childResult` = `{ childRunRefId, status, output }`.
* Cancelling or failing the parent cancels the child.
* The child spends its own credit budget; the parent pays 1.0 to start it.

## 10. Variables

`control:variable` reads and writes two kinds of variable:

| Scope | Lives in | Operations |
|---|---|---|
| `Local` | The branch's state | `Set` |
| `Shared` | `SharedVariables`, one value per workflow (all versions and runs) | `Set` (last writer wins), `Increment` / `Decrement` (atomic, counters only, `VARIABLE_NOT_A_COUNTER` otherwise), `CompareAndSet` (writes `value` only if the current value equals `expected`; sets `casSucceeded`) |

`value` is a JSON literal or an expression object. Expressions read shared variables with
`sharedVariableRef` or `{{ $shared.NAME }}` in a template. Shared variables can also be set by hand
from the console. A declaration's `resetPolicy` is accepted in the schema but not acted on yet.

## 11. Credits

Every run has a budget (definition `creditBudget`, else 10 000). Each node attempt is charged its
kind's price before it runs; triggers are free and resuming a parked node is free. Prices are in the
tables above and can be overridden per kind in configuration (`WorkflowEngine:CreditCosts`). When the
budget runs out the node fails with `OUT_OF_CREDITS`, the run is cancelled and ends `OutOfCredits`.

## 12. Expressions

Expressions are JSON objects with a `kind`:

| `kind` | Fields | Evaluates to |
|---|---|---|
| `literal` | `value` | The value. |
| `branchStateRef` | `path` | A dotted path into branch state, falling back to the trigger payload. |
| `sharedVariableRef` | `name` | A shared variable. |
| `template` | `template` | A string with `{{ path }}` or `{{ $shared.NAME }}` placeholders; a missing value renders empty. |
| `jsonPath` | `baseExpression`, `path` | A JSONPath query into another expression's result. |
| `member` | `object`, `member` | A member of another expression's result. |
| `binary` | `left`, `operator`, `right` | `Equal`, `NotEqual`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `And`, `Or`. |
| `unary` | `operator`, `operand` | `Not`, `Negate`. |
| `function` | `function`, `arguments` | `Now`, `UtcNow`, `ToString`, `ToLower`, `ToUpper`, `Contains`, `StartsWith`, `EndsWith`, `IsNull`, `IsEmpty`. |

Types are strict: no implicit coercion between strings, numbers and booleans. `And` and `Or`
short-circuit. A logic condition saved as a bare string is read as a branch-state path (or a
`true`/`false` literal) for backward compatibility, and is rewritten in structured form on the next
publish. An expression error fails the node with `EXPRESSION_EVALUATION_ERROR`.

## 13. Validation

`WorkflowValidator` runs at publish. Errors block the publish; warnings do not.

| Area | Codes |
|---|---|
| Structure | `NULL_DEFINITION`, `INVALID_WORKFLOW_REF_ID`, `INVALID_VERSION`, `DUPLICATE_NODE_ID`, `UNKNOWN_NODE_KIND`, `EDGE_UNKNOWN_NODE`, `EDGE_UNKNOWN_PORT`, `DUPLICATE_PORT_EDGE`, `MISSING_PORT`, `EXTRA_PORT` |
| Failure handling | `COMPENSATION_NOT_SUPPORTED`, `ONFAILURE_TARGET_NOT_FOUND`, `CONTINUE_ON_ERROR_WITHOUT_ERROR_PORT`, `RETRY_FACTOR_INVALID`, `RETRY_MAX_DELAY_BELOW_INITIAL` |
| Fan-out and joins | `PARALLEL_FOREACH_WITHOUT_JOIN`, `FORK_BRANCH_WITHOUT_JOIN`, `FORK_BRANCHES_JOIN_MISMATCH`, `JOIN_WITHOUT_COHORT` |
| Sub-workflows and end | `SUBWORKFLOW_INPUT_KEY_RESERVED`, `SUBWORKFLOW_SELF_REFERENCE`, `END_OUTPUT_WITHOUT_MANUAL_TRIGGER` |
| Variables and waits | `VARIABLE_NAME_RESERVED`, `WAITFORHTTP_WEAK_TOKEN` |
| Webhook step | `WEBHOOK_URL_RELATIVE`, `WEBHOOK_TIMEOUT_INVALID`, `WEBHOOK_HEADER_INVALID`, `WEBHOOK_HEADER_FORBIDDEN`, `WEBHOOK_HEADERS_TOO_MANY` |
| Other steps | `INTEGRATION_REF_MISSING`, `TELEGRAM_CHAT_MISSING`, `TELEGRAM_MESSAGE_INVALID`, `TELEGRAM_PARSE_MODE_INVALID`, `TELEGRAM_THREAD_INVALID`, `DISCORD_MESSAGE_INVALID`, `DISCORD_EMBED_INVALID`, `DISCORD_THREAD_INVALID`, `DISCORD_AVATAR_URL_INVALID`, `NTFY_MESSAGE_MISSING`, `NTFY_PRIORITY_INVALID`, `CLIENT_MESSAGE_INVALID`, `CLIENT_MESSAGE_ACK_TIMEOUT` |
| Triggers | `INVALID_CLIENT_REF`, `INVALID_CRON`, `INVALID_PRESENCE_DURATION`, `INVALID_HOLD_DURATION`, `HOLD_REQUIRES_FILTER` |
| Warnings | `NO_TRIGGERS`, `ORPHAN_NODE`, `SUSPICIOUS_CORRELATION_KEY` |

The management host also refuses a publish that references an integration the workspace does not
have.

## 14. Integrations

Credentials for outbound steps are stored as workspace **integrations**: `Telegram`, `Discord`,
`Smtp`, `Ntfy` and `HttpAuth` (a base URL plus an auth header, with a Home Assistant preset).

* Secrets are sealed by the engine with AES-256-GCM, bound to the workspace, the integration's RefId
  and its type, so a sealed blob cannot be moved to another integration.
* Steps resolve the integration fresh on every send, so a rotated secret takes effect immediately.
* Error messages are scrubbed of secret values before they reach history.
* The console's "test" button calls the engine to probe the integration.

## 15. Limits

| Limit | Value |
|---|---|
| Workflows per workspace | 100 |
| Active runs per workspace | 200 (child runs, queue replays and `CancelExisting` replacements exempt) |
| Default credit budget per run | 10 000 |
| Parallel for-each items | 1 000 |
| Branch state | 256 KB |
| Sub-workflow depth | 8 |
| Webhook response | 256 KB; timeout ≤ 60 s; ≤ 20 headers |
| Hold and presence durations, ack timeout | ≤ 24 h |
| Wait-for-HTTP token | ≥ 24 characters |
| Queued trigger events | kept 24 h |
| Open run with no live branch | failed after 30 min |
| History | Info 30 days, Warn/Error 365 days |

## 16. Not supported yet

Recorded so nobody mistakes an absence for a bug.

| Gap | Today |
|---|---|
| Compensation (undo steps on failure) | Rejected at publish with `COMPENSATION_NOT_SUPPORTED`. Tracked in #377. |
| A concurrency window on parallel for-each | Only the hard ceiling: more than 1 000 items fails the node with `PFE_FAN_OUT_TOO_LARGE` before anything is written. Every item's branch is started at once. |
| Expressions in action fields | Message text, URLs, payloads and `clientRef` are used as written. |
| Shared-variable `resetPolicy` | Accepted in the schema (`none`, `dailyAtUtc`, `onDefinitionPublish`) but never applied. |
| A durable record of inbound events that start no run | The dispatch outcome tells the caller why nothing ran (`Filtered`, `NoRegistration`, ...), but nothing is stored. |
