#!/bin/zsh
cd /Users/peter/.openclaw/agents/Bishop/workspace/RuleForge || exit 1
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5233 /usr/local/share/dotnet/dotnet run --no-launch-profile >> /tmp/ruleforge-5233.log 2>&1
