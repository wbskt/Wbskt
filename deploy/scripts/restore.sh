#!/usr/bin/env bash
# Restores a backup produced by backup.sh. Runs on the VM from the repo checkout.
#
#   deploy/scripts/restore.sh --file /var/backups/wbskt/Wbskt.Database_20260825T020000Z.bak
#   deploy/scripts/restore.sh --file <path> --target Wbskt.Database --force
#
# Defaults to restoring into a scratch database named <original>_restorecheck_<stamp>, because the
# common reason to run this is the rehearsal - proving the backups are restorable - not a real
# recovery. It prints row counts from the restored copy so there is something concrete to compare
# against the live database.
#
# Restoring over an existing database requires --force. Without it this script will not overwrite
# anything, which is what makes it safe to run on a whim against production.
set -euo pipefail

SCRIPT_PATH="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/$(basename "${BASH_SOURCE[0]}")"
readonly SCRIPT_PATH
REPO_ROOT="$(cd "$(dirname "$SCRIPT_PATH")/../.." && pwd)"
readonly REPO_ROOT
readonly COMPOSE_DIR="$REPO_ROOT/deploy/compose"
readonly ENV_FILE="$COMPOSE_DIR/.env"
readonly CONTAINER_DIR=/var/opt/mssql/backups

BACKUP_FILE=""
TARGET_DB=""
FORCE=false

die() {
    echo "restore: $*" >&2
    exit 1
}

log() {
    echo "restore: $*"
}

usage() {
    cat <<'EOF'
usage: restore.sh --file <path-to-.bak> [--target <database>] [--force]

  --file    Backup file on this host, as written by backup.sh.
  --target  Database to restore into. Defaults to a scratch name, for a rehearsal that
            cannot touch anything live.
  --force   Permit restoring over a database that already exists. Required for a real
            recovery; never needed for a rehearsal.
EOF
}

parse_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --file)   BACKUP_FILE="${2:-}"; [[ -n "$BACKUP_FILE" ]] || die "--file needs a value"; shift 2 ;;
            --target) TARGET_DB="${2:-}"; [[ -n "$TARGET_DB" ]] || die "--target needs a value"; shift 2 ;;
            --force)  FORCE=true; shift ;;
            -h|--help) usage; exit 0 ;;
            *) usage >&2; die "unknown argument '$1'" ;;
        esac
    done

    [[ -n "$BACKUP_FILE" ]] || { usage >&2; die "--file is required"; }
    [[ -f "$BACKUP_FILE" ]] || die "no such file: $BACKUP_FILE"
}

load_env() {
    [[ -f "$ENV_FILE" ]] || die "no .env at $ENV_FILE"

    # shellcheck disable=SC1090
    set -a && source "$ENV_FILE" && set +a

    : "${SQL_SA_PASSWORD:?SQL_SA_PASSWORD is required (deploy/compose/.env)}"
}

sqlcmd() {
    docker compose exec -T sql /opt/mssql-tools18/bin/sqlcmd \
        -S localhost -U sa -P "$SQL_SA_PASSWORD" -C -b -Q "$1"
}

# Headerless, pipe-separated, trimmed - so the output can be parsed rather than read.
sqlcmd_rows() {
    docker compose exec -T sql /opt/mssql-tools18/bin/sqlcmd \
        -S localhost -U sa -P "$SQL_SA_PASSWORD" -C -b -h -1 -W -s '|' -Q "$1"
}

main() {
    parse_args "$@"
    load_env
    cd "$COMPOSE_DIR"

    local base stamp staged
    base="$(basename "$BACKUP_FILE")"
    stamp="$(date -u +%Y%m%d%H%M%S)"

    if [[ -z "$TARGET_DB" ]]; then
        # Strip the _<stamp>.bak that backup.sh appends to get back to the original database name.
        TARGET_DB="$(sed -E 's/_[0-9]{8}T[0-9]{6}Z\.bak$//' <<< "$base")_restorecheck_${stamp}"
        log "no --target given; restoring into scratch database $TARGET_DB"
    fi

    staged="$CONTAINER_DIR/restore_${stamp}_${base}"

    docker compose exec -T sql mkdir -p "$CONTAINER_DIR" || die "could not create $CONTAINER_DIR"
    docker compose cp "$BACKUP_FILE" "sql:${staged}" || die "could not copy the backup into the container"

    # Verify before touching any database, so a corrupt file fails here rather than half way
    # through overwriting something.
    sqlcmd "RESTORE VERIFYONLY FROM DISK = N'${staged}' WITH CHECKSUM;" \
        || die "the backup did not verify; refusing to restore from it"

    if [[ "$FORCE" != true ]]; then
        local exists
        exists="$(sqlcmd_rows "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'${TARGET_DB}';" | tr -d '[:space:]')"
        [[ "$exists" == "0" ]] || die "database $TARGET_DB already exists; pass --force to overwrite it"
    fi

    # A backup restored under a different name still carries the original's physical file paths, so
    # every logical file has to be redirected or the restore collides with the live database's files.
    local move_clauses="" data_index=0 log_index=0
    while IFS='|' read -r logical _physical type _rest; do
        logical="$(sed -E 's/[[:space:]]+$//' <<< "$logical")"
        type="$(tr -d '[:space:]' <<< "$type")"

        # Only rows whose Type is a real file class. sqlcmd interleaves blank lines and can emit a
        # trailing "(n rows affected)" that would otherwise parse as a logical file and produce a
        # MOVE clause for a file that does not exist.
        case "$type" in
            D|L|F|S) ;;
            *) continue ;;
        esac

        [[ -n "$logical" ]] || continue

        if [[ "$type" == "L" ]]; then
            move_clauses+=", MOVE N'${logical}' TO N'/var/opt/mssql/data/${TARGET_DB}_log${log_index}.ldf'"
            log_index=$((log_index + 1))
        else
            move_clauses+=", MOVE N'${logical}' TO N'/var/opt/mssql/data/${TARGET_DB}_data${data_index}.mdf'"
            data_index=$((data_index + 1))
        fi
    done < <(sqlcmd_rows "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'${staged}';")

    [[ -n "$move_clauses" ]] || die "could not read the file list from the backup"

    log "restoring into $TARGET_DB"
    sqlcmd "RESTORE DATABASE [${TARGET_DB}] FROM DISK = N'${staged}' WITH REPLACE, RECOVERY${move_clauses}, STATS = 25;" \
        || die "RESTORE DATABASE failed"

    docker compose exec -T sql rm -f "$staged" || log "warning: could not remove $staged"

    log "row counts in $TARGET_DB - compare these against the live database:"
    sqlcmd "USE [${TARGET_DB}];
            SELECT t.name AS TableName, SUM(p.rows) AS [Rows]
            FROM sys.tables t
            JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
            GROUP BY t.name
            ORDER BY t.name;"

    cat <<EOF

restore: done. $TARGET_DB is a full copy of that backup.

If this was a rehearsal, drop it when you are finished comparing:
  docker compose exec -T sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '<password>' -C \\
    -Q "ALTER DATABASE [${TARGET_DB}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${TARGET_DB}];"
EOF
}

main "$@"
