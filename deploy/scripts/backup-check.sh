#!/usr/bin/env bash
# Read-only health check of the backup setup. Runs on the VM from the repo checkout:
#
#   deploy/scripts/backup-check.sh
#
# Run it as the user whose crontab holds the backup job (the deploy user), not with sudo, or the
# crontab check looks at the wrong user.
#
# backup.sh depends on things the repo cannot show: settings in the untracked .env, a line in the
# deploy user's crontab, and files on this disk. This script looks at each of them and prints one
# PASS / WARN / FAIL line per check. It changes nothing, and never prints a secret: settings are
# reported as set or unset, commands by their path.
#
# Exit status is 1 if any check FAILs, so it can run from cron or by hand after changing .env.
# It does not replace the restore rehearsal: run restore.sh into a scratch database for that.
set -uo pipefail

SCRIPT_PATH="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/$(basename "${BASH_SOURCE[0]}")"
readonly SCRIPT_PATH
REPO_ROOT="$(cd "$(dirname "$SCRIPT_PATH")/../.." && pwd)"
readonly REPO_ROOT
readonly COMPOSE_DIR="$REPO_ROOT/deploy/compose"
readonly ENV_FILE="$COMPOSE_DIR/.env"
readonly LOG_FILE="${BACKUP_LOG:-/var/log/wbskt-backup.log}"
readonly DATABASES=(Wbskt.Database Wbskt.Database.Auth)
# A nightly job is late once a day and a bit has passed, the same window BackupMissing uses.
readonly MAX_AGE_HOURS=26

failures=0
pass() { printf 'PASS  %s\n' "$*"; }
warn() { printf 'WARN  %s\n' "$*"; }
fail() { printf 'FAIL  %s\n' "$*"; failures=$((failures + 1)); }

hours_since() { echo $(( ($(date +%s) - $1) / 3600 )); }

# A command setting must name one executable (backup.sh runs it without a shell).
check_command() {
    local name="$1" value="$2" required="$3"
    if [[ -z "$value" ]]; then
        if [[ "$required" == true ]]; then fail "$name is not set"; else warn "$name is not set"; fi
    elif [[ ! -x "$value" ]]; then
        fail "$name is set to $value, which is not an executable file"
    else
        pass "$name is set ($value)"
    fi
}

check_env() {
    if [[ ! -f "$ENV_FILE" ]]; then
        fail "no .env at $ENV_FILE"
        return
    fi
    # shellcheck disable=SC1090
    set -a && source "$ENV_FILE" && set +a

    check_command BACKUP_UPLOAD_CMD "${BACKUP_UPLOAD_CMD:-}" true
    check_command BACKUP_ALERT_CMD "${BACKUP_ALERT_CMD:-}" true
    if [[ "${BACKUP_ALLOW_LOCAL_ONLY:-false}" == "true" ]]; then
        warn "BACKUP_ALLOW_LOCAL_ONLY=true: backups are allowed to stay on this VM"
    fi
    if [[ -z "${ALERT_EMAIL_TO:-}" && -z "${ALERT_WEBHOOK_URL:-}" ]]; then
        fail "neither ALERT_EMAIL_TO nor ALERT_WEBHOOK_URL is set, so backup alerts reach nobody"
    else
        pass "alerts have somewhere to go"
    fi
}

check_cron() {
    if crontab -l 2>/dev/null | grep -v '^[[:space:]]*#' | grep -q 'deploy/scripts/backup.sh'; then
        pass "crontab runs backup.sh: $(crontab -l | grep -v '^[[:space:]]*#' | grep 'deploy/scripts/backup.sh' | awk '{print $1, $2, $3, $4, $5}')"
    else
        fail "no active backup.sh line in $(id -un)'s crontab"
    fi
}

check_log() {
    if [[ ! -r "$LOG_FILE" ]]; then
        fail "no readable log at $LOG_FILE (has the cron job ever run?)"
        return
    fi
    # A successful run's last line is "backup: done (<stamp>)". Anything else last means the latest
    # run failed (die() writes its reason last, including the checks it makes before backing up
    # anything) or is still running.
    local last_line
    last_line="$(grep -v '^[[:space:]]*$' "$LOG_FILE" | tail -1)"
    if [[ -z "$last_line" ]]; then
        fail "$LOG_FILE is empty (has the cron job ever run?)"
    elif [[ "$last_line" == "backup: done"* ]]; then
        pass "the latest run in $LOG_FILE succeeded: $last_line"
    else
        fail "the latest run in $LOG_FILE did not finish (or is still running); its last line: $last_line"
    fi
}

check_local_files() {
    local dir="${BACKUP_DIR:-/var/backups/wbskt}" db newest age
    for db in "${DATABASES[@]}"; do
        newest="$(find "$dir" -maxdepth 1 \( -name "${db}_*.bak" -o -name "${db}_*.bak.gz" \) -type f -printf '%T@ %p\n' 2>/dev/null | sort -n | tail -1)"
        if [[ -z "$newest" ]]; then
            fail "no $db backup in $dir"
            continue
        fi
        age="$(hours_since "${newest%%.*}")"
        if [[ ! -s "${newest#* }" ]]; then
            fail "newest $db backup is empty: ${newest#* }"
        elif (( age > MAX_AGE_HOURS )); then
            fail "newest $db backup is ${age}h old: ${newest#* }"
        else
            pass "newest $db backup is ${age}h old ($(du -h "${newest#* }" | cut -f1))"
        fi
    done
}

check_pushgateway() {
    local url="${BACKUP_METRICS_URL:-http://127.0.0.1:9091}" line ts
    line="$(curl -fsS --max-time 5 "$url/metrics" 2>/dev/null | grep '^wbskt_backup_last_success_timestamp_seconds' | head -1)"
    if [[ -z "$line" ]]; then
        warn "the Pushgateway at $url has no backup success timestamp (it resets when its container restarts)"
        return
    fi
    ts="$(awk '{printf "%d", $NF}' <<<"$line")"
    if (( $(hours_since "$ts") > MAX_AGE_HOURS )); then
        fail "the last success the Pushgateway saw is $(hours_since "$ts")h old"
    else
        pass "the Pushgateway saw a success $(hours_since "$ts")h ago"
    fi
}

main() {
    echo "Backup check on $(hostname), $(date -u +%Y-%m-%dT%H:%M:%SZ)"
    check_env
    check_cron
    check_log
    check_local_files
    check_pushgateway
    echo
    echo "Off-box copies and the restore rehearsal are not checked here. Look at the upload"
    echo "target directly, and rehearse with: $REPO_ROOT/deploy/scripts/restore.sh --file <newest .bak.gz>"
    if (( failures > 0 )); then
        echo "$failures check(s) failed."
        exit 1
    fi
    echo "All checks passed."
}

main "$@"
