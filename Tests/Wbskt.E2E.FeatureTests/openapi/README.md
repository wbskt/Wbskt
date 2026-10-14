# OpenAPI snapshots

`auth.v1.json` and `management.v1.json` are the OpenAPI documents of the auth and management hosts,
as the running hosts serve them at `/openapi/v1.json` (in Development, where API docs are on),
normalized by `refresh.sh`: keys sorted with `jq -S`, and `servers` dropped, since it only names the
address the document was fetched from.

They are checked in so that adding, removing or reshaping a route shows up in review as a diff of
these files, and `Docs/API.Endpoints.md` follows them rather than the other way round.

## Refreshing

With the hosts running as Development (`start-hosts.sh` or `e2e-up.sh`):

```bash
Tests/Wbskt.E2E.FeatureTests/openapi/refresh.sh
```

rewrites both files. Commit them with the change that moved them. `OPENAPI_AUTH_URL` and
`OPENAPI_MANAGEMENT_URL` point it at hosts other than `http://localhost:5000` and `:5010`.

## In CI

The `e2e` job in `.github/workflows/build-images.yml` runs `refresh.sh --check` once the hosts are
up. It fails, printing a unified diff, when a host's document differs from its file here. The
documents it fetched are uploaded as the `openapi-snapshots` artifact on every run, so a failed run's
files can be committed as they are.

## Bootstrap

These snapshots cannot be produced without running the hosts, so the files were not committed with
the check. Until a file exists, `--check` writes it from the running host, prints
`::warning::snapshot created, commit it`, and does not fail: the first CI run produces both files
(in the `openapi-snapshots` artifact), and the diff gates from the run after they are committed here.
