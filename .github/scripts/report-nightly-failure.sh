#!/usr/bin/env bash
# Opens an issue for a failed nightly run, or comments on the one already open, so a red nightly
# reaches someone: a scheduled run that fails notifies nobody. One open issue per workflow, found by
# its title, collects every failing night until someone closes it.
#
#   .github/scripts/report-nightly-failure.sh "<workflow name>" [<job result>]
#
# Needs GH_TOKEN with issues: write, and the GITHUB_* variables Actions sets. The workflow name goes
# into a search and a jq string as it is, so it must not contain a double quote or a backslash.
set -euo pipefail

workflow="${1:?usage: report-nightly-failure.sh <workflow name> [<job result>]}"
result="${2:-failure}"
title="Nightly run failing: $workflow"
run_url="$GITHUB_SERVER_URL/$GITHUB_REPOSITORY/actions/runs/$GITHUB_RUN_ID"
body="The $workflow run on $(date -u +%Y-%m-%d) ended in $result at ${GITHUB_SHA:0:7}: $run_url"

existing=$(gh issue list --repo "$GITHUB_REPOSITORY" --state open --search "\"$title\" in:title" \
  --json number,title --jq "map(select(.title == \"$title\")) | first | .number // empty")

if [[ -n "$existing" ]]; then
  gh issue comment "$existing" --repo "$GITHUB_REPOSITORY" --body "$body"
  echo "Commented on #$existing"
else
  gh issue create --repo "$GITHUB_REPOSITORY" --title "$title" \
    --body "$body"$'\n\nClose this issue once the nightly is green again; the next failure opens a new one.'
fi
