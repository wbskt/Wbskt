#!/usr/bin/env bash
# The checked-in OpenAPI snapshots of the auth and management hosts (see README.md here).
#
#   Tests/Wbskt.E2E.FeatureTests/openapi/refresh.sh           # rewrite both snapshots from the running hosts
#   Tests/Wbskt.E2E.FeatureTests/openapi/refresh.sh --check   # diff the running hosts against them (CI)
#
# Each host serves its document at /openapi/v1.json while API docs are on, which they are in
# Development (ApiDocs:Enabled, default on there); start-hosts.sh and e2e-up.sh run the hosts so.
# A document is normalized before it is written or compared: keys sorted (jq -S) and "servers"
# dropped, since that names the address the document was fetched from rather than the API.
#
# --check fails with a diff when a document differs from its snapshot. A snapshot that does not
# exist yet is written instead, with a warning, so the first run creates it to be committed. Either
# way the documents fetched are also written to $OPENAPI_OUT (default: openapi-current/ at the repo
# root), which CI uploads, so a changed document can be committed from the run.
#
# The hosts' plain-HTTP dev ports are the default; OPENAPI_AUTH_URL and OPENAPI_MANAGEMENT_URL
# override them.

set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../../.." && pwd)"

check=false
case "${1:-}" in
  "") ;;
  --check) check=true ;;
  *) echo "usage: $0 [--check]" >&2; exit 2 ;;
esac

command -v jq >/dev/null || { echo "jq is required" >&2; exit 2; }

declare -A urls=(
  [auth]="${OPENAPI_AUTH_URL:-http://localhost:5000}"
  [management]="${OPENAPI_MANAGEMENT_URL:-http://localhost:5010}"
)

out="${OPENAPI_OUT:-$root/openapi-current}"
if $check; then
  mkdir -p "$out"
fi

status=0
for host in auth management; do
  snapshot="$here/$host.v1.json"
  url="${urls[$host]}/openapi/v1.json"

  fetched="$(mktemp)"
  if ! curl -fsS "$url" | jq -S 'del(.servers)' > "$fetched"; then
    echo "::error::could not fetch the $host host's OpenAPI document from $url (is it running as Development?)"
    rm -f "$fetched"
    status=1
    continue
  fi

  if ! $check; then
    mv "$fetched" "$snapshot"
    echo "wrote ${snapshot#"$root"/}"
    continue
  fi

  cp "$fetched" "$out/$host.v1.json"

  if [[ ! -f "$snapshot" ]]; then
    cp "$fetched" "$snapshot"
    echo "::warning::snapshot created, commit it: ${snapshot#"$root"/} did not exist, so it was written from $url and nothing was compared. Commit it (CI uploads it as the openapi-snapshots artifact) and later runs diff against it."
  elif diff -u --label "${snapshot#"$root"/} (checked in)" --label "$url (running host)" "$snapshot" "$fetched"; then
    echo "$host: the OpenAPI document matches its snapshot"
  else
    echo "::error::the $host host's OpenAPI document differs from ${snapshot#"$root"/} (diff above). If the change is intended, run Tests/Wbskt.E2E.FeatureTests/openapi/refresh.sh against the hosts and commit the result, or take $host.v1.json from this run's openapi-snapshots artifact."
    status=1
  fi
  rm -f "$fetched"
done

exit $status
