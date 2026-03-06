#!/bin/zsh
set -euo pipefail

PGURL="${RULEFORGE_PGURL:-postgresql://ruleforge:ruleforge_dev_password@127.0.0.1:5432/ruleforge}"
BACKUP_DIR="${RULEFORGE_PG_BACKUP_DIR:-$HOME/backups/ruleforge-postgres}"
KEEP_DAILY="${RULEFORGE_PG_KEEP_DAILY:-7}"
KEEP_WEEKLY="${RULEFORGE_PG_KEEP_WEEKLY:-4}"

mkdir -p "$BACKUP_DIR/daily" "$BACKUP_DIR/weekly"

ts=$(date +%Y%m%d-%H%M%S)
out="$BACKUP_DIR/daily/ruleforge-$ts.dump"

/opt/homebrew/opt/postgresql@16/bin/pg_dump "$PGURL" -Fc -f "$out"
shasum -a 256 "$out" > "$out.sha256"

# daily retention
find "$BACKUP_DIR/daily" -type f -name "ruleforge-*.dump" -print0 | xargs -0 ls -1t 2>/dev/null | awk "NR>${KEEP_DAILY}" | while read -r f; do
  rm -f "$f" "$f.sha256"
done

# weekly snapshot on Sunday
if [[ "$(date +%u)" == "7" ]]; then
  wk="$BACKUP_DIR/weekly/ruleforge-weekly-$ts.dump"
  cp "$out" "$wk"
  cp "$out.sha256" "$wk.sha256"
fi

# weekly retention
find "$BACKUP_DIR/weekly" -type f -name "ruleforge-weekly-*.dump" -print0 | xargs -0 ls -1t 2>/dev/null | awk "NR>${KEEP_WEEKLY}" | while read -r f; do
  rm -f "$f" "$f.sha256"
done

echo "backup_created=$out"
