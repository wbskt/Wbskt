#!/usr/bin/env bash
# Starts the hosts the E2E suite runs against, in the background, as Development (so they pick
# up appsettings.Development.json: the dev ports, the raised auth rate limit, sign-in without a
# confirmed address). Expects SQL Server with both databases deployed and RabbitMQ on localhost,
# as Deploy-Databases.ps1 and the Docker commands in README.md set up.
#
#   Tests/Wbskt.E2E.FeatureTests/start-hosts.sh            # build, start them all, wait for /healthz
#   Tests/Wbskt.E2E.FeatureTests/start-hosts.sh auth       # restart only the auth host (no build)
#
# Extra host configuration passes through the environment, e.g.
#   RateLimiting__Authentication__PermitLimit=10 Tests/Wbskt.E2E.FeatureTests/start-hosts.sh auth
#
# "devices" is a second management host serving only device registration and login (Host:Role
# Devices), on its own ports, as the devices container does in deploy/compose.
#
# Logs and pid files go to $E2E_HOST_LOGS (default: e2e-hosts/ at the repo root).

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
logs="${E2E_HOST_LOGS:-$root/e2e-hosts}"
mkdir -p "$logs"

declare -A projects=(
  [auth]=Hosts/Wbskt.Auth.Host
  [management]=Hosts/Wbskt.Management.Host
  [devices]=Hosts/Wbskt.Management.Host
  [socket]=Hosts/Wbskt.Socket.Host
  [engine]=Hosts/Wbskt.Workflow.Engine.Host
)
# The plain-HTTP dev port of each host, for the liveness probe.
declare -A ports=([auth]=5000 [management]=5010 [devices]=5015 [socket]=5020 [engine]=5030)
# Per-service settings on top of appsettings.Development.json, as space-separated KEY=VALUE pairs.
declare -A settings=(
  [devices]="Host__Role=Devices Kestrel__Endpoints__Http__Url=http://localhost:5015 Kestrel__Endpoints__Https__Url=https://localhost:7015"
)

# Management and devices sign device tokens with one key, as both read MANAGEMENT_JWT_SIGNING_KEY in
# deploy/compose. Left unset, each would generate its own, and the socket host, which trusts only
# management's JWKS, would refuse every token devices issues. Kept in a file so restarting one of
# them alone keeps the key the other is using.
if [[ -z "${Jwt__SigningKey:-}" ]]; then
  key_file="$logs/management-signing-key"
  if [[ ! -s "$key_file" ]]; then
    openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt | base64 -w0 > "$key_file"
  fi
  device_token_key="Jwt__SigningKey=$(cat "$key_file")"
  settings[management]="$device_token_key"
  settings[devices]+=" $device_token_key"
fi

if [[ $# -gt 0 ]]; then
  services=("$@")
else
  services=(auth management devices socket engine)
  for project in $(printf '%s\n' "${projects[@]}" | sort -u); do
    dotnet build "$root/$project" --nologo -v q
  done
fi

for s in "${services[@]}"; do
  if [[ -f "$logs/$s.pid" ]] && kill -0 "$(cat "$logs/$s.pid")" 2>/dev/null; then
    kill "$(cat "$logs/$s.pid")"
    for _ in $(seq 1 30); do
      kill -0 "$(cat "$logs/$s.pid")" 2>/dev/null || break
      sleep 1
    done
  fi

  # The built dll, run from the project directory: that directory is the content root the shared
  # Config/ lookup is relative to, and the pid recorded is the host itself rather than a `dotnet run`
  # wrapper that would outlive a kill. The environment is set here rather than taken from the
  # launch profile, so a renamed profile cannot quietly start a host as Production.
  dll="$(basename "${projects[$s]}").dll"
  (
    cd "$root/${projects[$s]}"
    # shellcheck disable=SC2086 # the settings are deliberately word-split into KEY=VALUE pairs
    ASPNETCORE_ENVIRONMENT=Development exec env ${settings[$s]:-} dotnet "bin/Debug/net10.0/$dll"
  ) > "$logs/$s.log" 2>&1 &
  echo $! > "$logs/$s.pid"
done

for s in "${services[@]}"; do
  url="http://localhost:${ports[$s]}/healthz"
  for _ in $(seq 1 90); do
    if curl -fs -o /dev/null "$url"; then
      echo "$s is up"
      continue 2
    fi
    if ! kill -0 "$(cat "$logs/$s.pid")" 2>/dev/null; then
      break
    fi
    sleep 2
  done
  echo "$s did not come up at $url; last log lines:" >&2
  tail -n 50 "$logs/$s.log" >&2
  exit 1
done
