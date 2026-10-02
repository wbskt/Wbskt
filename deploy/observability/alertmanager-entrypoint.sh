#!/bin/sh
# Builds alertmanager.yml from the environment, then runs Alertmanager. Alertmanager does not expand
# environment variables in its config, and the SMTP relay settings already live in .env for the auth
# host, so they are reused here rather than duplicated into a checked-in file.
#
#   ALERT_EMAIL_TO      where alerts are mailed; needs SMTP_HOST and SMTP_FROM_ADDRESS too
#   ALERT_WEBHOOK_URL   a Slack/Teams/ntfy-compatible webhook, as well as or instead of email
#
# With neither set, alerts still fire and are visible in Grafana and the Alertmanager UI; they just
# reach nobody, which the startup log says plainly.
set -eu

config=/tmp/alertmanager.yml
receivers=""

{
    echo "route:"
    echo "  receiver: default"
    echo "  group_by: [alertname]"
    echo "  group_wait: 30s"
    echo "  group_interval: 5m"
    echo "  repeat_interval: 4h"
    echo "receivers:"
    echo "  - name: default"

    if [ -n "${ALERT_EMAIL_TO:-}" ]; then
        receivers="email"
        echo "    email_configs:"
        echo "      - to: '${ALERT_EMAIL_TO}'"
        echo "        from: '${SMTP_FROM_ADDRESS:?ALERT_EMAIL_TO needs SMTP_FROM_ADDRESS}'"
        echo "        smarthost: '${SMTP_HOST:?ALERT_EMAIL_TO needs SMTP_HOST}:${SMTP_PORT:-587}'"
        echo "        require_tls: ${SMTP_USE_STARTTLS:-true}"
        if [ -n "${SMTP_USERNAME:-}" ]; then
            echo "        auth_username: '${SMTP_USERNAME}'"
            echo "        auth_password: '${SMTP_PASSWORD:-}'"
        fi
        echo "        send_resolved: true"
    fi

    if [ -n "${ALERT_WEBHOOK_URL:-}" ]; then
        receivers="${receivers:+$receivers, }webhook"
        echo "    webhook_configs:"
        echo "      - url: '${ALERT_WEBHOOK_URL}'"
        echo "        send_resolved: true"
    fi
} > "$config"

if [ -z "$receivers" ]; then
    echo "alertmanager: neither ALERT_EMAIL_TO nor ALERT_WEBHOOK_URL is set - alerts will not be delivered anywhere" >&2
else
    echo "alertmanager: delivering alerts by $receivers"
fi

exec /bin/alertmanager --config.file="$config" --storage.path=/alertmanager "$@"
