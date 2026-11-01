#!/usr/bin/env bash
# Deploys pre-built images onto this VM. Runs from the repo checkout on the VM, either over SSH
# from a deploy workflow or by hand. Two modes, because the backend and the console are separate
# repos with separate release streams:
#
#   deploy/scripts/deploy.sh --tag sha-abc1234                             # backend hosts
#   deploy/scripts/deploy.sh --tag sha-abc1234 --services auth,management --skip-migrations
#   deploy/scripts/deploy.sh --console-tag sha-def5678                     # console only
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
# Everything lives in functions called from main() at the bottom, so bash parses the whole file
# before executing any of it - see sync_and_reexec for why that matters.
set -euo pipefail

# Backing services (sql/rabbitmq/redis/traefik) are deliberately absent - they are not rebuilt per
# release and restarting them is a separate, more disruptive decision. Console is absent too; it
# has its own mode above.
readonly ALLOWED_SERVICES=(auth management devices gateway-ws engine)
readonly DEFAULT_SERVICES=(auth management devices gateway-ws engine)
# Services this stack no longer has, each with the one that replaced it. A deploy that starts the
# replacement stops and removes the old one's containers once the replacement is healthy, and an old
# name on --services means its replacement. socket became gateway-ws (see deploy/README.md).
readonly -A RETIRED_SERVICES=([socket]=gateway-ws)
readonly HEALTH_TIMEOUT_SECONDS=240
# Free space required on the image store after pruning, before anything is pulled. A full disk
# fails layer extraction halfway through a pull; failing here instead says why, and still happens
# before any container has been stopped.
readonly MIN_FREE_GB=3

SCRIPT_PATH="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/$(basename "${BASH_SOURCE[0]}")"
readonly SCRIPT_PATH
REPO_ROOT="$(cd "$(dirname "$SCRIPT_PATH")/../.." && pwd)"
readonly REPO_ROOT
readonly COMPOSE_DIR="$REPO_ROOT/deploy/compose"
readonly ENV_FILE="$COMPOSE_DIR/.env"

TAG=""
CONSOLE_TAG=""
REF=""
SERVICES=()
SKIP_MIGRATIONS=false

usage() {
    cat <<'EOF'
usage: deploy.sh --tag <image-tag> [--services <a,b,c>] [--ref <git-ref>] [--skip-migrations]
       deploy.sh --console-tag <image-tag>

  --tag              Backend image tag, e.g. sha-abc1234. Also selects the commit this
                     checkout is reset to.
  --services         Comma- or space-separated subset of: auth management devices gateway-ws
                     engine. Defaults to all of them. "socket", the old name of gateway-ws,
                     still means gateway-ws. Use commas over SSH: the remote side word-splits
                     the command line, so a quoted space-separated list arrives as separate
                     arguments.
  --ref              Git ref to reset the checkout to. Defaults to the commit embedded in a
                     sha-<commit> tag; required for any other tag shape.
  --skip-migrations  Don't run the migrator. Migrations run by DEFAULT - the incremental
                     publish is idempotent and refuses data loss, so skipping is the choice
                     that needs justifying, not running.

  --console-tag      Console image tag from the Dashboard repo. Deploys only the console and
                     leaves this checkout alone. Not combinable with the options above.
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
        if [[ -n "${RETIRED_SERVICES[$svc]:-}" ]]; then
            echo "==> '$svc' is now '${RETIRED_SERVICES[$svc]}'; deploying that"
            svc="${RETIRED_SERVICES[$svc]}"
        fi
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
    if [[ -n "$TAG" || -n "$REF" || ${#SERVICES[@]} -gt 0 || "$SKIP_MIGRATIONS" == true ]]; then
        die "--console-tag is its own mode; it cannot be combined with --tag/--ref/--services/--skip-migrations"
    fi
    validate_tag "$CONSOLE_TAG"
    require_env_file
    SERVICES=(console)
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

# Persisted rather than just exported, so a later bare `docker compose ps/up` on the VM refers to
# the same images this deploy started.
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
    require_complete_env
    set_env_var CONSOLE_IMAGE_TAG "$CONSOLE_TAG"

    free_disk_space

    echo "==> pulling console at $CONSOLE_TAG"
    docker compose pull console

    echo "==> starting console"
    docker compose up -d --no-build --no-deps console

    wait_for_health
    echo "==> deployed console $CONSOLE_TAG"
}

deploy_backend() {
    cd "$COMPOSE_DIR"
    require_complete_env
    set_env_var IMAGE_TAG "$TAG"

    # Before anything is stopped or pulled: refuse deploys Compose cannot complete.
    assert_no_network_recreation
    free_disk_space

    echo "==> pulling ${SERVICES[*]} at $TAG"
    docker compose pull "${SERVICES[@]}"

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
        # The dependency wait --no-deps gives up: sql must be up and healthy, as after a VM reboot.
        wait_for_health sql
        docker compose --profile migrate pull migrator
        MIGRATE_FRESH=false MIGRATE_ALLOW_DATA_LOSS=false \
            docker compose --profile migrate run --rm --no-deps migrator
    fi

    # devices and management both serve device login and registration: Traefik sends those paths to
    # devices and falls back to management while no devices instance is healthy. Restarting them
    # together would leave devices with neither, so devices goes first and must be healthy before
    # the rest (management among them) are touched.
    local -a rest=()
    local svc
    if [[ " ${SERVICES[*]} " == *" devices "* && " ${SERVICES[*]} " == *" management "* ]]; then
        echo "==> starting devices before management"
        start_services devices
        for svc in "${SERVICES[@]}"; do
            [[ "$svc" == devices ]] || rest+=("$svc")
        done
        start_services "${rest[@]}"
    else
        start_services "${SERVICES[@]}"
    fi

    retire_replaced_services

    echo "==> deployed $TAG (${SERVICES[*]}) at $(git -C "$REPO_ROOT" rev-parse --short HEAD)"

    report_drift || echo "warning: the drift check itself failed; the deploy above is unaffected"
}

# This script only ever recreates the five hosts, but the checkout it just reset carries every
# other service's definition and config too. Anything that changed for them - a new exporter, a
# scrape job, an alert rule - does nothing until that container is recreated, so say so loudly.
# Reports only; never fails the deploy. The ::warning:: lines surface as annotations on the
# Deploy workflow run, since its SSH step relays this output.
report_drift() {
    local -a stale=() missing=() lagging=() files=()
    local svc want cid have src dest started

    while read -r svc want; do
        local ids
        ids="$(docker compose ps -q "$svc" 2>/dev/null)"
        if [[ -z "$ids" ]]; then
            if is_allowed_service "$svc"; then lagging+=("$svc"); else missing+=("$svc"); fi
            continue
        fi
        for cid in $ids; do
            have="$(docker inspect -f '{{index .Config.Labels "com.docker.compose.config-hash"}}' "$cid")"
            if [[ "$have" != "$want" ]]; then
                if is_allowed_service "$svc"; then lagging+=("$svc"); else stale+=("$svc"); fi
                break
            fi
        done

        # A file bind-mounted on its own keeps the inode it was started with. git reset replaces
        # a changed file rather than rewriting it, so the container goes on reading the old content
        # until it is recreated. The file's mtime is then newer than the container's start. (Reading
        # the file back is no use: docker cp resolves a bind mount to the host path.)
        cid="${ids%%$'\n'*}"
        started="$(date -d "$(docker inspect -f '{{.State.StartedAt}}' "$cid")" +%s)"
        while read -r src dest; do
            [[ -f "$src" ]] || continue
            if (( $(stat -c %Y "$src") > started )); then files+=("$svc:$dest"); fi
        done < <(docker inspect -f '{{range .Mounts}}{{if eq .Type "bind"}}{{.Source}} {{.Destination}}{{"\n"}}{{end}}{{end}}' "$cid")
    done < <(docker compose config --hash '*')

    if (( ${#stale[@]} + ${#missing[@]} + ${#lagging[@]} + ${#files[@]} == 0 )); then
        echo "==> every running service matches the compose file"
        return 0
    fi

    local -a apply=()
    mapfile -t apply < <(printf '%s\n' "${stale[@]}" "${missing[@]}" "${files[@]%%:*}" | grep -v '^$' | sort -u)
    for svc in "${stale[@]}"; do
        echo "::warning::$svc is running an older definition than docker-compose.yml"
    done
    for svc in "${missing[@]}"; do
        echo "::warning::$svc is in docker-compose.yml but has no container"
    done
    for svc in "${files[@]}"; do
        echo "::warning::${svc%%:*} still has the old ${svc#*:}; the file changed on disk after it started"
    done
    for svc in "${lagging[@]}"; do
        echo "::warning::$svc was not part of this deploy and does not match the compose file at $TAG"
    done
    if (( ${#apply[@]} > 0 )); then
        echo "    apply on the VM, in deploy/compose: docker compose up -d --no-build --no-deps ${apply[*]}"
    fi
    if (( ${#lagging[@]} > 0 )); then
        echo "    hosts: deploy.sh --tag $TAG --services $(IFS=,; echo "${lagging[*]}")"
    fi
}

# Stops and removes the containers of a service this stack renamed, once its replacement has just
# been started and reported healthy. Compose no longer knows the old name, so it would leave them
# running as orphans: serving devices from the old image, holding their Traefik router, and renewing
# their presence heartbeat. They are stopped the way compose stops a host (SIGTERM, 10 s grace), so
# each closes its device connections as a server restart and the devices reconnect to the
# replacement through the same public address.
#
# Best-effort: by now the deploy has succeeded, so a failure here warns with the command to finish
# by hand rather than failing the deploy (and skipping the drift report after it).
retire_replaced_services() {
    local old new project filters
    local -a ids
    # The project name (docker-compose.yml's name:), which compose stamps on every container it starts.
    project="$(docker compose config 2>/dev/null | sed -n 's/^name: *//p' | head -n 1)" || project=""
    if [[ -z "$project" ]]; then
        echo "warning: could not read the compose project name; any containers of a renamed service are left running" >&2
        return 0
    fi
    for old in "${!RETIRED_SERVICES[@]}"; do
        new="${RETIRED_SERVICES[$old]}"
        [[ " ${SERVICES[*]} " == *" $new "* ]] || continue
        filters="--filter label=com.docker.compose.project=$project --filter label=com.docker.compose.service=$old"
        # A failed listing reads as none left: process substitution hides the status, and set -e with it.
        # shellcheck disable=SC2086 # the filters are two words each, on purpose
        mapfile -t ids < <(docker ps -aq $filters 2>/dev/null)
        (( ${#ids[@]} > 0 )) || continue
        echo "==> retiring $old (${#ids[@]} container(s)): $new is healthy and replaces it"
        if ! { docker stop --time 10 "${ids[@]}" >/dev/null && docker rm "${ids[@]}" >/dev/null; }; then
            echo "warning: could not retire every old $old container; finish with: docker rm -f \$(docker ps -aq $filters)" >&2
        fi
    done
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

    if [[ -n "$CONSOLE_TAG" ]]; then
        validate_console
        deploy_console
        return
    fi

    validate_backend
    if [[ "${WBSKT_DEPLOY_SYNCED:-}" != "1" ]]; then
        sync_and_reexec "$@"
    fi
    deploy_backend
}

main "$@"
