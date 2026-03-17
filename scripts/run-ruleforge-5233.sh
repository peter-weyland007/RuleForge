#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_DIR="$ROOT_DIR/.run"
LOG_FILE="$RUN_DIR/ruleforge.log"
PID_FILE="$RUN_DIR/ruleforge.pid"
DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/Cellar/dotnet/10.0.105/libexec}"
PATH="$DOTNET_ROOT:$PATH"

mkdir -p "$RUN_DIR"

if [[ -f "$PID_FILE" ]]; then
  old_pid="$(cat "$PID_FILE" 2>/dev/null || true)"
  if [[ -n "$old_pid" ]] && kill -0 "$old_pid" 2>/dev/null; then
    echo "RuleForge already running (pid $old_pid)"
    echo "Log: $LOG_FILE"
    exit 0
  fi
fi

cd "$ROOT_DIR"
nohup env \
  DOTNET_ROOT="$DOTNET_ROOT" \
  PATH="$PATH" \
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS=http://0.0.0.0:5233 \
  dotnet run --no-launch-profile \
  >"$LOG_FILE" 2>&1 &

echo $! > "$PID_FILE"
sleep 4

echo "PID: $(cat "$PID_FILE")"
echo "Log: $LOG_FILE"
tail -n 30 "$LOG_FILE" || true
