#!/usr/bin/env bash
# Deploys pre-built images onto this VM. Runs from the repo checkout on the VM, either over SSH
# from a deploy workflow or by hand. Two modes, because the backend and the console are separate
# repos with separate release streams:
#
#   deploy/scripts/deploy.sh --tag sha-abc1234                             # backend hosts
#   deploy/scripts/deploy.sh --tag sha-abc1234 --services auth,management --skip-migrations
#   deploy/scripts/deploy.sh --console-tag sha-def5678                     # console only
#   deploy/scripts/deploy.sh --admin-tag sha-0123abc                       # platform admin only
#
# Nothing is built here - images come from CI via GHCR (.github/workflows/build-images.yml here,
# .github/workflows/build-and-deploy.yml in the Dashboard repo).
#
# --tag additionally resets this checkout to the commit the backend images were built from, so
# docker-compose.yml and config/ always match the running code. --console-tag deliberately does
# not: it names a *Dashboard* commit, which means nothing to this repo, and a console release
# never changes the compose file.
#
# One-time VM setup: `docker login ghcr.io` with a PAT carrying only read:packages, and a
# deploy/compose/.env filled in from .env.example.
#
# A deploy runs detached from whoever started it (see detach_and_follow): the caller only follows its
# log, so a dropped ssh session, a job timeout or Ctrl-C stops the watching, never the deploy. Every
# mode, from every repo, takes one lock on the VM (see take_deploy_lock), so a console or admin deploy
# never runs during a backend one: they rewrite the same .env and prune each other's images.
#
# Everything lives in functions called from main() at the bottom, so bash parses the whole file
# before executing any of it - see sync_and_reexec for why that matters.
set -euo pipefail

# Backing services (sql/rabbitmq/redis/traefik) are deliberately absent - they are not rebuilt per
# release and restarting them is a separate, more disruptive decision. Console is absent too; it
# has its own mode above.
readonly ALLOWED_SERVICES=(auth management client-auth gateway-ws gateway-mqtt engine)
readonly DEFAULT_SERVICES=(auth management client-auth gateway-ws gateway-mqtt engine)
# Recreated by a backend deploy whenever their definition or a mounted config file changed (see
# reconcile_services). Every other service that is not a host - sql, rabbitmq, redis, traefik, and
# any service added later until it is listed here - is only reported.
readonly SUPPORTING_SERVICES=(console otel-collector prometheus alertmanager pushgateway node-exporter cadvisor loki tempo grafana)
readonly HEALTH_TIMEOUT_SECONDS=240
# Free space required on the image store after pruning, before anything is pulled. A full disk
# fails layer extraction halfway through a pull; failing here instead says why, and still happens
# before any container has been stopped.
readonly MIN_FREE_GB=3
# One lock for every mode. /run/lock is world-writable and cleared at boot, so a lock left by a deploy
# the VM restarted under cannot hold the next one.
readonly LOCK_FILE=/run/lock/wbskt-deploy.lock
# How long a deploy waits for one already running. A backend deploy with a long migration takes most
# of an hour (MIGRATE_COMMAND_TIMEOUT is 30 minutes a statement), so this outlasts it.
readonly LOCK_WAIT_SECONDS=3600
# Each deploy's log and exit status, the last LOGS_KEPT of them.
readonly STATE_DIR="${XDG_STATE_HOME:-$HOME/.local/state}/wbskt-deploy"
readonly LOGS_KEPT=20
# Tries of each image pull. A registry that answers one token request with EOF failed whole deploys.
readonly PULL_ATTEMPTS=3

SCRIPT_PATH="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/$(basename "${BASH_SOURCE[0]}")"
readonly SCRIPT_PATH
REPO_ROOT="$(cd "$(dirname "$SCRIPT_PATH")/../.." && pwd)"
readonly REPO_ROOT
readonly COMPOSE_DIR="$REPO_ROOT/deploy/compose"
readonly ENV_FILE="$COMPOSE_DIR/.env"
# shellcheck source=deploy/scripts/env-file.sh
source "$(dirname "$SCRIPT_PATH")/env-file.sh"

TAG=""
CONSOLE_TAG=""
ADMIN_TAG=""
REF=""
SERVICES=()
SKIP_MIGRATIONS=false

usage() {
    cat <<'EOF'
usage: deploy.sh --tag <image-tag> [--services <a,b,c>] [--ref <git-ref>] [--skip-migrations]
       deploy.sh --console-tag <image-tag>
       deploy.sh --admin-tag <image-tag>

  --tag              Backend image tag, e.g. sha-abc1234. Also selects the commit this
                     checkout is reset to.
  --services         Comma- or space-separated subset of: auth management client-auth gateway-ws gateway-mqtt
                     engine. Defaults to all of them. Use commas over SSH: the remote side word-splits
                     the command line, so a quoted space-separated list arrives as separate
                     arguments.
  --ref              Git ref to reset the checkout to. Defaults to the commit embedded in a
                     sha-<commit> tag; required for any other tag shape.
  --skip-migrations  Don't run the migrator. Migrations run by DEFAULT - the incremental
                     publish is idempotent and refuses data loss, so skipping is the choice
                     that needs justifying, not running.

  --console-tag      Console image tag from the Dashboard repo. Deploys only the console and
                     leaves this checkout alone. Not combinable with the options above.

  --admin-tag        Platform admin image tag from the Wbskt.Admin repo. Migrates the admin
                     database, then deploys only the admin host, loopback only. Leaves this
                     checkout alone. Not combinable with any other option.
EOF
}

die() {
    echo "deploy: $*" >&2
    exit 1
}

parse_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --tag)         TAG="${2:-}"; [[ -n "$TAG" ]] || die "--tag needs a value"; shift 2 ;;
            --console-tag) CONSOLE_TAG="${2:-}"
                           [[ -n "$CONSOLE_TAG" ]] || die "--console-tag needs a value"; shift 2 ;;
            --admin-tag)   ADMIN_TAG="${2:-}"
                           [[ -n "$ADMIN_TAG" ]] || die "--admin-tag needs a value"; shift 2 ;;
            --ref)         REF="${2:-}"; [[ -n "$REF" ]] || die "--ref needs a value"; shift 2 ;;
            --services)    [[ -n "${2:-}" ]] || die "--services needs a value"
                           read -r -a SERVICES <<< "${2//,/ }"; shift 2 ;;
            --skip-migrations) SKIP_MIGRATIONS=true; shift ;;
            -h|--help)     usage; exit 0 ;;
            *)             usage >&2; die "unknown argument: $1" ;;
        esac
    done
}

is_allowed_service() {
    local candidate="$1" allowed
    for allowed in "${ALLOWED_SERVICES[@]}"; do
        if [[ "$candidate" == "$allowed" ]]; then
            return 0
        fi
    done
    return 1
}

is_supporting_service() {
    local candidate="$1" supporting
    for supporting in "${SUPPORTING_SERVICES[@]}"; do
        if [[ "$candidate" == "$supporting" ]]; then
            return 0
        fi
    done
    return 1
}

# Both this and the calling workflow validate. The script is also run by hand, and it is the thing
# that actually interpolates the value into compose invocations and a sed replacement.
validate_tag() {
    [[ "$1" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]] || die "invalid tag: $1"
}

require_env_file() {
    [[ -f "$ENV_FILE" ]] || die "missing $ENV_FILE - copy .env.example and fill it in"
}

validate_backend() {
    local svc
    [[ -n "$TAG" ]] || { usage >&2; die "--tag is required"; }
    validate_tag "$TAG"

    [[ ${#SERVICES[@]} -gt 0 ]] || SERVICES=("${DEFAULT_SERVICES[@]}")
    local -a named=("${SERVICES[@]}")
    SERVICES=()
    for svc in "${named[@]}"; do
        is_allowed_service "$svc" || die "unknown service '$svc' (allowed: ${ALLOWED_SERVICES[*]})"
        [[ " ${SERVICES[*]} " == *" $svc "* ]] || SERVICES+=("$svc")
    done

    if [[ -z "$REF" && "$TAG" =~ ^sha-([0-9a-f]{7,40})$ ]]; then
        REF="${BASH_REMATCH[1]}"
    fi
    [[ -n "$REF" ]] || die "--ref is required for tag '$TAG' (only sha-<commit> tags imply a ref)"

    require_env_file
}

validate_console() {
    if [[ -n "$ADMIN_TAG" || -n "$TAG" || -n "$REF" || ${#SERVICES[@]} -gt 0 || "$SKIP_MIGRATIONS" == true ]]; then
        die "--console-tag is its own mode; it cannot be combined with --tag/--ref/--services/--skip-migrations"
    fi
    validate_tag "$CONSOLE_TAG"
    require_env_file
    SERVICES=(console)
}

validate_admin() {
    if [[ -n "$TAG" || -n "$CONSOLE_TAG" || -n "$REF" || ${#SERVICES[@]} -gt 0 || "$SKIP_MIGRATIONS" == true ]]; then
        die "--admin-tag is its own mode; it cannot be combined with any other option"
    fi
    validate_tag "$ADMIN_TAG"
    require_env_file
    SERVICES=(admin)
}

# Resets the checkout to the deployed commit, then hands off to the version of this script that
# just landed. Both steps sit in one function on purpose: `git reset --hard` rewrites this very
# file, and bash reads scripts incrementally from a byte offset, so reading the next line from a
# file that changed underneath it can execute garbage. A fully-parsed function body runs from
# memory, and `exec` replaces the process before bash ever reads from the stale offset.
#
# The re-exec is also the correct behaviour on its own terms: a commit that changes deploy.sh
# should be deployed by its own version of the script, not the previous one.
sync_and_reexec() {
    # reset --hard silently discards tracked-file edits, so refuse rather than destroy a hotfix
    # someone applied on the box. (.env is untracked and unaffected either way.)
    if [[ -n "$(git -C "$REPO_ROOT" status --porcelain --untracked-files=no)" ]]; then
        die "working tree has uncommitted changes; commit, stash or discard them before deploying"
    fi

    echo "==> syncing checkout to $REF"
    git -C "$REPO_ROOT" fetch --prune origin
    git -C "$REPO_ROOT" reset --hard "$REF"

    WBSKT_DEPLOY_SYNCED=1 exec bash "$SCRIPT_PATH" "$@"
}

# Starts this deploy again in a session of its own, with its output in a log under STATE_DIR, and
# follows that log until it ends; then exits with the deploy's own status. The deploy is not a child
# of the ssh session (setsid, nohup), so when the session drops - the job timed out, the network went,
# someone pressed Ctrl-C - it carries on and finishes, rather than stopping between a migration and
# the hosts that need it. Its log stays on the VM for whoever looks next.
#
# The deploy's status is its exit code, which nohup and setsid --wait hand back to wait. A deploy of a
# commit older than this script (a rollback) is run by that commit's script, which never writes the
# status file, so the file is only a record in the log directory, not what the caller is told.
detach_and_follow() {
    local stamp log status pid old rc

    mkdir -p "$STATE_DIR"
    stamp="$(date -u +%Y%m%dT%H%M%SZ)-$$"
    log="$STATE_DIR/deploy-$stamp.log"
    status="$STATE_DIR/deploy-$stamp.status"

    # --wait keeps setsid until the deploy ends, so its pid is the one to follow even if it forked.
    WBSKT_DEPLOY_DETACHED=1 WBSKT_DEPLOY_STATUS="$status" \
        nohup setsid --wait bash "$SCRIPT_PATH" "$@" > "$log" 2>&1 < /dev/null &
    pid=$!
    echo "==> deploying in the background (pid $pid); the log is $log on the VM"

    while read -r old; do
        rm -f "$old" "${old%.log}.status"
    done < <(ls -1t "$STATE_DIR"/deploy-*.log 2>/dev/null | tail -n +$((LOGS_KEPT + 1)))

    tail -n +1 -f --pid="$pid" "$log" || true
    rc=0
    wait "$pid" || rc=$?

    echo "==> the deploy exited with $rc; its log is $log on the VM"
    exit "$rc"
}

# Holds LOCK_FILE for the rest of the deploy, on fd 8. The fd survives the exec in sync_and_reexec, and
# WBSKT_DEPLOY_LOCKED tells the new script it already holds the lock: opening the file again would
# close the fd and let the lock go.
take_deploy_lock() {
    if [[ "${WBSKT_DEPLOY_LOCKED:-}" == 1 ]]; then
        return
    fi

    exec 8> "$LOCK_FILE"
    if ! flock -n 8; then
        echo "==> another deploy is running on this VM; waiting for it (up to $((LOCK_WAIT_SECONDS / 60)) minutes)"
        flock -w "$LOCK_WAIT_SECONDS" 8 || die "another deploy held the lock for $((LOCK_WAIT_SECONDS / 60)) minutes; nothing was changed"
    fi
    export WBSKT_DEPLOY_LOCKED=1
}

# Pulls with docker compose, trying again after a pause: one failed token request from the registry
# should not fail a release. Nothing has been stopped or written when this gives up.
pull_images() {
    local attempt
    for attempt in $(seq 1 "$PULL_ATTEMPTS"); do
        if docker compose "$@"; then
            return 0
        fi
        if (( attempt < PULL_ATTEMPTS )); then
            echo "==> pull failed (attempt $attempt of $PULL_ATTEMPTS); trying again in $((attempt * 10))s"
            sleep $((attempt * 10))
        fi
    done
    die "could not pull the images after $PULL_ATTEMPTS attempts; nothing was stopped and .env is unchanged"
}

# Persisted rather than just exported, so a later bare `docker compose ps/up` on the VM refers to
# the same images this deploy started. Written only once the hosts started on them are healthy: until
# then compose reads the tag from the environment, so .env keeps naming the release that last passed
# if the deploy stops before that.
set_env_var() {
    local key="$1" value="$2"
    if grep -q "^${key}=" "$ENV_FILE"; then
        sed -i "s|^${key}=.*|${key}=${value}|" "$ENV_FILE"
    else
        printf '%s=%s\n' "$key" "$value" >> "$ENV_FILE"
    fi
}

# Compose reconciles networks before containers. If a network's definition changed, it removes and
# recreates it - which it CANNOT do while a container outside our --no-deps set (traefik) is still
# attached. Compose discovers that only after it has already stopped the hosts, so the deploy dies
# with the stack down and unroutable. Ask Compose what it intends to do before letting it start.
#
# Only Removing/Removed is fatal. A bare "Creating" is a first-ever bring-up, which is fine.
assert_no_network_recreation() {
    local plan
    plan=$(docker compose --dry-run up -d --no-build --no-deps "${SERVICES[@]}" 2>&1 || true)
    if grep -qE 'Network [^[:space:]]+ +(Removing|Removed)' <<< "$plan"; then
        echo "--- what Compose planned to do ---" >&2
        grep -E 'Network ' <<< "$plan" >&2
        echo "----------------------------------" >&2
        die "the network definitions changed, so Compose would tear the networks down and rebuild
them. That cannot happen while Traefik is attached, and attempting it leaves the stack stopped
and off the network. This needs a maintenance window, not an incremental deploy:
    docker compose down && docker compose up -d --no-build
See the 'Network changes' section of deploy/README.md."
    fi
}

# Every deploy pulls a new sha-<commit> tag of each host plus the migrator, and nothing ever removed
# the old ones, so the image store grew by a full release per deploy until a pull ran the disk out.
# Pruning unused images is safe here: whatever is running keeps its image (prune skips anything a
# container still references), and any older tag can be pulled from GHCR again to roll back.
# Runs before the pull, because that is the step that needs the room.
free_disk_space() {
    local store free_kb
    store=/var/lib/containerd
    [[ -d "$store" ]] || store="$(docker info -f '{{.DockerRootDir}}')"

    echo "==> pruning unused images (free on $store before: $(df -h --output=avail "$store" | tail -1 | tr -d ' '))"
    docker image prune -af >/dev/null
    docker builder prune -af >/dev/null 2>&1 || true

    free_kb="$(df -k --output=avail "$store" | tail -1 | tr -d ' ')"
    echo "==> free on $store after prune: $((free_kb / 1024 / 1024)) GB"
    if (( free_kb < MIN_FREE_GB * 1024 * 1024 )); then
        df -h "$store" >&2
        docker system df >&2 || true
        die "under ${MIN_FREE_GB} GB free on $store even after pruning unused images. Nothing was
stopped. Check what else is using the disk (backups under BACKUP_DIR, volumes, journald) or grow
the disk - see the 'Disk space' section of deploy/README.md."
    fi
}

container_state() {
    docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$1"
}

# Waits for the services named, or for all of $SERVICES when none are.
wait_for_health() {
    local deadline=$((SECONDS + HEALTH_TIMEOUT_SECONDS))
    local pending svc cid state
    local -a cids services
    if [[ $# -gt 0 ]]; then services=("$@"); else services=("${SERVICES[@]}"); fi

    echo "==> waiting for ${services[*]} to report healthy"
    while (( SECONDS < deadline )); do
        pending=""
        for svc in "${services[@]}"; do
            mapfile -t cids < <(docker compose ps -q "$svc")
            if [[ ${#cids[@]} -eq 0 ]]; then
                pending+=" $svc"
                continue
            fi
            for cid in "${cids[@]}"; do
                state="$(container_state "$cid")"
                # A service without a healthcheck can only be judged on "is it still up".
                if [[ "$state" != healthy && "$state" != running ]]; then
                    pending+=" $svc"
                fi
            done
        done
        if [[ -z "$pending" ]]; then
            docker compose ps
            return 0
        fi
        sleep 5
    done

    docker compose ps

    # The reason is almost always in the container's own log - a missing environment variable, a
    # failed connection, a crash loop. Printing it here turns "timed out waiting for: console"
    # into an actionable failure instead of the start of a debugging session.
    # Deduplicated: a scaled tier contributes one entry to $pending per unhealthy replica, and the
    # logs are per service.
    for svc in $(tr ' ' '\n' <<< "$pending" | sort -u); do
        echo >&2
        echo "--- last 20 log lines: $svc ---" >&2
        docker compose logs --tail 20 "$svc" 2>&1 | tail -20 >&2
    done
    echo >&2

    die "timed out after ${HEALTH_TIMEOUT_SECONDS}s waiting for:$pending"
}

# Every compose command interpolates the whole file, so one required variable missing from .env
# (a `${VAR:?…}` in docker-compose.yml) stops pull, up and exec alike. Check before anything is
# pulled or stopped, and say which variable, rather than failing part way through the deploy.
require_complete_env() {
    local err
    if ! err=$(docker compose config --quiet 2>&1); then
        die ".env is incomplete; no image was pulled and no container touched:
$err
Fill it in from .env.example and deploy again."
    fi
}

# The console image is built from the Dashboard repo, so there is no checkout to sync and no
# migration to consider - just swap the tag and recreate the one container.
deploy_console() {
    cd "$COMPOSE_DIR"
    export CONSOLE_IMAGE_TAG="$CONSOLE_TAG"
    require_complete_env

    free_disk_space

    echo "==> pulling console at $CONSOLE_TAG"
    pull_images pull console

    echo "==> starting console"
    docker compose up -d --no-build --no-deps console

    wait_for_health
    set_env_var CONSOLE_IMAGE_TAG "$CONSOLE_TAG"
    echo "==> deployed console $CONSOLE_TAG"
}

# The platform admin's settings are optional in .env (an install may not run it), so compose cannot
# insist on them; this does, before anything is pulled.
require_admin_env() {
    local key missing=""
    for key in SQL_ADMIN_HOST_PASSWORD SQL_ADMIN_READER_PASSWORD ADMIN_STAFF_SECRET_KEY; do
        grep -Eq "^${key}=.+" "$ENV_FILE" || missing+=" $key"
    done
    [[ -z "$missing" ]] || die "set$missing in $ENV_FILE first (see \"Platform admin\" in deploy/README.md)"
}

# The admin image comes from the Wbskt.Admin repo, so like the console there is no checkout to sync.
# Its own database is migrated first, by the migrator image published alongside it; the main
# databases' [admin] views and its reader login come from this repo's migrator on backend deploys.
deploy_admin() {
    cd "$COMPOSE_DIR"
    export COMPOSE_PROFILES=admin
    require_complete_env
    require_admin_env
    export ADMIN_IMAGE_TAG="$ADMIN_TAG"

    free_disk_space

    echo "==> pulling admin and admin-migrator at $ADMIN_TAG"
    pull_images pull admin admin-migrator

    echo "==> migrating the admin database"
    docker compose run --rm --no-deps admin-migrator

    echo "==> starting admin"
    docker compose up -d --no-build --no-deps admin

    wait_for_health admin
    set_env_var ADMIN_IMAGE_TAG "$ADMIN_TAG"
    echo "==> deployed admin $ADMIN_TAG on 127.0.0.1:8090"
}

# SQL_MEMORY_LIMIT_MB cannot go under 2048, which on a 4 GB VM left most of SQL Server's cache in swap:
# every page it believed cached was a disk read. max server memory has no such floor, applies at once
# without a restart and is kept in master, so each deploy sets it from .env and an edit there takes
# effect on the next one.
apply_sql_memory() {
    local mb
    mb=$(env_get "$ENV_FILE" SQL_MAX_SERVER_MEMORY_MB)
    mb=${mb:-1024}
    [[ "$mb" =~ ^[0-9]+$ ]] || die "SQL_MAX_SERVER_MEMORY_MB must be a whole number of MB, not '$mb'"
    echo "==> setting SQL Server max server memory to $mb MB"
    docker compose exec -T sql bash -c "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"\$MSSQL_SA_PASSWORD\" -C -b -Q \"EXEC sp_configure 'show advanced options', 1; RECONFIGURE; EXEC sp_configure 'max server memory (MB)', $mb; RECONFIGURE;\"" >/dev/null
}

deploy_backend() {
    cd "$COMPOSE_DIR"
    export IMAGE_TAG="$TAG"
    require_complete_env

    # Before anything is stopped or pulled: refuse deploys Compose cannot complete.
    assert_no_network_recreation
    free_disk_space

    echo "==> pulling ${SERVICES[*]} at $TAG"
    pull_images pull "${SERVICES[@]}"

    # The dependency wait --no-deps gives up below: sql must be up and healthy, as after a VM reboot.
    wait_for_health sql
    apply_sql_memory

    if [[ "$SKIP_MIGRATIONS" == true ]]; then
        echo "==> SKIPPING migrations (--skip-migrations)"
    else
        # Runs by default. sqlpackage's incremental publish is idempotent - a no-op when the
        # schema already matches - so the cost of running it needlessly is seconds, while the cost
        # of forgetting it is new code against an old schema. Opt-out beats opt-in here.
        # MIGRATE_FRESH and MIGRATE_ALLOW_DATA_LOSS are forced off: both destroy data with no
        # confirmation, and compose would otherwise read them from .env or this shell, where one
        # stray line would drop both databases on the next automatic deploy. See deploy/README.md.
        # --no-deps: without it, `run` recreates sql whenever its compose definition has changed
        # (an edition or memory setting, a new image), restarting the database mid-deploy. Changing
        # sql is a deliberate step of its own (see "SQL Server edition" in deploy/README.md).
        echo "==> running incremental database migration"
        pull_images --profile migrate pull migrator
        MIGRATE_FRESH=false MIGRATE_ALLOW_DATA_LOSS=false \
            docker compose --profile migrate run --rm --no-deps migrator
    fi

    # client-auth and management both serve client login and registration: Traefik sends those paths to
    # client-auth and falls back to management while no client-auth instance is healthy. Restarting them
    # together would leave client login with neither, so client-auth goes first and must be healthy before
    # the rest (management among them) are touched.
    #
    # .env takes the new tag only once every host started here is healthy (compose reads the exported
    # IMAGE_TAG meanwhile): after a failed start, `docker compose up -d <host>` brings back the release
    # that last passed, not the one that did not (#986). A deploy of some hosts only still leaves .env
    # naming a release the others never ran, which it says below.
    local -a rest=()
    local svc
    echo "==> .env keeps IMAGE_TAG=$(grep -E '^IMAGE_TAG=' "$ENV_FILE" | cut -d= -f2- || true) until ${SERVICES[*]} are healthy at $TAG"
    if [[ " ${SERVICES[*]} " == *" client-auth "* && " ${SERVICES[*]} " == *" management "* ]]; then
        echo "==> starting client-auth before management"
        start_services client-auth
        for svc in "${SERVICES[@]}"; do
            [[ "$svc" == client-auth ]] || rest+=("$svc")
        done
        start_services "${rest[@]}"
    else
        start_services "${SERVICES[@]}"
    fi
    set_env_var IMAGE_TAG "$TAG"
    local -a behind=()
    for svc in "${DEFAULT_SERVICES[@]}"; do
        [[ " ${SERVICES[*]} " == *" $svc "* ]] || behind+=("$svc")
    done
    if [[ ${#behind[@]} -gt 0 ]]; then
        echo "::warning::.env now names $TAG, which ${behind[*]} never ran; \`docker compose up -d\` on one of them starts $TAG unchecked"
    fi

    echo "==> deployed $TAG (${SERVICES[*]}) at $(git -C "$REPO_ROOT" rev-parse --short HEAD)"

    reconcile_services
}

# This script deploys the hosts, but the checkout it just reset carries every other service's
# definition and config too, and a change there does nothing until that container is recreated.
# Reporting alone let memory caps, the RabbitMQ memory alarm and the dead man's switch sit
# unapplied behind green deploys. So the supporting services,
# which hold no client connections and come back in seconds, are brought in line here. The held
# services drop every connection when recreated, so they are only reported, on a line the Deploy
# workflow turns into an issue.
reconcile_services() {
    local -a recreate=() force=() held=() lagging=() files=()
    local svc want cid have src dest started ids changed

    while read -r svc want; do
        ids="$(docker compose ps -q "$svc" 2>/dev/null)"
        changed=false
        if [[ -z "$ids" ]]; then
            changed=true
        else
            for cid in $ids; do
                have="$(docker inspect -f '{{index .Config.Labels "com.docker.compose.config-hash"}}' "$cid")"
                if [[ "$have" != "$want" ]]; then
                    changed=true
                    break
                fi
            done
        fi

        # A file bind-mounted on its own keeps the inode it was started with. git reset replaces
        # a changed file rather than rewriting it, so the container goes on reading the old content
        # until it is recreated. The file's mtime is then newer than the container's start. (Reading
        # the file back is no use: docker cp resolves a bind mount to the host path.)
        files=()
        if [[ -n "$ids" ]]; then
            cid="${ids%%$'\n'*}"
            started="$(date -d "$(docker inspect -f '{{.State.StartedAt}}' "$cid")" +%s)"
            while read -r src dest; do
                [[ -f "$src" ]] || continue
                if (( $(stat -c %Y "$src") > started )); then files+=("$dest"); fi
            done < <(docker inspect -f '{{range .Mounts}}{{if eq .Type "bind"}}{{.Source}} {{.Destination}}{{"\n"}}{{end}}{{end}}' "$cid")
        fi

        if [[ "$changed" == false && ${#files[@]} -eq 0 ]]; then
            continue
        fi
        if is_allowed_service "$svc"; then
            lagging+=("$svc")
        elif is_supporting_service "$svc"; then
            # The config hash covers the definition, not the content of a mounted file, so a
            # plain `up` would leave a container whose only change is a file as it is.
            if [[ "$changed" == true ]]; then recreate+=("$svc"); else force+=("$svc"); fi
        else
            held+=("$svc")
            if [[ -z "$ids" ]]; then
                echo "::warning::$svc is in docker-compose.yml but has no container"
            elif [[ "$changed" == true ]]; then
                echo "::warning::$svc is running an older definition than docker-compose.yml"
            fi
            for dest in "${files[@]}"; do
                echo "::warning::$svc still has the old $dest; the file changed on disk after it started"
            done
        fi
    done < <(docker compose config --hash '*')

    if (( ${#recreate[@]} > 0 )); then
        echo "==> applying the compose file to ${recreate[*]}"
        docker compose up -d --no-build --no-deps "${recreate[@]}"
    fi
    if (( ${#force[@]} > 0 )); then
        echo "==> recreating ${force[*]} for a changed config file"
        docker compose up -d --no-build --no-deps --force-recreate "${force[@]}"
    fi
    if (( ${#recreate[@]} + ${#force[@]} > 0 )); then
        wait_for_health "${recreate[@]}" "${force[@]}"
    fi

    for svc in "${lagging[@]}"; do
        echo "::warning::$svc was not part of this deploy and does not match the compose file at $TAG"
    done
    report_admin_drift
    if (( ${#lagging[@]} > 0 )); then
        echo "    hosts: deploy.sh --tag $TAG --services $(IFS=,; echo "${lagging[*]}")"
    fi

    # Read by deploy.yml, which opens an issue while this list is not empty and closes it once it is.
    echo "held-services-behind: ${held[*]}"
    if (( ${#held[@]} > 0 )); then
        echo "    apply in a quiet minute, in deploy/compose: docker compose up -d --no-build --no-deps --force-recreate ${held[*]}"
    else
        echo "==> every running service matches the compose file"
    fi
}

# The platform admin sits in the admin profile, which the hash listing above leaves out, and is
# deployed on its own (deploy.sh --admin-tag), so a backend deploy never recreates it. Where it runs,
# a change to its definition here (an env var, a limit) is reported, as a lagging host is.
report_admin_drift() {
    local cid want have
    cid="$(docker ps -aq --filter 'name=^wbskt-admin$')"
    [[ -n "$cid" ]] || return 0
    want="$(COMPOSE_PROFILES=admin docker compose config --hash admin | awk '{ print $2 }')"
    have="$(docker inspect -f '{{index .Config.Labels "com.docker.compose.config-hash"}}' "$cid")"
    if [[ -n "$want" && "$have" != "$want" ]]; then
        echo "::warning::admin was not part of this deploy and does not match the compose file at $TAG"
        echo "    admin: deploy.sh --admin-tag $(grep -E '^ADMIN_IMAGE_TAG=' "$ENV_FILE" | cut -d= -f2- || true)"
    fi
}

start_services() {
    echo "==> starting $*"
    # --no-build: the compose file keeps its build: keys for local development, and without this a
    # missing image would silently trigger a full SDK build on the production box.
    # --no-deps: sql/rabbitmq/redis/traefik are already running and must not be recycled per deploy.
    docker compose up -d --no-build --no-deps "$@"

    wait_for_health "$@"
}

main() {
    parse_args "$@"

    # Validated before detaching, so a mistyped argument is answered to the caller directly.
    if [[ -n "$CONSOLE_TAG" ]]; then
        validate_console
    elif [[ -n "$ADMIN_TAG" ]]; then
        validate_admin
    else
        validate_backend
    fi

    if [[ "${WBSKT_DEPLOY_DETACHED:-}" != 1 ]]; then
        detach_and_follow "$@"
    fi

    # Kept beside the log as a record of how it ended (detach_and_follow exits with the deploy's own
    # code). An exec (sync_and_reexec) runs no trap, and the new script sets its own.
    trap 'echo "$?" > "$WBSKT_DEPLOY_STATUS"' EXIT
    take_deploy_lock

    if [[ -n "$CONSOLE_TAG" ]]; then
        deploy_console
        return
    fi

    if [[ -n "$ADMIN_TAG" ]]; then
        deploy_admin
        return
    fi

    if [[ "${WBSKT_DEPLOY_SYNCED:-}" != "1" ]]; then
        sync_and_reexec "$@"
    fi
    deploy_backend
}

main "$@"
