#!/usr/bin/env bash
# Rehearses the production upgrade: the previous release's migrator builds both databases, every
# table gets a row, then this commit's migrator upgrades them. Fails when the upgrade would lose
# data (sqlpackage's BlockOnPossibleDataLoss, or a table with fewer rows afterwards), leaves the
# schema short of this commit's DACPAC (a non-empty DeployReport), cannot run a second time, or
# leaves behind objects the DACPAC no longer defines. See "Upgrade rehearsal" in deploy/README.md.
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
    -e SQL_ADMIN_READER_PASSWORD="$host_password" \
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
# stopped short, e.g. a change it skipped. Run after the first upgrade and again after a second one,
# which must find nothing left either: deploy.sh reruns the migrator on every deploy, so a migration
# that only works once (a post-deployment step that is not idempotent, a pre-deployment guard that
# assumes the old shape) would fail the next one.
report() {
  local db=$1 dacpac=$2 name=$3
  shift 3
  docker run --rm --network host -v "$out:/out" --entrypoint sqlpackage "$CURRENT_IMAGE" \
    /Action:DeployReport \
    /SourceFile:"/app/$dacpac" \
    /TargetConnectionString:"$(conn "$db")" \
    /OutputPath:"/out/$name-$db.xml" \
    /OverwriteFiles:True \
    "$@"
}

operations() {
  grep -o '<Operation Name="[^"]*">\|<Item Value="[^"]*"' "$1" | sed 's/^/  /'
}

check_converged() {
  local pass=$1
  report "$auth_db" Wbskt.Database.Auth.dacpac "deploy-report-$pass" /v:AuthHostPassword="$host_password" /v:AdminReaderPassword="$host_password"
  report "$core_db" Wbskt.Database.dacpac "deploy-report-$pass" /v:ManagementHostPassword="$host_password" /v:EngineHostPassword="$host_password" /v:AdminReaderPassword="$host_password"
  for db in "$auth_db" "$core_db"; do
    if grep -q '<Operation ' "$out/deploy-report-$pass-$db.xml"; then
      echo "::error::$db still differs from this commit's DACPAC after the $pass upgrade:"
      operations "$out/deploy-report-$pass-$db.xml"
      status=1
    fi
  done
}

check_converged first

echo "::group::This commit again: $CURRENT_IMAGE"
if ! migrate "$CURRENT_IMAGE" false; then
  echo "::error::This commit's migrator failed the second time it ran against the same databases. deploy.sh runs it on every deploy, so the next deploy would fail."
  status=1
fi
echo "::endgroup::"
check_converged second

# Objects the previous release had that this commit's DACPACs no longer define. sqlpackage leaves
# them in place (the migrator never drops what is not in the source), so a procedure removed from
# the project would otherwise stay in production, callable, for good. This report only lists them;
# remove one on purpose with a guarded DROP in a pre-deployment script. The logins, users, role
# memberships and grants the post-deployment scripts create are not in the model and are excluded.
left_behind() {
  local db=$1 dacpac=$2
  shift 2
  report "$db" "$dacpac" left-behind \
    /p:DropObjectsNotInSource=True \
    "/p:DoNotDropObjectTypes=Users;Logins;RoleMembership;Permissions" \
    "$@"
  if grep -q '<Operation Name="Drop">' "$out/left-behind-$db.xml"; then
    echo "::error::$db keeps objects this commit's DACPAC no longer defines. Drop them in a pre-deployment script, or keep them in the project:"
    sed -n '/<Operation Name="Drop">/,/<\/Operation>/p' "$out/left-behind-$db.xml" | grep -o '<Item Value="[^"]*"' | sed 's/^/  /'
    status=1
  fi
}

left_behind "$auth_db" Wbskt.Database.Auth.dacpac /v:AuthHostPassword="$host_password" /v:AdminReaderPassword="$host_password"
left_behind "$core_db" Wbskt.Database.dacpac /v:ManagementHostPassword="$host_password" /v:EngineHostPassword="$host_password" /v:AdminReaderPassword="$host_password"

if [[ $status -eq 0 ]]; then
  echo "Upgrade from $PREVIOUS_IMAGE rehearsed: no data lost, schema matches this commit, reruns cleanly, nothing left behind."
fi
exit $status
