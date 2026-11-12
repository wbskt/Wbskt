#!/usr/bin/env bash
# Keeps one issue, "Outside probe failing", in step with the outside probe: opens it when a probe
# fails, and closes it when one passes. A failure while it is already open adds nothing, so a long
# outage notifies once rather than every 10 minutes.
#
#   .github/scripts/report-outside-probe.sh <success|failure>
#
# Needs GH_TOKEN with issues: write, and the GITHUB_* variables Actions sets.
set -euo pipefail

result="${1:?usage: report-outside-probe.sh <success|failure>}"
title="Outside probe failing"
run_url="$GITHUB_SERVER_URL/$GITHUB_REPOSITORY/actions/runs/$GITHUB_RUN_ID"
now="$(date -u +'%Y-%m-%d %H:%M UTC')"

existing=$(gh issue list --repo "$GITHUB_REPOSITORY" --state open --search "\"$title\" in:title" \
  --json number,title --jq "map(select(.title == \"$title\")) | first | .number // empty")

case "$result" in
  failure)
    if [[ -n "$existing" ]]; then
      echo "Already open as #$existing"
    else
      gh issue create --repo "$GITHUB_REPOSITORY" --title "$title" \
        --body "A public endpoint failed the probe from outside the VM at $now: $run_url"$'\n\n'"The run's log names which. The next probe that passes closes this issue."
    fi
    ;;
  success)
    if [[ -n "$existing" ]]; then
      gh issue close "$existing" --repo "$GITHUB_REPOSITORY" --comment "Every endpoint passed again at $now: $run_url"
    fi
    ;;
  *)
    echo "report-outside-probe.sh: result must be success or failure, not '$result'" >&2
    exit 1
    ;;
esac
