#!/usr/bin/env bash
# Rehearses the production upgrade: the previous release's migrator builds both databases, every
# table gets a row, then this commit's migrator upgrades them. Fails when the upgrade would lose
# data (sqlpackage's BlockOnPossibleDataLoss, or a table with fewer rows afterwards) or leaves the
# schema short of this commit's DACPAC (a non-empty DeployReport). See "Upgrade rehearsal" in
# deploy/README.md.
#
#   PREVIOUS_IMAGE   the migrator production last ran, e.g. ghcr.io/wbskt/wbskt-migrator:latest
#   CURRENT_IMAGE    this commit's migrator, built locally
#   SQL_CONTAINER    the SQL Server container to run sqlcmd in (its port 1433 is on localhost)
#   SA_PASSWORD      that server's sa password

set -euo pipefail

: "${PREVIOUS_IMAGE:?}" "${CURRENT_IMAGE:?}" "${SQL_CONTAINER:?}" "${SA_PASSWORD:?}"

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
out="${REHEARSAL_OUT:-$PWD/rehearsal-out}"
mkdir -p "$out"

auth_db=Wbskt.Database.Auth
core_db=Wbskt.Database
host_password=Wb1-Rehearsal-Login

conn() { echo "Server=localhost,1433;Database=$1;User ID=sa;Password=$SA_PASSWORD;Encrypt=False;TrustServerCertificate=True;"; }

migrate() {
  local image=$1 fresh=$2
  docker run --rm --network host \
    -e AUTH_DB_CONNECTION_STRING="$(conn "$auth_db")" \
    -e DEFAULT_DB_CONNECTION_STRING="$(conn "$core_db")" \
    -e SQL_AUTH_PASSWORD="$host_password" \
    -e SQL_MANAGEMENT_PASSWORD="$host_password" \
    -e SQL_ENGINE_PASSWORD="$host_password" \
    -e MIGRATE_FRESH="$fresh" \
    "$image"
}

docker cp "$here/seed-every-table.sql" "$SQL_CONTAINER:/tmp/seed-every-table.sql"
docker cp "$here/row-counts.sql" "$SQL_CONTAINER:/tmp/row-counts.sql"

# -y 0: no width cap, so a long constraint error in the seed log is not cut off. sqlcmd refuses
# -h alongside it, so the header lines stay; every reader greps for the lines it wants. -I: sqlcmd
# defaults QUOTED_IDENTIFIER to OFF, which refuses inserts into tables with filtered indexes.
sql() {
  local db=$1 file=$2
  docker exec "$SQL_CONTAINER" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$SA_PASSWORD" -C -b -I -d "$db" -y 0 -i "$file" | sed 's/[[:space:]]*$//'
}

echo "::group::Previous release: $PREVIOUS_IMAGE"
docker pull "$PREVIOUS_IMAGE"
migrate "$PREVIOUS_IMAGE" true
echo "::endgroup::"

status=0
for db in "$auth_db" "$core_db"; do
  echo "::group::Seed $db"
  sql "$db" /tmp/seed-every-table.sql | tee "$out/seed-$db.txt"
  echo "::endgroup::"
  grep '^UNSEEDED|' "$out/seed-$db.txt" | while IFS='|' read -r _ table error; do
    echo "::warning::$db $table was left empty, so the rehearsal cannot see data loss in it: $error"
  done || true
  sql "$db" /tmp/row-counts.sql | grep '|' > "$out/before-$db.txt"
done

echo "::group::This commit: $CURRENT_IMAGE"
if ! migrate "$CURRENT_IMAGE" false; then
  echo "::error::This commit's migrator failed against the previous release's schema with data in it. deploy.sh runs it the same way, so the deploy would fail too. If sqlpackage reported possible data loss, see \"Upgrade rehearsal\" in deploy/README.md."
  exit 1
fi
echo "::endgroup::"

for db in "$auth_db" "$core_db"; do
  sql "$db" /tmp/row-counts.sql | grep '|' > "$out/after-$db.txt"
  # A table that lost rows. One that is gone is only a warning: a rename with a refactorlog entry
  # looks the same, and sqlpackage leaves a table removed from the model in place.
  while IFS='|' read -r table before; do
    after=$(awk -F'|' -v t="$table" '$1 == t { print $2 }' "$out/after-$db.txt")
    if [[ -z "$after" ]]; then
      echo "::warning::$db $table had $before rows before the upgrade and is gone after it"
    elif [[ "$after" -lt "$before" ]]; then
      echo "::error::$db $table had $before rows before the upgrade and $after after"
      status=1
    fi
  done < "$out/before-$db.txt"
done

# The upgraded databases against this commit's DACPACs: anything left to do means the migrator
# stopped short, e.g. a change it skipped.
report() {
  local db=$1 dacpac=$2
  shift 2
  docker run --rm --network host -v "$out:/out" --entrypoint sqlpackage "$CURRENT_IMAGE" \
    /Action:DeployReport \
    /SourceFile:"/app/$dacpac" \
    /TargetConnectionString:"$(conn "$db")" \
    /OutputPath:"/out/deploy-report-$db.xml" \
    /OverwriteFiles:True \
    "$@"
  if grep -q '<Operation ' "$out/deploy-report-$db.xml"; then
    echo "::error::$db still differs from this commit's DACPAC after the upgrade:"
    grep -o '<Operation Name="[^"]*">\|<Item Value="[^"]*"' "$out/deploy-report-$db.xml" | sed 's/^/  /'
    status=1
  fi
}

report "$auth_db" Wbskt.Database.Auth.dacpac /v:AuthHostPassword="$host_password"
report "$core_db" Wbskt.Database.dacpac /v:ManagementHostPassword="$host_password" /v:EngineHostPassword="$host_password"

if [[ $status -eq 0 ]]; then
  echo "Upgrade from $PREVIOUS_IMAGE rehearsed: no data lost, schema matches this commit."
fi
exit $status
