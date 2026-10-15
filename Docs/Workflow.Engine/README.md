# Workflow engine

The workflow engine runs the automations a workspace publishes. A workflow is a graph of nodes:
**triggers** start a run, **controls** route, wait, fan out and join, and **actions** do something
in the outside world (message a device, call a webhook, send an email). This folder documents the
engine as it is on `master`. The code is the source of truth; when this folder and the code
disagree, fix this folder.

| Document | Read it when you want to know |
|---|---|
| [Architecture.md](Architecture.md) | How the engine host is put together: leadership, the inbound path, the branch pump and loop, persistence, recovery and the background workers. |
| [Features.md](Features.md) | What a workflow author can use: every node kind, triggers, retries, failure handling, joins, sub-workflows, variables, expressions, integrations, credits, limits and validation codes. |
| [Design.Patterns.md](Design.Patterns.md) | The recurring patterns the engine is built from, and the rule each one imposes on new code. |

The older design documents in `Docs/archive/` described an earlier engine and have been removed;
this folder replaces them.

## The engine in one page

```
devices ───────────► Socket host ──────┐
                                        ├──► RabbitMQ ──► Workflow Engine host ──┬──► SQL Server (all state, leader lease)
users, HTTP callers ─► Management host ─┘                  (never public)       └──► devices, webhooks, email, chat
```

* **Four hosts.** Auth, Management, Socket and the Workflow Engine. The engine is backend-only:
  everything the outside world sends reaches it through the management host or the message bus.
* **Publishing** happens in the management host. It validates the definition, stores it as a new
  version and writes the trigger registrations the engine matches events against.
* **One active engine.** Engine instances run active/standby. Only the instance holding the leader
  lease (a row in SQL) consumes messages, runs workflows and runs background workers.
* **Runs and branches.** An inbound event that matches a trigger starts a **run**. A run executes
  as one or more **branches**; each branch walks the graph one node at a time, carrying its own JSON
  **local state**. Forks and parallel loops create more branches; joins merge them back.
* **Waiting is durable.** A branch that must wait (a delay, a signal, an HTTP callback, a child
  workflow, a device ack, a long retry backoff) **parks**: it writes a **bookmark** row and frees its
  worker. A timer or a matching inbound event claims the bookmark and resumes the branch.
* **Everything is in SQL.** A crashed or replaced leader recovers by re-dispatching every active
  branch from the database. Nodes that are not safe to run twice are deduplicated with idempotency
  keys.

## Glossary

| Term | Meaning |
|---|---|
| Definition | One published version of a workflow: `workflowRefId` + `version`, nodes and edges. |
| Node kind | The type of a node, such as `trigger:client`, `control:fork`, `action:webhook`. |
| Port | A named exit of a node (`default`, `true`, `error`, a fork branch name). An edge joins one output port to the next node's `in`. |
| Run | One execution of a definition, with a status, a credit budget and a correlation key. |
| Branch | One line of execution inside a run, with its current node and local state. |
| Local state | The branch's JSON object. It starts as `{ "trigger": <payload> }` and nodes patch it. |
| Bookmark | A row that says "this branch is waiting for X until Y". Claimed by delete when X or Y happens. |
| Trigger registration | A row that says "events with this key start this workflow". Written at publish time. |
| Channel | One source of inbound events (client message, schedule, webhook, signal...). Each channel owns how its keys are built. |
| Cohort | The set of branches a fork or parallel loop started, which a join waits for. |
| Credits | The budget a run spends; each node kind has a price. |
