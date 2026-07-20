#!/bin/bash
set -euo pipefail

: "${AUTH_DB_CONNECTION_STRING:?AUTH_DB_CONNECTION_STRING is required}"
: "${DEFAULT_DB_CONNECTION_STRING:?DEFAULT_DB_CONNECTION_STRING is required}"

# Defaults to incremental (sqlpackage diffs the DACPAC against the live schema and applies only
# the delta - safe to rerun any time, including against a database with real data). Set
# MIGRATE_FRESH=true to drop and recreate both databases instead - only for a brand-new
# environment; never against a database you want to keep.
if [ "${MIGRATE_FRESH:-false}" = "true" ]; then
    CREATE_NEW_DATABASE=True
else
    CREATE_NEW_DATABASE=False
fi

echo "Publishing Wbskt.Database.Auth (CreateNewDatabase=${CREATE_NEW_DATABASE})..."
sqlpackage /Action:Publish \
    /SourceFile:/app/Wbskt.Database.Auth.dacpac \
    /TargetConnectionString:"${AUTH_DB_CONNECTION_STRING}" \
    /p:BlockOnPossibleDataLoss=False \
    /p:CreateNewDatabase="${CREATE_NEW_DATABASE}"

echo "Publishing Wbskt.Database (CreateNewDatabase=${CREATE_NEW_DATABASE})..."
sqlpackage /Action:Publish \
    /SourceFile:/app/Wbskt.Database.dacpac \
    /TargetConnectionString:"${DEFAULT_DB_CONNECTION_STRING}" \
    /p:BlockOnPossibleDataLoss=False \
    /p:CreateNewDatabase="${CREATE_NEW_DATABASE}"

echo "Database migration complete."
