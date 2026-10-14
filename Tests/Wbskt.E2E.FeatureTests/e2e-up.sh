#!/usr/bin/env bash
# Brings up everything the E2E suite runs against with one command: SQL Server, RabbitMQ, Redis and
# Mailpit, the migrator (both databases, incrementally), and the five hosts built from source as
# Development, from deploy/compose/docker-compose.e2e.yml. Returns once every host answers /healthz.
#
#   Tests/Wbskt.E2E.FeatureTests/e2e-up.sh                  # build and start it all, then wait
#   Tests/Wbskt.E2E.FeatureTests/e2e-up.sh infra            # infrastructure and databases only
#   Tests/Wbskt.E2E.FeatureTests/e2e-up.sh restart socket   # restart one host and wait for it
#   Tests/Wbskt.E2E.FeatureTests/e2e-up.sh logs auth        # any other docker compose command
#   Tests/Wbskt.E2E.FeatureTests/e2e-up.sh down             # stop it all and drop the database
#
# Then: dotnet test Tests/Wbskt.E2E.FeatureTests
#
# "infra" is for running the hosts yourself (start-hosts.sh, Rider): SQL takes the sa password
# Config/connectionstrings.json uses, and everything publishes on the usual localhost ports.
#
# Needs Docker with Compose v2, host networking (Linux, or Docker Desktop with it turned on), and the
# .NET SDK for its development certificate, which must be trusted on this machine as for
# start-hosts.sh. Keys and the exported certificate live in $E2E_HOST_LOGS/compose (default:
# e2e-hosts/compose at the repo root).

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
state="${E2E_HOST_LOGS:-$root/e2e-hosts}/compose"
compose_file="$root/deploy/compose/docker-compose.e2e.yml"
mkdir -p "$state"

hosts=(auth management devices socket engine)

# The plain-HTTP dev port of each host, for the liveness probe. A function rather than an
# associative array, which the bash macOS ships does not have.
port_of() {
  case "$1" in
    auth) echo 5000 ;;
    management) echo 5010 ;;
    devices) echo 5015 ;;
    socket) echo 5020 ;;
    engine) echo 5030 ;;
    *) echo "unknown host: $1 (one of: ${hosts[*]})" >&2; exit 2 ;;
  esac
}

# Management and devices sign device tokens with one key (see the compose file). Kept in a file so
# a host recreated on its own keeps the key the other is using.
key_file="$state/device-token-key"
if [[ ! -s "$key_file" ]]; then
  openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt | base64 | tr -d '\n' > "$key_file"
fi
export E2E_DEVICE_TOKEN_KEY
E2E_DEVICE_TOKEN_KEY="$(cat "$key_file")"
export E2E_DEV_CERT_DIR="$state/https"
export E2E_DEV_CERT_PASSWORD="${E2E_DEV_CERT_PASSWORD:-wbskt-e2e-dev}"
mkdir -p "$E2E_DEV_CERT_DIR"

compose() {
  docker compose -f "$compose_file" --profile hosts "$@"
}

# Exported on every start, so a renewed development certificate is picked up.
export_dev_cert() {
  rm -f "$E2E_DEV_CERT_DIR/aspnetcore-dev.pfx"
  dotnet dev-certs https -ep "$E2E_DEV_CERT_DIR/aspnetcore-dev.pfx" -p "$E2E_DEV_CERT_PASSWORD" > /dev/null
}

wait_for() {
  local s url
  for s in "$@"; do
    url="http://localhost:$(port_of "$s")/healthz"
    for _ in $(seq 1 90); do
      if curl -fs -o /dev/null "$url"; then
        echo "$s is up"
        continue 2
      fi
      sleep 2
    done
    echo "$s did not come up at $url; last log lines:" >&2
    compose logs --tail 50 "$s" >&2
    exit 1
  done
}

case "${1:-up}" in
  up)
    export_dev_cert
    # Returns once the hosts are started; they start only after the migrator has succeeded.
    compose up -d --build
    wait_for "${hosts[@]}"
    ;;
  infra)
    # No profile: the hosts stay down.
    docker compose -f "$compose_file" up -d --build sql rabbitmq redis mailpit migrator
    # `up -d` does not wait for a one-shot service, so wait for its exit code here.
    status="$(docker wait "$(docker compose -f "$compose_file" ps -aq migrator)")"
    if [[ "$status" != "0" ]]; then
      docker compose -f "$compose_file" logs --tail 50 migrator >&2
      exit 1
    fi
    echo "infrastructure is up and both databases are deployed"
    ;;
  restart)
    shift
    [[ $# -gt 0 ]] || { echo "usage: $0 restart <host>..." >&2; exit 2; }
    for s in "$@"; do port_of "$s" > /dev/null; done
    compose restart "$@"
    wait_for "$@"
    ;;
  down)
    compose down --volumes --remove-orphans
    ;;
  *)
    compose "$@"
    ;;
esac
