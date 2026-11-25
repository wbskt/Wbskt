#!/bin/sh
# Renders prometheus.yml for where production runs, then runs Prometheus. Prometheus does not expand
# environment variables in its config.
#
#   PROD_HOST_ADDRESS   unset when production runs on this VM: prometheus.yml is used as it is, and
#                       scrapes rabbitmq, node-exporter and cadvisor by their container names.
#                       Set to the production VM's private address when monitoring runs on a VM of its
#                       own: those three jobs scrape the ports production publishes there
#                       (METRICS_BIND_ADDRESS), and this VM's own node-exporter and cAdvisor are added
#                       as node-monitoring and cadvisor-monitoring. The jobs named node, cadvisor and
#                       rabbitmq keep meaning production, so the alerts on them keep their meaning.
set -eu

config=/tmp/prometheus.yml
address="${PROD_HOST_ADDRESS:-}"

if [ -z "$address" ]; then
    cp /etc/prometheus/prometheus.yml "$config"
else
    # An IPv4 address or a host name: it goes into a sed replacement and a YAML list unquoted.
    case "$address" in
        *[!A-Za-z0-9.-]*)
            echo "PROD_HOST_ADDRESS must be an IPv4 address or a host name, not '$address'" >&2
            exit 1
            ;;
    esac
    sed -e "s/\[rabbitmq:15692\]/[$address:15692]/" \
        -e "s/\[node-exporter:9100\]/[$address:9100]/" \
        -e "s/\[cadvisor:8080\]/[$address:8080]/" \
        /etc/prometheus/prometheus.yml > "$config"
    cat >> "$config" <<YAML

  # This VM's own exporters, under job names of their own (added by prometheus-entrypoint.sh).
  - job_name: node-monitoring
    static_configs:
      - targets: [node-exporter:9100]

  - job_name: cadvisor-monitoring
    static_configs:
      - targets: [cadvisor:8080]
YAML
    echo "scraping production at $address"
fi

exec /bin/prometheus --config.file="$config" "$@"
