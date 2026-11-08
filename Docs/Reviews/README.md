# Architecture reviews

A full architecture review of every Wbskt component and the infrastructure runs **every 5th day**,
but only when something was committed since the last one. This file is the brief the review follows.
Change the lanes or checks here, not in the scheduled task that starts it.

Reports live beside this file as `YYYY-MM-DD.md`, one per review. The newest report's date is the
"last review" date. Older reviews, written before this routine, are in the project files
(`full-arch-review/`, `workflow-review/`, `db-review/` and others).

## 1. Decide whether to run

1. Take the date `D` of the newest `Docs/Reviews/YYYY-MM-DD.md` (none yet: 2026-11-02, the last
   full review, wbskt/Wbskt#537).
2. List commits on `master` since `D` in wbskt/Wbskt, wbskt/Wbskt.Console, wbskt/Wbskt.Arduino and
   richardjoy530/fluffy-waddle.
3. **No commits anywhere: stop.** Write nothing and file nothing. Otherwise carry on, and note which
   areas changed; those get the deep pass.

## 2. Lanes

Run the six lanes in parallel. Each lane reviews everything changed since `D` in depth, then takes a
lighter pass over the rest of its area for drift. Every lane looks for: inconsistencies between
components, missed or misused design patterns, dead or obsolete code, edge cases, performance, and
breaks from [Coding.Conventions.md](../Coding.Conventions.md) (console:
`CODING_GUIDELINES.md`).

| Lane | Area | Also checks |
|---|---|---|
| Auth and management | `Hosts/Wbskt.Auth.Host`, `Hosts/Wbskt.Management.Host`, `Wbskt.Infrastructure`, `Models`, `Events` | `Result` vs fault handling, caching and invalidation, SignalR events and their console listeners, background jobs that run on every replica, rate limits |
| Gateways and SDKs | `Wbskt.Gateway.Core`, gateway hosts, `Clients/` (.NET, Python), wbskt/Wbskt.Arduino | protocol and close codes match the docs in `Docs/`, conformance drift, reconnect and offline buffer behaviour, token handling |
| Workflow engine | `Wbskt.Workflow*`, the engine host | run lifecycle, leases and leader election, retries and idempotency, waits and long-running runs, node contracts vs the console designer |
| Database | `Databases/` | see "Database checks" below |
| Console | wbskt/Wbskt.Console | feature-service structure, calls to endpoints that changed or no longer exist, unversioned API calls, dead components, style budgets, live-update handlers |
| Infra and CI | `deploy/`, `.github/workflows/`, `Deploy-Databases.ps1` | compose services, networks, ports, env vars and secrets, deploy order and health waits, CI time and skipped checks, telemetry and alerts. Compare against [deploy/infra-map.html](../../deploy/infra-map.html) and list every place the map is wrong |

### Database checks

- **Unused stored procedures.** List every `CREATE PROCEDURE` under `Databases/`. Search the C# for
  each name (providers call them by string). A procedure with no caller is a finding; the fix drops
  it in the project and in the pre-deployment script, as #573 did.
- **Indexes.** For each table, list its indexes and the queries that touch it. Flag indexes no query
  can use, duplicates and left-prefix overlaps, foreign keys with no supporting index, and hot
  queries that scan. Read the actual predicate, not the index name.
- **Deadlocks.** For every procedure or C# transaction that writes more than one table, write down the
  order it takes locks. Flag any two paths that take the same tables in opposite orders, reads
  followed by writes without `UPDLOCK`, and transactions held across a network call.
- **Production numbers.** When the export from wbskt/Wbskt#619 exists, read index usage, missing-index
  hints and deadlock graphs from it; a finding backed by production numbers outranks a code guess.
  Until then, say in the report that index and deadlock findings come from code alone.

## 3. Verify

Each lane checks its own findings before handing them on. A finding needs a file and line, a concrete
scenario (input or state, then what goes wrong), and a second read that confirms it. Drop anything
that doesn't survive. Search open issues in both repos first and mark findings already filed.

## 4. Merge, rank and report

One final step merges the lanes:

1. Remove duplicates across lanes.
2. Rank: **P1** breaks something users see or loses data; **P2** should land before going public;
   **P3** is cleanup or post-launch.
3. Write `Docs/Reviews/YYYY-MM-DD.md` in the format below, and copy it to
   `arch-reviews/` in the project files.

## 5. Act

- Open one PR with the report (`docs(reviews): architecture review YYYY-MM-DD`), following
  [CONTRIBUTING.md](../../CONTRIBUTING.md).
- File an epic **"Architecture review (D Mon): follow-ups"** in wbskt/Wbskt with one sub-issue per new
  finding, filed without waiting. Console findings go to wbskt/Wbskt.Console and are attached as
  sub-issues too. Milestone: P1 to the current milestone, P2 to M3 Go public, P3 to Post-launch.
- Small, safe fixes (dead code, a missing index, a doc mismatch) can go in their own PRs. Anything
  larger waits for the owner.
- Update the roadmap's Reviews data
  ([Docs/Roadmap/wbskt-backend-roadmap.html](../Roadmap/wbskt-backend-roadmap.html)) and, when the
  infra lane found errors, the infra map, then republish both pages.

## Report format

```markdown
# Architecture review, YYYY-MM-DD

Range: <D> to <today>. Commits reviewed: Wbskt N, Console N, Arduino N, site N.
Production numbers: <used | not available (code only)>.

## Summary
<three to five sentences: the most important findings and the overall trend>

## Findings
| # | P | Lane | Finding | Where | Issue |
|---|---|---|---|---|---|

### 1. <title>
**Where:** path:line · **Scenario:** … · **Fix:** …

## Infra map corrections
## Already filed (not re-filed)
```
