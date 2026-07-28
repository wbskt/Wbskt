# Deployment assets

Docker-on-VMs deployment for Wbskt (K8s-ready images, no K8s yet). See
`Docs\Workflow.Engine.V3.Remediation.Plan.md` for the engine remediation prerequisite before
running the engine with 2 replicas (Phase 4 below).

Images are built in GitHub Actions and pushed to GHCR; the VM only ever pulls them. It has no
inbound deploy endpoint — `.github/workflows/deploy.yml` SSHes in and runs `scripts/deploy.sh`
there. See [Build and release flow](#build-and-release-flow).

## Layout

- `docker/Dockerfile` — one parameterized multi-stage image for all 4 ASP.NET Core hosts
  (`HOST_PROJECT` / `HOST_DLL` build args).
- `docker/Dockerfile.migrator` + `docker/migrate.sh` — builds both DACPACs and publishes them
  with `sqlpackage`. `..\..\Deploy-Databases.ps1` stays the Windows-dev path; this is the
  container path. Defaults to an incremental publish (safe to rerun against a database with
  real data); `MIGRATE_FRESH=true` drops and recreates instead — see Database migrations below.
- The Angular console has no assets here at all — the [Dashboard](https://github.com/wbskt/Dashboard)
  repo owns its `Dockerfile`/`nginx.conf` and publishes `wbskt-console` itself. The `console`
  service below is the only reference to it, and it has no `build:` key.
- `compose/docker-compose.yml` + `compose/.env.example` — the full stack: Traefik, the 4 hosts,
  console, RabbitMQ, Redis, SQL Server, and a one-shot `migrator` (profile `migrate`). The 4 hosts
  and the migrator carry both `image:` (what the VM pulls) and `build:` (local development).
- `config/serilog.json` — console-only Serilog sink, mounted at `/Config` (replaces the
  Windows-dev config, which also writes to a rolling file).
- `scripts/deploy.sh` — runs **on the VM**: resets the checkout to the deployed commit, pulls the
  requested images, optionally migrates, restarts only the named services, waits for health.
- `scripts/ssh-forced-command.sh` — what the CI deploy key is pinned to in `authorized_keys`, so
  that key can only ever invoke `deploy.sh`.
- `..\..\.github\workflows\build-images.yml` — builds and pushes the 5 backend images to GHCR.
- `..\..\.github\workflows\deploy.yml` — manual (`workflow_dispatch`) deploy that SSHes to the VM
  and invokes `scripts/deploy.sh`.

## Build and release flow

Two repos, two independent release streams, one VM.

```
Wbskt      push to master ─> build-images.yml ─> ghcr.io/wbskt/wbskt-<service>:sha-<short>
                                                              │
                     you dispatch deploy.yml ────────────────-┘
                                  │ ssh
                                  v
             deploy.sh --tag sha-<short>
               git reset --hard <short>     # compose + config match the images
               docker compose pull && up -d --no-build --no-deps

Dashboard  push to master ─> build-and-deploy.yml ─> ghcr.io/wbskt/wbskt-console:sha-<short>
                                  │ ssh (automatic - static assets, no DB behind it)
                                  v
             deploy.sh --console-tag sha-<short>
               no checkout sync              # the tag is a Dashboard commit
               docker compose pull console && up -d --no-build --no-deps console
```

Five images per backend commit — `wbskt-auth`, `wbskt-management`, `wbskt-socket`, `wbskt-engine`,
`wbskt-migrator` — each tagged `sha-<short-commit>` and `latest`. The console is a sixth,
published on the Dashboard repo's own cadence and tracked by its own `CONSOLE_IMAGE_TAG`.

The backend deploy is a manual dispatch of the **Deploy** workflow with the tag from the build run:

| Input | Meaning |
|---|---|
| `tag` | `sha-abc1234` — also determines the commit the VM checkout is reset to |
| `services` | blank for all four hosts, or e.g. `auth management` |
| `run_migrations` | runs the *incremental* migrator first (never `MIGRATE_FRESH`) |

Rollback is just dispatching an older tag. Because the tag encodes the commit, the VM's
`docker-compose.yml` and `config/` roll back with it.

### One-time setup

On the VM:

```bash
git clone https://github.com/wbskt/Wbskt.git ~/Wbskt
cd ~/Wbskt/deploy/compose && cp .env.example .env    # then fill it in

# Read-only registry access. ghcr.io wants a *classic* PAT - fine-grained tokens have
# historically not worked against the container registry - scoped to read:packages and nothing
# else. Not needed at all if you make the packages public.
echo "$GHCR_PAT" | docker login ghcr.io -u <github-user> --password-stdin
```

Add the deploy key to `~/.ssh/authorized_keys` behind a forced command, so a leaked key gets a
deploy and nothing else — no shell, no arbitrary command:

```
command="/home/deploy/Wbskt/deploy/scripts/ssh-forced-command.sh",no-pty,no-agent-forwarding,no-port-forwarding,no-X11-forwarding ssh-ed25519 AAAA... ci-deploy
```

Be clear-eyed about the blast radius: this account is in the `docker` group, which is
root-equivalent on the host. The forced command means a stolen key can only invoke `deploy.sh`
with arguments that survive its validation — but that still includes deploying any tag already
published to the registry.

Repository secrets the workflows need:

| Secret | Value |
|---|---|
| `DEPLOY_HOST` | VM hostname or IP |
| `DEPLOY_USER` | SSH user (in the `docker` group) |
| `DEPLOY_PATH` | Checkout path on the VM, e.g. `/home/deploy/Wbskt` |
| `DEPLOY_SSH_KEY` | Private half of the deploy key |
| `DEPLOY_KNOWN_HOSTS` | `ssh-keyscan <host>` output — pins the host key so the runner can't be MITM'd |

The Dashboard repo needs the same five. Define them as **organisation** secrets shared with both
repos rather than copying them, so rotating the deploy key is one change instead of two.

`GITHUB_TOKEN` covers the GHCR push; no extra secret for that. The workflows target the
`production` (backend) and `production-console` GitHub Environments, so adding required reviewers
there gates deploys behind an approval — worth doing for `production-console` in particular, since
that one otherwise deploys automatically on every push.

## Fresh install (brand-new VM)

Prerequisites you handle outside this repo: provision the VM, point DNS (`auth.<domain>`,
`api.<domain>`, `ws.<domain>`, `console.<domain>`, or one wildcard `*.<domain>`) at its IP, and
open inbound 22/80/443 in the cloud firewall. If the VM has under ~4GB RAM, add swap first
(`fallocate` a few GB, `mkswap`, `swapon`, persist in `/etc/fstab`) — SQL Server + RabbitMQ +
Redis + 4 hosts + Traefik is not a light stack.

```bash
# Docker
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER   # log out/in, or keep using sudo

# Checkout + registry login + .env - see One-time setup above. Fill in DOMAIN, ACME_EMAIL, and
# generate real secrets for SQL_SA_PASSWORD, RABBITMQ_PASSWORD, JWT_KEY, ENGINE_INBOUND_API_KEY,
# CONSOLE_ORIGIN. Consider setting ACME_CASERVER to the Let's Encrypt staging directory first
# (see .env.example) to avoid burning production rate limits while you're still iterating.
# Set IMAGE_TAG to the sha-<short> you want; deploy.sh rewrites it from then on.
cd ~/Wbskt/deploy/compose

docker compose up -d sql                                    # wait for it to report healthy
docker compose --profile migrate pull migrator
MIGRATE_FRESH=true docker compose --profile migrate run --rm migrator   # first run only - creates the DBs

docker compose pull auth management socket engine
docker compose up -d rabbitmq redis
docker compose up -d --no-build auth management socket engine traefik
docker compose ps                                           # everything should report healthy
```

After this, every subsequent release goes through the Deploy workflow — nothing above needs
repeating. `--no-build` matters: the `build:` keys are still present for local development, and
without it a missing image would kick off a full SDK build on the VM.

Once you're happy with staging certs, switch to production: remove `ACME_CASERVER` from `.env`,
then `docker compose stop traefik && docker run --rm -v wbskt_traefik-acme:/letsencrypt busybox rm -f /letsencrypt/acme.json && docker compose up -d traefik`
(the old ACME account/cert state is staging-only, so it needs clearing before Traefik will
register fresh against production).

Then bring up the console, which is already published by the Dashboard repo:

```bash
docker compose pull console && docker compose up -d --no-build console
```

## Incremental deployment (already running, you've changed something)

Normally: push to `master`, wait for **Build images**, then dispatch **Deploy** with that run's
`sha-<short>` tag. Tick `run_migrations` if a DACPAC or SQL script changed — it's the incremental
path, which is always safe to rerun.

The console needs nothing here — pushing to the Dashboard repo builds and deploys it on its own.

To do the same by hand on the VM (equivalent, and what the workflows end up executing):

```bash
deploy/scripts/deploy.sh --tag sha-abc1234 --services "auth management" --migrate
deploy/scripts/deploy.sh --console-tag sha-def5678
```

`--console-tag` is a separate mode: it moves only `CONSOLE_IMAGE_TAG` and never touches the
checkout, because the tag names a Dashboard commit that means nothing to this repo. It is also the
precise way to roll the console back to a known image.

The script refuses to run if the checkout has uncommitted tracked changes, since it does a
`git reset --hard` to line the compose file up with the images. It deliberately exposes no way to
set `MIGRATE_FRESH` or `MIGRATE_ALLOW_DATA_LOSS` — both destroy data with no confirmation, so they
stay a deliberate manual act on the box.

Under the hood it uses `--no-deps`, which stops Compose from also restarting things `auth` depends
on that haven't changed. Redeploying a scaled tier recreates *all* its replicas at once — plain
Compose has no rolling-update primitive like Swarm/K8s, so there's brief downtime for that tier
unless you script "bring up one new, remove one old, repeat" manually. Traefik's Docker provider
watches for label changes on its own, so it only needs a restart if you changed Traefik's *own*
static config (entrypoints, ACME settings, etc.), not for routine host redeploys.

## Scaling

Replica counts are declared in `.env` (`SOCKET_REPLICAS`, `MANAGEMENT_REPLICAS`,
`ENGINE_REPLICAS`), not passed as `--scale` on the command line:

```bash
SOCKET_REPLICAS=3    # Phase 1
MANAGEMENT_REPLICAS=2 # Phase 3
ENGINE_REPLICAS=1    # Phase 4 - needs the remediation prerequisite before going above 1
```

This is load-bearing, not a style preference. `docker compose up -d socket` converges to the
count declared in the compose file, so a tier scaled with a one-off `--scale` flag silently drops
back to a single replica the next time anything redeploys it. Declaring it in `.env` means the
deploy script preserves the scale without needing to know about it.

Engine/RabbitMQ/Redis/SQL are backend-network-only and are never published by Traefik.

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

If an incremental run fails with a "possible data loss" error, the DACPAC contains a change that
would destroy existing rows (a dropped or narrowed column). That block is deliberate. Review what
sqlpackage reported, and if the loss is intended, rerun with:

```
MIGRATE_ALLOW_DATA_LOSS=true docker compose --profile migrate run --rm migrator
```

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
