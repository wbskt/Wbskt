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

# By default sqlpackage refuses to apply a delta that would destroy existing rows (column
# drops/narrowing) - that refusal is what makes the incremental path safe to rerun blindly.
# Set MIGRATE_ALLOW_DATA_LOSS=true to apply such a delta anyway, after reviewing the error
# sqlpackage printed. Irrelevant when MIGRATE_FRESH=true (the database is recreated empty).
if [ "${MIGRATE_ALLOW_DATA_LOSS:-false}" = "true" ]; then
    BLOCK_ON_POSSIBLE_DATA_LOSS=False
else
    BLOCK_ON_POSSIBLE_DATA_LOSS=True
fi

echo "Publishing Wbskt.Database.Auth (CreateNewDatabase=${CREATE_NEW_DATABASE}, BlockOnPossibleDataLoss=${BLOCK_ON_POSSIBLE_DATA_LOSS})..."
sqlpackage /Action:Publish \
    /SourceFile:/app/Wbskt.Database.Auth.dacpac \
    /TargetConnectionString:"${AUTH_DB_CONNECTION_STRING}" \
    /p:BlockOnPossibleDataLoss="${BLOCK_ON_POSSIBLE_DATA_LOSS}" \
    /p:CreateNewDatabase="${CREATE_NEW_DATABASE}"

echo "Publishing Wbskt.Database (CreateNewDatabase=${CREATE_NEW_DATABASE}, BlockOnPossibleDataLoss=${BLOCK_ON_POSSIBLE_DATA_LOSS})..."
sqlpackage /Action:Publish \
    /SourceFile:/app/Wbskt.Database.dacpac \
    /TargetConnectionString:"${DEFAULT_DB_CONNECTION_STRING}" \
    /p:BlockOnPossibleDataLoss="${BLOCK_ON_POSSIBLE_DATA_LOSS}" \
    /p:CreateNewDatabase="${CREATE_NEW_DATABASE}"

echo "Database migration complete."
