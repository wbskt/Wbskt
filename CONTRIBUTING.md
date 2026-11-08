# Contributing to Wbskt

How changes are written, committed and reviewed in this repo.

## Commit messages

Every commit subject, and every PR title, follows [Conventional Commits](https://www.conventionalcommits.org):

```
<type>(<scope>): <summary>

<body: why the change was needed, not a list of what changed>

Closes #N
```

- **type** is one of `feat`, `fix`, `perf`, `refactor`, `docs`, `test`, `chore`, `ci`, `build`.
  Use `chore(db)` for a schema-only change.
- **scope** names the part of the code the change touches (auth, management, gateway, engine, workflow, db, sdk, conformance, deploy, ci).
  Leave it out, as in `docs: …`, when a change spans the repo.
- **summary** is lower case, in the imperative, with no full stop, and under about 72 characters:
  `fix(engine): keep a drained backlog out of the message rate`.
- One logical change per commit. Squash fix-ups before review rather than leaving "fix review" commits.
- Older history also has `(feat): …` and unprefixed subjects. Don't copy them.

PRs are merged by rebase, so each commit lands on `master` exactly as written. That is why the
commits, not only the PR title, follow the format.

## Branches and merging

- Never create a merge commit. Rebase onto `master` to catch up, and merge PRs with rebase (or squash).
- Never force-push a branch someone else owns or `master` itself.

## Pull requests

1. Find the GitHub issue(s) the change addresses and link them in the body (`Closes #N`, or `Refs #N`
   when it only does part of the work). Open an issue first if there is none.
2. Run the checks below before pushing.
3. **Review it again once it's open.** Read the full diff on GitHub as a fresh reviewer, not as the
   author: correctness and edge cases, concurrency and lock order, error handling, missing tests, and
   these conventions. Push fixes as follow-up commits, then add a line to the PR body:
   `Post-open review: <what was found and fixed, or "nothing found">`.
4. Wait for CI to pass, then ask for the merge.

## Checks before pushing

```bash
dotnet build Wbskt.slnx
dotnet test Tests/Wbskt.Host.Tests                              # unit tests, no infrastructure
dotnet test Tests/Wbskt.Workflow.Engine.Host.IntegrationTests   # when the engine or its SQL changes
```

- A change to a public contract also refreshes the OpenAPI snapshots
  (`Tests/Wbskt.E2E.FeatureTests/openapi/refresh.sh`); CI fails on a stale one.
- The end-to-end suite needs the hosts running (`Tests/Wbskt.E2E.FeatureTests/start-hosts.sh`).
  CI runs it on every PR that touches the hosts.
- Code follows [Docs/Coding.Conventions.md](Docs/Coding.Conventions.md).

## Docs that move with the code

- **Infra map** ([deploy/infra-map.html](deploy/infra-map.html)): update it in the same PR when a change
  touches hosts, services, networks, ports, env vars, CI or deploy flows, or telemetry. This file is the
  source; the copy in the project files mirrors it.
- **Backend roadmap** ([Docs/Roadmap/wbskt-backend-roadmap.html](Docs/Roadmap/wbskt-backend-roadmap.html)):
  update its data block when issues are filed, closed or re-milestoned, or PRs open or merge.
- **Architecture reviews** live in [Docs/Reviews](Docs/Reviews/README.md).
