#!/usr/bin/env bash
set -euo pipefail

printf '\n== English Learning Platform: Dev Container setup ==\n'
printf 'dotnet: '; dotnet --version
printf 'git: '; git --version
printf 'docker: '; docker --version || true
printf 'gh: '; gh --version | head -n 1 || true

if dotnet tool list --global | awk '{print $1}' | grep -qx 'dotnet-ef'; then
  dotnet tool update --global dotnet-ef --version '10.*'
else
  dotnet tool install --global dotnet-ef --version '10.*'
fi

export PATH="$PATH:$HOME/.dotnet/tools"
printf 'dotnet-ef: '; dotnet ef --version

dotnet restore EnglishLearningPlatform.sln

echo
echo 'Dev Container ready.'
echo 'Build: dotnet build EnglishLearningPlatform.sln'
echo 'Test : dotnet test EnglishLearningPlatform.sln'
echo 'Run  : dotnet run --project src/EnglishLearningPlatform.Web'
echo 'DB   : Server=db,1433 inside the Dev Container'
