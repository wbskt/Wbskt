#!/usr/bin/env bash
# Nightly full backup of both databases. Runs on the VM from the repo checkout, normally from cron:
#
#   0 2 * * *  /home/<user>/Wbskt/deploy/scripts/backup.sh >> /var/log/wbskt-backup.log 2>&1
#
# Three stages, because a backup that never leaves the machine is not a backup:
#
#   1. BACKUP DATABASE inside the sql container, into a staging directory on the sql-data volume.
#   2. RESTORE VERIFYONLY, then copy the file out to BACKUP_DIR on the host and delete the staged
#      copy - so the volume never accumulates backups it would lose along with the data.
#   3. BACKUP_UPLOAD_CMD pushes BACKUP_DIR off the box.
#
# Stage 3 is mandatory. Without BACKUP_UPLOAD_CMD set, this script refuses to run rather than
# quietly producing same-disk copies that would be destroyed by the failure they exist for. Set
# BACKUP_ALLOW_LOCAL_ONLY=true to override that, deliberately, while setting the upload up.
#
# Configuration comes from deploy/compose/.env (see .env.example). Nothing is hardcoded to a cloud
# provider: BACKUP_UPLOAD_CMD is any command you like - rclone, aws s3 sync, restic, scp - invoked
# with BACKUP_DIR as $1.
set -euo pipefail

readonly DATABASES=(Wbskt.Database Wbskt.Database.Auth)

# Inside the sql container. A subdirectory of /var/opt/mssql specifically: that path is already
# owned by the non-root `mssql` user the image runs as, whereas a fresh named volume mounted
# anywhere else arrives root-owned and SQL Server cannot write to it.
readonly STAGING_DIR=/var/opt/mssql/backups

SCRIPT_PATH="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/$(basename "${BASH_SOURCE[0]}")"
readonly SCRIPT_PATH
REPO_ROOT="$(cd "$(dirname "$SCRIPT_PATH")/../.." && pwd)"
readonly REPO_ROOT
readonly COMPOSE_DIR="$REPO_ROOT/deploy/compose"
readonly ENV_FILE="$COMPOSE_DIR/.env"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
readonly STAMP

die() {
    echo "backup: $*" >&2
    alert "backup FAILED on $(hostname): $*"
    exit 1
}

log() {
    echo "backup: $*"
}

# Best-effort notification. Deliberately never fails the script: a broken alert hook must not be
# able to mask - or manufacture - a backup failure.
alert() {
    if [[ -n "${BACKUP_ALERT_CMD:-}" ]]; then
        "${BACKUP_ALERT_CMD}" "$*" || echo "backup: alert command failed" >&2
    fi
}

load_env() {
    [[ -f "$ENV_FILE" ]] || die "no .env at $ENV_FILE"

    # shellcheck disable=SC1090
    set -a && source "$ENV_FILE" && set +a

    : "${SQL_SA_PASSWORD:?SQL_SA_PASSWORD is required (deploy/compose/.env)}"

    BACKUP_DIR="${BACKUP_DIR:-/var/backups/wbskt}"
    BACKUP_RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-14}"

    if [[ -z "${BACKUP_UPLOAD_CMD:-}" && "${BACKUP_ALLOW_LOCAL_ONLY:-false}" != "true" ]]; then
        die "BACKUP_UPLOAD_CMD is not set. A backup that stays on this VM does not survive the
     failure it exists for. Set it in .env, or set BACKUP_ALLOW_LOCAL_ONLY=true to accept
     local-only backups on purpose."
    fi

    mkdir -p "$BACKUP_DIR"
}

# `docker compose exec -T` rather than a bare `docker exec` so the container is resolved through
# the same project/env the rest of the deploy scripts use.
sqlcmd() {
    docker compose exec -T sql /opt/mssql-tools18/bin/sqlcmd \
        -S localhost -U sa -P "$SQL_SA_PASSWORD" -C -b -Q "$1"
}

back_up_one() {
    local db="$1"
    local staged="$STAGING_DIR/${db}_${STAMP}.bak"
    local local_copy="$BACKUP_DIR/${db}_${STAMP}.bak"

    log "backing up $db"

    # CHECKSUM makes the verify below meaningful - without it VERIFYONLY only checks the file is
    # structurally readable, not that its pages match what was written. INIT because each run
    # writes its own uniquely named file and should never append to a previous one.
    sqlcmd "BACKUP DATABASE [${db}] TO DISK = N'${staged}'
            WITH INIT, CHECKSUM, COMPRESSION, STATS = 25;" \
        || die "BACKUP DATABASE failed for $db"

    sqlcmd "RESTORE VERIFYONLY FROM DISK = N'${staged}' WITH CHECKSUM;" \
        || die "backup of $db did not verify - treating it as no backup at all"

    docker compose cp "sql:${staged}" "$local_copy" \
        || die "could not copy $db backup out of the container"

    # The staged copy lives on the same volume as the data files, so leaving it there would grow
    # the volume without adding any protection.
    docker compose exec -T sql rm -f "$staged" \
        || log "warning: could not remove staged copy $staged"

    [[ -s "$local_copy" ]] || die "$local_copy is empty"

    log "wrote $local_copy ($(du -h "$local_copy" | cut -f1))"
}

upload() {
    if [[ -z "${BACKUP_UPLOAD_CMD:-}" ]]; then
        log "BACKUP_UPLOAD_CMD unset and local-only explicitly allowed; skipping upload"
        return
    fi

    log "uploading $BACKUP_DIR"
    "${BACKUP_UPLOAD_CMD}" "$BACKUP_DIR" || die "upload command failed"
}

# Local pruning only. Retention at the off-box destination is that system's job - expiring remote
# copies from here would mean this script could destroy the only surviving backups.
prune() {
    log "pruning local backups older than $BACKUP_RETENTION_DAYS days"
    find "$BACKUP_DIR" -maxdepth 1 -name '*.bak' -type f -mtime "+$BACKUP_RETENTION_DAYS" -print -delete
}

main() {
    load_env
    cd "$COMPOSE_DIR"

    docker compose ps --status running --services 2>/dev/null | grep -qx sql \
        || die "the sql service is not running"

    docker compose exec -T sql mkdir -p "$STAGING_DIR" || die "could not create $STAGING_DIR"

    for db in "${DATABASES[@]}"; do
        back_up_one "$db"
    done

    upload
    prune

    log "done ($STAMP)"
}

main "$@"
