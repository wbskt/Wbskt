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

# A YAML single-quoted scalar: a quote inside is written twice, and nothing else is special. printf
# rather than echo, because dash's echo would expand a backslash sequence in a password.
q() {
    printf "'%s'" "$(printf '%s' "$1" | sed "s/'/''/g")"
}

# The auth host reads the same setting and accepts True/False in any case; YAML wants a bare boolean.
require_tls="$(printf '%s' "${SMTP_USE_STARTTLS:-true}" | tr '[:upper:]' '[:lower:]')"

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
        case "$require_tls" in
            true|false) ;;
            *) echo "alertmanager: SMTP_USE_STARTTLS must be true or false, not '${SMTP_USE_STARTTLS}'" >&2; exit 1 ;;
        esac
        receivers="email"
        echo "    email_configs:"
        printf '      - to: %s\n' "$(q "$ALERT_EMAIL_TO")"
        printf '        from: %s\n' "$(q "${SMTP_FROM_ADDRESS:?ALERT_EMAIL_TO needs SMTP_FROM_ADDRESS}")"
        printf '        smarthost: %s\n' "$(q "${SMTP_HOST:?ALERT_EMAIL_TO needs SMTP_HOST}:${SMTP_PORT:-587}")"
        echo "        require_tls: $require_tls"
        if [ -n "${SMTP_USERNAME:-}" ]; then
            printf '        auth_username: %s\n' "$(q "$SMTP_USERNAME")"
            printf '        auth_password: %s\n' "$(q "${SMTP_PASSWORD:-}")"
        fi
        echo "        send_resolved: true"
    fi

    if [ -n "${ALERT_WEBHOOK_URL:-}" ]; then
        receivers="${receivers:+$receivers, }webhook"
        echo "    webhook_configs:"
        printf '      - url: %s\n' "$(q "$ALERT_WEBHOOK_URL")"
        echo "        send_resolved: true"
    fi
} > "$config"

if [ -z "$receivers" ]; then
    echo "alertmanager: neither ALERT_EMAIL_TO nor ALERT_WEBHOOK_URL is set - alerts will not be delivered anywhere" >&2
else
    echo "alertmanager: delivering alerts by $receivers"
fi

exec /bin/alertmanager --config.file="$config" --storage.path=/alertmanager "$@"
