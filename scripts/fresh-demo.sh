#!/usr/bin/env bash
set -euo pipefail

elp_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$elp_root"
elp_project="${1:-elp-rc-$(date -u +%Y%m%d%H%M%S)}"
if [[ ! "$elp_project" =~ ^elp-rc-[a-z0-9-]+$ ]]; then
    printf 'Use an isolated project name beginning with elp-rc-.\n' >&2
    exit 1
fi
: "${MSSQL_SA_PASSWORD:?Set MSSQL_SA_PASSWORD through environment or ignored local config}"
: "${LearningDemo__Password:?Set LearningDemo__Password through environment}"
if [[ -n "$(docker volume ls -q --filter "label=com.docker.compose.project=$elp_project")" ]] ||
   [[ -n "$(docker ps -aq --filter "label=com.docker.compose.project=$elp_project")" ]]; then
    printf 'Project already has data/resources; choose a new name for fresh setup.\n' >&2
    exit 1
fi
export LearningDemo__Enabled=true ASPNETCORE_ENVIRONMENT=Development
export Payments__Enabled=false CheckoutSandbox__Enabled=true
export ELP_SQL_PORT="${ELP_SQL_PORT:-1434}" ELP_WEB_PORT="${ELP_WEB_PORT:-8081}"
docker compose --project-name "$elp_project" up --build --detach
printf 'Fresh project: %s. Browser URL: http://localhost:%s\n' "$elp_project" "$ELP_WEB_PORT"
printf 'Check /health, then run the browser suite against this fresh database.\n'
printf 'Resources remain available for review. No existing database/volume is reset.\n'
