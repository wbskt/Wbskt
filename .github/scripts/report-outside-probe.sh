#!/usr/bin/env bash
# Keeps one issue, "Outside probe failing", in step with the outside probe: open while probes fail,
# closed once one passes. A failure while it is open adds nothing, so a long outage notifies once
# rather than every 10 minutes, and a new failure reopens the last one rather than filing another,
# so a flapping endpoint stays one thread.
#
#   FAILED="<failed URLs>" .github/scripts/report-outside-probe.sh <success|failure>
#
# Needs GH_TOKEN with issues: write, and the GITHUB_* variables Actions sets. The issue is found by its
# label, which reads the issues directly; a title search goes through the search index, which lags
# and would let two runs close together each open one.
set -euo pipefail

result="${1:?usage: report-outside-probe.sh <success|failure>}"
repo="$GITHUB_REPOSITORY"
label="outside-probe"
title="Outside probe failing"
run_url="$GITHUB_SERVER_URL/$repo/actions/runs/$GITHUB_RUN_ID"
now="$(date -u +'%Y-%m-%d %H:%M UTC')"

latest() {
  gh issue list --repo "$repo" --label "$label" --state "$1" --limit 1 --json number --jq '.[0].number // empty'
}

case "$result" in
  failure)
    what="${FAILED:-the probe job itself (no endpoint result; see the run)}"
    open=$(latest open)
    if [[ -n "$open" ]]; then
      echo "Already open as #$open"
      exit 0
    fi
    closed=$(latest closed)
    if [[ -n "$closed" ]]; then
      gh issue reopen "$closed" --repo "$repo" --comment "Failing again at $now: $what. $run_url"
    else
      gh label create "$label" --repo "$repo" --color B60205 \
        --description "Opened and closed by outside-probe.yml" 2>/dev/null || true
      gh issue create --repo "$repo" --title "$title" --label "$label" \
        --body "The probe from outside the VM failed at $now: $what. $run_url"$'\n\n'"The next probe that passes closes this issue, and a later failure reopens it."
    fi
    ;;
  success)
    open=$(latest open)
    if [[ -n "$open" ]]; then
      gh issue close "$open" --repo "$repo" --comment "Every endpoint passed again at $now: $run_url"
    fi
    ;;
  *)
    echo "report-outside-probe.sh: result must be success or failure, not '$result'" >&2
    exit 1
    ;;
esac
