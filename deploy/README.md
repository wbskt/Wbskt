# Deployment assets

Docker-on-VMs deployment for Wbskt (K8s-ready images, no K8s yet). See
`Docs\Workflow.Engine.V3.Remediation.Plan.md` for the engine remediation prerequisite before
running the engine with 2 replicas (Phase 4 below).

## Layout

- `docker/Dockerfile` — one parameterized multi-stage image for all 4 ASP.NET Core hosts
  (`HOST_PROJECT` / `HOST_DLL` build args).
- `docker/Dockerfile.migrator` + `docker/migrate.sh` — builds both DACPACs and publishes them
  with `sqlpackage`. `..\..\Deploy-Databases.ps1` stays the Windows-dev path; this is the
  container path. Defaults to an incremental publish (safe to rerun against a database with
  real data); `MIGRATE_FRESH=true` drops and recreates instead — see Database migrations below.
- `console/` — nginx image serving the Angular console's production build (separate repo; see
  `console/Dockerfile` for the build-context contract).
- `compose/docker-compose.yml` + `compose/.env.example` — the full stack: Traefik, the 4 hosts,
  console, RabbitMQ, Redis, SQL Server, and a one-shot `migrator` (profile `migrate`).
- `config/serilog.json` — console-only Serilog sink, mounted at `/Config` (replaces the
  Windows-dev config, which also writes to a rolling file).

## Fresh install (brand-new VM)

Prerequisites you handle outside this repo: provision the VM, point DNS (`auth.<domain>`,
`api.<domain>`, `ws.<domain>`, `console.<domain>`, or one wildcard `*.<domain>`) at its IP, and
open inbound 22/80/443 in the cloud firewall. If the VM has under ~4GB RAM, add swap first
(`fallocate` a few GB, `mkswap`, `swapon`, persist in `/etc/fstab`) — SQL Server + RabbitMQ +
Redis + 4 hosts + Traefik is not a light stack.

```
# Docker
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER   # log out/in, or keep using sudo

# Get the code onto the VM (git clone, or scp/tar the working tree), then:
cd deploy/compose
cp .env.example .env
# fill in DOMAIN, ACME_EMAIL, and generate real secrets for SQL_SA_PASSWORD, RABBITMQ_PASSWORD,
# JWT_KEY, ENGINE_INBOUND_API_KEY, CONSOLE_ORIGIN. Consider setting ACME_CASERVER to the
# Let's Encrypt staging directory first (see .env.example) to avoid burning production rate
# limits while you're still iterating; drop it once things look right.

docker compose up -d sql                                    # wait for it to report healthy
docker compose --profile migrate build migrator
MIGRATE_FRESH=true docker compose --profile migrate run --rm migrator   # first run only - creates the DBs

docker compose build auth management socket engine
docker compose up -d rabbitmq redis
docker compose up -d auth management socket engine traefik
docker compose ps                                            # everything should report healthy
```

Once you're happy with staging certs, switch to production: remove `ACME_CASERVER` from `.env`,
then `docker compose stop traefik && docker run --rm -v wbskt_traefik-acme:/letsencrypt busybox rm -f /letsencrypt/acme.json && docker compose up -d traefik`
(the old ACME account/cert state is staging-only, so it needs clearing before Traefik will
register fresh against production).

Console is separate — only relevant once you have a built `dist/` to drop into `console/`, then
`docker compose build console && docker compose up -d console`.

## Incremental deployment (already running, you've changed something)

```
# Get the updated source onto the VM (git pull, or re-transfer changed files)

# If a DACPAC/SQL script changed - see Database migrations below, this is always safe to rerun
docker compose --profile migrate build migrator
docker compose --profile migrate run --rm migrator          # no MIGRATE_FRESH - incremental by default

# Rebuild + recreate only the host(s) that actually changed
docker compose build auth                                    # e.g. only Auth changed
docker compose up -d --no-deps auth
```

`--no-deps` stops Compose from also restarting things `auth` depends on that haven't changed.
For a scaled tier (`socket`, `engine`), `docker compose up -d --scale socket=3 socket` recreates
*all* replicas at once — plain Compose has no rolling-update primitive like Swarm/K8s, so there's
brief downtime for that tier unless you script "bring up one new, remove one old, repeat"
manually. Traefik's Docker provider watches for label changes on its own, so it only needs a
restart if you changed Traefik's *own* static config (entrypoints, ACME settings, etc.), not for
routine host redeploys.

Scale a tier with `docker compose up -d --scale socket=3` (Phase 1) or `--scale engine=2`
(Phase 4, after the remediation prerequisite). Engine/RabbitMQ/Redis/SQL are backend-network-only
and are never published by Traefik.

## Database migrations

`docker/migrate.sh` runs `sqlpackage /Action:Publish` for both databases, which diffs the DACPAC
against the live schema and applies only the delta — additive changes (new tables, sprocs,
columns) land without touching existing data. This is what makes it safe to rerun on every
incremental deploy, including against a database with real rows in it:

```
docker compose --profile migrate run --rm migrator          # incremental - default, always safe
```

`MIGRATE_FRESH=true` instead drops and recreates both databases from scratch:

```
MIGRATE_FRESH=true docker compose --profile migrate run --rm migrator
```

Only ever set `MIGRATE_FRESH=true` for a brand-new environment. Running it against a database
you want to keep destroys all data in it — there's no confirmation prompt, so treat this the same
as `DROP DATABASE`. Verified: rerunning the migrator without `MIGRATE_FRESH` against a database
with existing rows leaves them untouched while still applying schema changes.

## SQL placement

The `sql` service (container + volume) is the staging/dev path. For production, prefer a
VM-installed or managed SQL Server instance instead — the deciding factor is the backup/patching
story, not performance, since both DBs stay on one SQL instance regardless (no partitioning below
~5k devices; `WorkspaceId` is the future partition key). Either way it must be reachable as
`sql:1433` on the `backend` network so `ConnectionStrings__DefaultConnection` /
`ConnectionStrings__AuthDBConnection` don't need to change.

## Rollout phases + verification

Rollout phases (containerize as-is → socket fan-out → management ×2 → engine API key → engine
active/standby → polish) and their verification steps are tracked separately and run on the
target VM, not from this workstation.
