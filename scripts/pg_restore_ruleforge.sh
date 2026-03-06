#!/bin/zsh
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "usage: $0 <dump-file> <target-db> [--drop-create]"
  exit 1
fi

DUMP_FILE="$1"
TARGET_DB="$2"
DROP_CREATE="${3:-}"

PGHOST="${RULEFORGE_PGHOST:-127.0.0.1}"
PGPORT="${RULEFORGE_PGPORT:-5432}"
PGUSER="${RULEFORGE_PGUSER:-ruleforge}"
PGPASSWORD="${RULEFORGE_PGPASSWORD:-ruleforge_dev_password}"
export PGPASSWORD

if [[ ! -f "$DUMP_FILE" ]]; then
  echo "dump not found: $DUMP_FILE"
  exit 1
fi

if [[ "$DROP_CREATE" == "--drop-create" ]]; then
  /opt/homebrew/opt/postgresql@16/bin/psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d postgres -c "DROP DATABASE IF EXISTS \"$TARGET_DB\";"
  /opt/homebrew/opt/postgresql@16/bin/psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d postgres -c "CREATE DATABASE \"$TARGET_DB\" OWNER \"$PGUSER\";"
fi

/opt/homebrew/opt/postgresql@16/bin/pg_restore -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$TARGET_DB" --clean --if-exists "$DUMP_FILE"

echo "restore_complete db=$TARGET_DB dump=$DUMP_FILE"
