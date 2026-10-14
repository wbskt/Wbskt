#!/usr/bin/env bash
# Takes a dependency of the E2E hosts down, or brings it back, for the outage scenarios
# (Tests/Wbskt.E2E.FeatureTests/Scenarios/Outage, run with E2E_OUTAGE_COMMAND set to this script).
#
#   outage.sh broker-stop | broker-start     # the RabbitMQ container
#   outage.sh redis-stop  | redis-start      # the Redis container
#   outage.sh engine2-start                  # a second engine beside the first, as a standby
#   outage.sh engine-kill-leader             # kill -9 whichever engine holds the lease
#   outage.sh engine-restore                 # stop the second engine; the original one leads again
#   outage.sh restore-all                    # all of the above back as they were
#
# The containers are named by E2E_RABBITMQ_CONTAINER and E2E_REDIS_CONTAINER (an id or a name; in
# CI, job.services.<name>.id). The engines are the hosts start-hosts.sh runs from source: the
# original on its dev ports (5030/7030), the second on 5031/7031, against the same database and
# broker. Every action waits until what it changed has settled, and bringing back something that
# is already up does nothing, so a scenario can always restore in a finally.
#
# Logs and pid files share start-hosts.sh's directory, $E2E_HOST_LOGS (default: e2e-hosts/ at the
# repo root), so CI's "Host logs" step prints the second engine's log with the others.

set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
logs="${E2E_HOST_LOGS:-$root/e2e-hosts}"
mkdir -p "$logs"

engine_port=5030
engine2_port=5031
engine2_https_port=7031

# How long a new leader may take: the lease (WorkflowEngine:LeaderElectionLeaseTtlSeconds, 30 s by
# default) has to run out after a kill, the standby notices on its next renewal tick (10 s), and
# then starts its bus. Three minutes leaves room for a slow runner.
leader_wait_seconds=180

log() { echo "outage.sh: $*" >&2; }

# The functions below return their failures explicitly rather than leaning on set -e, which bash
# suspends inside a function called as `f || status=1` (as restore-all does).

container_for() {
  local var="$1"
  if [[ -z "${!var:-}" ]]; then
    log "$var is not set; it names the container to stop and start"
    return 1
  fi
  echo "${!var}"
}

# Waits for a container to run and, when it has a health check, to report healthy.
wait_container_healthy() {
  local container="$1" state="" health=""
  local deadline=$((SECONDS + 180))
  while ((SECONDS < deadline)); do
    state="$(docker inspect --format '{{.State.Status}}' "$container")" || return 1
    health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$container")" || return 1
    if [[ "$state" == "running" && ( "$health" == "healthy" || "$health" == "none" ) ]]; then
      log "$container is $state ($health)"
      return 0
    fi
    sleep 2
  done
  log "$container did not become healthy (last: $state, $health)"
  docker logs --tail 50 "$container" >&2 || true
  return 1
}

container_stop() {
  local container
  container="$(container_for "$1")" || return 1
  docker stop "$container" > /dev/null || return 1
  log "stopped $container"
}

container_start() {
  local container
  container="$(container_for "$1")" || return 1
  # Starting a running container is a no-op, which is what makes restoring safe to repeat.
  docker start "$container" > /dev/null || return 1
  wait_container_healthy "$container"
}

pid_alive() {
  [[ -f "$1" ]] && kill -0 "$(cat "$1")" 2>/dev/null
}

# 200 from /healthz/ready on exactly the instance that holds the lease and has started its bus
# (LeadershipHealthCheck); a standby always answers 503 there.
is_ready() {
  curl -fs -o /dev/null --max-time 5 "http://localhost:$1/healthz/ready"
}

wait_ready() {
  local port="$1"
  local deadline=$((SECONDS + leader_wait_seconds))
  while ((SECONDS < deadline)); do
    if is_ready "$port"; then
      log "the engine on $port holds the lease"
      return 0
    fi
    sleep 1
  done
  log "no engine became leader on $port within ${leader_wait_seconds}s"
  return 1
}

# Stops a process by pid file: SIGTERM, then SIGKILL if it has not gone within 30 seconds.
stop_pid() {
  local pid_file="$1"
  if ! pid_alive "$pid_file"; then
    return 0
  fi
  local pid
  pid="$(cat "$pid_file")"
  kill "$pid" 2>/dev/null || true
  for _ in $(seq 1 30); do
    kill -0 "$pid" 2>/dev/null || return 0
    sleep 1
  done
  kill -9 "$pid" 2>/dev/null || true
}

engine2_start() {
  local pid_file="$logs/engine2.pid"
  if pid_alive "$pid_file"; then
    log "the second engine is already running"
    return 0
  fi

  # The same build and content root as start-hosts.sh's engine (that directory is what the shared
  # Config/ lookup is relative to), on its own ports. Output goes to a file: an inherited stdout
  # would keep the caller's pipe open for as long as the engine runs.
  (
    cd "$root/Hosts/Wbskt.Workflow.Engine.Host"
    ASPNETCORE_ENVIRONMENT=Development exec env \
      Kestrel__Endpoints__Http__Url="http://localhost:$engine2_port" \
      Kestrel__Endpoints__Https__Url="https://localhost:$engine2_https_port" \
      dotnet bin/Debug/net10.0/Wbskt.Workflow.Engine.Host.dll
  ) > "$logs/engine2.log" 2>&1 < /dev/null &
  echo $! > "$pid_file"

  for _ in $(seq 1 90); do
    if curl -fs -o /dev/null --max-time 5 "http://localhost:$engine2_port/healthz"; then
      log "the second engine is up on $engine2_port"
      return 0
    fi
    if ! pid_alive "$pid_file"; then
      break
    fi
    sleep 2
  done
  log "the second engine did not come up; last log lines:"
  tail -n 50 "$logs/engine2.log" >&2 || true
  return 1
}

engine_kill_leader() {
  # Readiness also covers the database and the bus, so a leader can be briefly unready (its bus
  # reconnecting after the broker outage, say). Ask for a while before concluding there is none.
  local deadline=$((SECONDS + 60))
  while ((SECONDS < deadline)); do
    local port pid_file
    for port in "$engine_port" "$engine2_port"; do
      if is_ready "$port"; then
        if [[ "$port" == "$engine_port" ]]; then pid_file="$logs/engine.pid"; else pid_file="$logs/engine2.pid"; fi
        if ! pid_alive "$pid_file"; then
          log "the engine on $port leads but its pid file $pid_file names no live process"
          return 1
        fi
        local pid
        pid="$(cat "$pid_file")"
        # A crash, not a shutdown: nothing is drained and the lease is not released, so the
        # standby has to wait it out.
        kill -9 "$pid" || return 1
        for _ in $(seq 1 30); do
          kill -0 "$pid" 2>/dev/null || break
          sleep 1
        done
        log "killed the leader on $port (pid $pid)"
        echo "$port"
        return 0
      fi
    done
    sleep 1
  done
  log "no engine answered ready on $engine_port or $engine2_port"
  return 1
}

engine_restore() {
  stop_pid "$logs/engine2.pid"
  rm -f "$logs/engine2.pid"
  if ! pid_alive "$logs/engine.pid"; then
    # start-hosts.sh with a host named restarts only that host, without building.
    "$here/start-hosts.sh" engine < /dev/null >&2 || return 1
  fi
  # The second engine did not release the lease as it stopped, so the original may have to wait it out.
  wait_ready "$engine_port"
}

case "${1:-}" in
  broker-stop)        container_stop E2E_RABBITMQ_CONTAINER || exit 1 ;;
  broker-start)       container_start E2E_RABBITMQ_CONTAINER || exit 1 ;;
  redis-stop)         container_stop E2E_REDIS_CONTAINER || exit 1 ;;
  redis-start)        container_start E2E_REDIS_CONTAINER || exit 1 ;;
  engine2-start)      engine2_start || exit 1 ;;
  engine-kill-leader) engine_kill_leader || exit 1 ;;
  engine-restore)     engine_restore || exit 1 ;;
  restore-all)
    # Each in turn, and every one attempted even if an earlier one failed. A container left unnamed
    # was never stopped by this script, so there is nothing to bring back.
    status=0
    if [[ -n "${E2E_RABBITMQ_CONTAINER:-}" ]]; then container_start E2E_RABBITMQ_CONTAINER || status=1; fi
    if [[ -n "${E2E_REDIS_CONTAINER:-}" ]]; then container_start E2E_REDIS_CONTAINER || status=1; fi
    engine_restore || status=1
    exit $status
    ;;
  *)
    echo "usage: $0 broker-stop|broker-start|redis-stop|redis-start|engine2-start|engine-kill-leader|engine-restore|restore-all" >&2
    exit 2
    ;;
esac
