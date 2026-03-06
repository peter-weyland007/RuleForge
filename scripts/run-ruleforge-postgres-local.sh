#!/bin/zsh
set -euo pipefail
cd /Users/peter/.openclaw/agents/Bishop/workspace/RuleForge

export RULEFORGE_DB_PROVIDER=postgres
export RULEFORGE_POSTGRES_CONNECTION="Host=127.0.0.1;Port=5432;Database=ruleforge;Username=ruleforge;Password=ruleforge_dev_password;Include Error Detail=true"

ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5233 /usr/local/share/dotnet/dotnet run --no-launch-profile
