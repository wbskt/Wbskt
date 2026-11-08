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
# "client-auth" is a second management host serving only client registration and login (Host:Role
# ClientAuth), on its own ports, as the client-auth container does in deploy/compose. "gateway-mqtt"
# listens for plain MQTT on 1883 (TLS is Traefik's in production) and answers health on 5021.
#
# Logs and pid files go to $E2E_HOST_LOGS (default: e2e-hosts/ at the repo root).

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
logs="${E2E_HOST_LOGS:-$root/e2e-hosts}"
mkdir -p "$logs"

declare -A projects=(
  [auth]=Hosts/Wbskt.Auth.Host
  [management]=Hosts/Wbskt.Management.Host
  [client-auth]=Hosts/Wbskt.Management.Host
  [gateway-ws]=Hosts/Wbskt.Gateway.WebSocket.Host
  [gateway-mqtt]=Hosts/Wbskt.Gateway.MQTT.Host
  [engine]=Hosts/Wbskt.Workflow.Engine.Host
)
# The plain-HTTP dev port of each host, for the liveness probe.
declare -A ports=([auth]=5000 [management]=5010 [client-auth]=5015 [gateway-ws]=5020 [gateway-mqtt]=5021 [engine]=5030)
# Per-service settings on top of appsettings.Development.json, as space-separated KEY=VALUE pairs.
# Auth: every scenario's user signs up on Free (one workspace, one member) and then makes a workspace of
# its own (CreateIsolatedWorkspaceAsync) or invites others, so the suite raises those two Free limits.
# Pooled limits the scenarios count against (clients, active runs) stay at Free's real values.
declare -A settings=(
  [auth]="Billing__Plans__Free__Workspaces=1000 Billing__Plans__Free__Members=1000"
  [client-auth]="Host__Role=ClientAuth Kestrel__Endpoints__Http__Url=http://localhost:5015 Kestrel__Endpoints__Https__Url=https://localhost:7015"
)

# Management and client-auth sign client tokens with one key, as both read MANAGEMENT_JWT_SIGNING_KEY in
# deploy/compose. Left unset, each would generate its own, and the gateways, which trust only
# management's JWKS, would refuse every token client-auth issues. Kept in a file so restarting one of
# them alone keeps the key the other is using.
if [[ -z "${Jwt__SigningKey:-}" ]]; then
  key_file="$logs/management-signing-key"
  if [[ ! -s "$key_file" ]]; then
    openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt | base64 -w0 > "$key_file"
  fi
  client_token_key="Jwt__SigningKey=$(cat "$key_file")"
  settings[management]="$client_token_key"
  settings[client-auth]+=" $client_token_key"
fi

if [[ $# -gt 0 ]]; then
  services=("$@")
else
  services=(auth management client-auth gateway-ws gateway-mqtt engine)
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
