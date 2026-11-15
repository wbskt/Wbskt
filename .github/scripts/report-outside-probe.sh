#!/usr/bin/env bash
# Keeps one issue, "Outside probe failing", in step with the outside probe: open while probes fail,
# closed once one passes (see sync-labelled-issue.sh).
#
#   FAILED="<failed URLs>" .github/scripts/report-outside-probe.sh <success|failure>
set -euo pipefail

result="${1:?usage: report-outside-probe.sh <success|failure>}"
run_url="$GITHUB_SERVER_URL/$GITHUB_REPOSITORY/actions/runs/$GITHUB_RUN_ID"
now="$(date -u +'%Y-%m-%d %H:%M UTC')"
sync="$(dirname "$0")/sync-labelled-issue.sh"

case "$result" in
  failure)
    what="${FAILED:-the probe job itself (no endpoint result; see the run)}"
    "$sync" open outside-probe "Outside probe failing" "The probe from outside the VM failed at $now: $what. $run_url"
    ;;
  success)
    "$sync" close outside-probe "Outside probe failing" "Every endpoint passed again at $now: $run_url"
    ;;
  *)
    echo "report-outside-probe.sh: result must be success or failure, not '$result'" >&2
    exit 1
    ;;
esac
