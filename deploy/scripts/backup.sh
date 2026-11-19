#!/usr/bin/env bash
# Nightly full backup of both databases. Runs on the VM from the repo checkout, normally from cron:
#
#   0 2 * * *  /home/<user>/Wbskt/deploy/scripts/backup.sh >> /var/log/wbskt-backup.log 2>&1
#
# Three stages, because a backup that never leaves the machine is not a backup:
#
#   1. BACKUP DATABASE inside the sql container, into a staging directory on the sql-data volume.
#   2. RESTORE VERIFYONLY, then gzip the file out to BACKUP_DIR on the host and delete the staged
#      copy - so the volume never accumulates backups it would lose along with the data. A run that
#      fails part way removes its staged copies on exit, and the next run removes any a killed run
#      left behind.
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
# shellcheck source=deploy/scripts/env-file.sh
source "$(dirname "$SCRIPT_PATH")/env-file.sh"

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

    env_load "$ENV_FILE" SQL_SA_PASSWORD BACKUP_DIR BACKUP_RETENTION_DAYS BACKUP_UPLOAD_CMD BACKUP_ALLOW_LOCAL_ONLY BACKUP_ALERT_CMD BACKUP_METRICS_URL

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

# Only the staged copies this script names, one pattern per database: restore.sh stages its files in
# the same directory, and a backup must never delete a restore in progress. Best effort, since it
# also runs from the EXIT trap, where a failure must not replace the real exit status.
clear_staging() {
    local patterns=() db
    for db in "${DATABASES[@]}"; do
        patterns+=("$STAGING_DIR/${db}_*.bak")
    done
    docker compose exec -T sql sh -c "rm -f ${patterns[*]}" \
        || log "warning: could not clear staged copies in $STAGING_DIR"
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
    local compressed="$BACKUP_DIR/${db}_${STAMP}.bak.gz"

    log "backing up $db"

    # CHECKSUM makes the verify below meaningful - without it VERIFYONLY only checks the file is
    # structurally readable, not that its pages match what was written. INIT because each run
    # writes its own uniquely named file and should never append to a previous one. No COMPRESSION:
    # Express refuses it, so the file is gzipped on the host below instead.
    sqlcmd "BACKUP DATABASE [${db}] TO DISK = N'${staged}'
            WITH INIT, CHECKSUM, STATS = 25;" \
        || die "BACKUP DATABASE failed for $db"

    sqlcmd "RESTORE VERIFYONLY FROM DISK = N'${staged}' WITH CHECKSUM;" \
        || die "backup of $db did not verify - treating it as no backup at all"

    # Compressed on its way out, so the host never holds a full-size copy next to the staged one:
    # uncompressed, a large database would need twice its size free on the disk for a moment.
    docker compose exec -T sql cat "$staged" | nice gzip > "$compressed" \
        || die "could not copy $db backup out of the container"

    # The staged copy lives on the same volume as the data files, so leaving it there would grow
    # the volume without adding any protection.
    docker compose exec -T sql rm -f "$staged" \
        || log "warning: could not remove staged copy $staged"

    [[ -s "$compressed" ]] || die "$compressed is empty"
    gzip --test "$compressed" || die "$compressed does not decompress"

    log "wrote $compressed ($(du -h "$compressed" | cut -f1))"
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
    find "$BACKUP_DIR" -maxdepth 1 \( -name '*.bak' -o -name '*.bak.gz' \) -type f -mtime "+$BACKUP_RETENTION_DAYS" -print -delete
}

# Records the moment of the last good backup in the Pushgateway, where Prometheus picks it up. The
# BackupMissing alert fires when this goes stale, which is what catches the failure no alert command
# can report: cron not running the script at all. Best effort, like alert().
report_success() {
    local url="${BACKUP_METRICS_URL:-http://127.0.0.1:9091}"
    printf 'wbskt_backup_last_success_timestamp_seconds %s\n' "$(date +%s)" \
        | curl -fsS --max-time 10 --data-binary @- "$url/metrics/job/wbskt_backup" \
        || echo "backup: could not report success to $url" >&2
}

# Each database's allocated data-file size, for DatabaseNearExpressLimit: Express caps a database's
# data at 10 GB. Allocated, not used, because that is what the cap counts, and deleting rows never
# shrinks the file. Pushed before backing up so a failing backup does not hide a growing database.
# Best effort, like report_success.
report_sizes() {
    local url="${BACKUP_METRICS_URL:-http://127.0.0.1:9091}" names rows
    names="$(printf "N'%s', " "${DATABASES[@]}")"
    rows="$(docker compose exec -T sql /opt/mssql-tools18/bin/sqlcmd \
        -S localhost -U sa -P "$SQL_SA_PASSWORD" -C -b -h -1 -W -s '|' -Q "SET NOCOUNT ON;
            SELECT DB_NAME(database_id), SUM(CAST(size AS BIGINT)) * 8192
            FROM sys.master_files
            WHERE type = 0 AND DB_NAME(database_id) IN (${names%, })
            GROUP BY database_id;")" || { echo "backup: could not read database sizes" >&2; return; }

    awk -F'|' 'NF == 2 { printf "wbskt_sql_data_file_bytes{database=\"%s\"} %s\n", $1, $2 }' <<< "$rows" \
        | curl -fsS --max-time 10 --data-binary @- "$url/metrics/job/wbskt_backup" \
        || echo "backup: could not report database sizes to $url" >&2
}

main() {
    load_env
    cd "$COMPOSE_DIR"

    # grep without -q: an early exit would SIGPIPE docker compose, which pipefail reports as failure.
    docker compose ps --status running --services 2>/dev/null | grep -x sql > /dev/null \
        || die "the sql service is not running"

    # Two runs at once (cron, and one started by hand after an alert) would each clear the other's
    # staged copy below.
    exec 9> "$BACKUP_DIR/.backup.lock"
    flock -n 9 || die "another backup.sh is still running"

    docker compose exec -T sql mkdir -p "$STAGING_DIR" || die "could not create $STAGING_DIR"

    # Each staged copy is the full size of its database, on the volume that holds the data, so one
    # left by a failed or killed run is cleared before this run adds its own.
    clear_staging
    trap clear_staging EXIT
    trap 'exit 130' INT
    trap 'exit 143' TERM

    report_sizes

    for db in "${DATABASES[@]}"; do
        back_up_one "$db"
    done

    upload
    prune
    report_success

    log "done ($STAMP)"
}

main "$@"
