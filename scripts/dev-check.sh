#!/usr/bin/env bash
set -euo pipefail

printf '== Toolchain ==\n'
dotnet --version
dotnet ef --version
git --version
docker --version
docker compose version
gh --version | head -n 1 || true

printf '\n== Containers ==\n'
docker ps --format 'table {{.Names}}\t{{.Image}}\t{{.Status}}'

printf '\n== Restore / Build / Test ==\n'
dotnet restore EnglishLearningPlatform.sln
dotnet build EnglishLearningPlatform.sln --no-restore
dotnet test EnglishLearningPlatform.sln --no-build

printf '\nEnvironment OK.\n'
