#!/usr/bin/env bash
# Raises an alert in the stack's own Alertmanager, so it is delivered wherever every other alert is
# (ALERT_EMAIL_TO / ALERT_WEBHOOK_URL). Built for backup.sh's BACKUP_ALERT_CMD:
#
#   BACKUP_ALERT_CMD=/home/<user>/Wbskt/deploy/scripts/alert.sh
#
# Usage: alert.sh "<message>"   (one argument, as BACKUP_ALERT_CMD passes it)
#
# Talks to Alertmanager on the VM's loopback, where compose publishes it. The alert resolves itself
# after ALERT_TTL (default 24h) unless raised again; the BackupMissing rule keeps firing on its own
# until a backup actually succeeds, so nothing is lost when this one expires.
set -euo pipefail

message="${1:?usage: alert.sh <message>}"
url="${ALERTMANAGER_URL:-http://127.0.0.1:9093}"
ttl_hours="${ALERT_TTL_HOURS:-24}"
ends_at="$(date -u -d "+${ttl_hours} hours" +%Y-%m-%dT%H:%M:%SZ)"

# JSON-escape the message: backslashes, quotes, then newlines.
escaped="$(printf '%s' "$message" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | awk 'BEGIN{ORS="\\n"} {print}' | sed 's/\\n$//')"

curl -fsS -X POST "$url/api/v2/alerts" -H 'Content-Type: application/json' --data-binary @- <<JSON
[{
  "labels": {"alertname": "BackupFailed", "severity": "page", "host": "$(hostname)"},
  "annotations": {
    "summary": "$escaped",
    "impact": "Last night's database backup did not finish, so the newest good backup is older than it should be. If the database were lost now, the data since then would be lost with it.",
    "action": "Read the backup log on the VM for the error, fix it, then run backup.sh by hand to take a backup straight away. BackupMissing keeps firing until one succeeds.",
    "command": "tail -50 /var/log/wbskt-backup.log"
  },
  "endsAt": "$ends_at"
}]
JSON
