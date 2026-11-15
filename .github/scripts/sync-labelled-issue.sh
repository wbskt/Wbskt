#!/usr/bin/env bash
# Keeps one issue per label in step with a condition a workflow checks: opened (or reopened) while it
# holds, closed once it clears. A new occurrence reopens the last issue rather than filing another, so
# a flapping condition stays one thread.
#
#   .github/scripts/sync-labelled-issue.sh open  <label> <title> <message>
#   .github/scripts/sync-labelled-issue.sh close <label> <title> <message>
#
# While the issue is already open, `open` adds nothing, so a long outage notifies once; set
# COMMENT_IF_OPEN=true to comment each time instead, when every occurrence is worth reading.
#
# Needs GH_TOKEN with issues: write, and the GITHUB_* variables Actions sets. The issue is found by its
# label, which reads the issues directly; a title search goes through the search index, which lags
# and would let two runs close together each open one.
set -euo pipefail

action="${1:?usage: sync-labelled-issue.sh <open|close> <label> <title> <message>}"
label="${2:?label missing}"
title="${3:?title missing}"
message="${4:?message missing}"
repo="$GITHUB_REPOSITORY"
workflow="${GITHUB_WORKFLOW:-a workflow}"

# Made up front: listing by a label that does not exist yet is not something to depend on.
gh label create "$label" --repo "$repo" --color B60205 --description "Opened and closed by CI" 2>/dev/null || true

latest() {
  gh issue list --repo "$repo" --label "$label" --author app/github-actions --state "$1" --limit 1 --json number --jq '.[0].number // empty'
}

case "$action" in
  open)
    open=$(latest open)
    if [[ -n "$open" ]]; then
      if [[ "${COMMENT_IF_OPEN:-false}" == true ]]; then
        gh issue comment "$open" --repo "$repo" --body "$message"
      fi
      echo "Already open as #$open"
      exit 0
    fi
    closed=$(latest closed)
    if [[ -n "$closed" ]]; then
      gh issue reopen "$closed" --repo "$repo" --comment "$message"
    else
      gh issue create --repo "$repo" --title "$title" --label "$label" \
        --body "$message"$'\n\n'"$workflow closes this issue once the condition clears, and reopens it if it comes back."
    fi
    ;;
  close)
    open=$(latest open)
    if [[ -n "$open" ]]; then
      gh issue close "$open" --repo "$repo" --comment "$message"
    fi
    ;;
  *)
    echo "sync-labelled-issue.sh: the action must be open or close, not '$action'" >&2
    exit 1
    ;;
esac
