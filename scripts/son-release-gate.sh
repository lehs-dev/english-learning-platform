#!/usr/bin/env bash
set -euo pipefail

# Validate a committed candidate; this script never merges, tags or publishes.
elp_repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$elp_repo_root"
elp_expected_sha="$(git rev-parse --verify --end-of-options "${1:-HEAD}^{commit}")"
elp_results_directory="${ELP_RELEASE_RESULTS:-$(mktemp -d "${TMPDIR:-/tmp}/elp-release.XXXXXX")}"

check_candidate() {
    if [[ "$(git rev-parse HEAD)" != "$elp_expected_sha" ]]; then
        printf 'Candidate HEAD changed; rerun against the intended commit.\n' >&2
        exit 1
    fi
    if [[ -n "$(git status --porcelain)" ]]; then
        printf 'Commit the candidate changes before running the release gate.\n' >&2
        exit 1
    fi
    git diff --check
}

check_candidate
mkdir -p "$elp_results_directory"
elp_results_directory="$(cd "$elp_results_directory" && pwd)"
printf '%s\n' "$elp_expected_sha" > "$elp_results_directory/candidate-sha.txt"
dotnet --version > "$elp_results_directory/sdk-version.txt"

run_gate() {
    local elp_log_name="$1"
    shift
    # Best-effort console redaction; this does not sanitize directly written TRX.
    # Keep evidence configuration/output secret-free and inspect files before sharing.
    "$@" 2>&1 | sed -E 's/((Password|Pwd|SigningSecret)[=:])[^;[:space:]]+/\1[REDACTED]/Ig' \
        | tee "$elp_results_directory/$elp_log_name.log"
}

run_gate restore dotnet restore EnglishLearningPlatform.sln
run_gate build dotnet build EnglishLearningPlatform.sln -c Release --no-restore
run_gate unit dotnet test tests/EnglishLearningPlatform.UnitTests/EnglishLearningPlatform.UnitTests.csproj \
    -c Release --no-build --no-restore --logger 'trx;LogFileName=unit.trx' --results-directory "$elp_results_directory"
run_gate integration dotnet test tests/EnglishLearningPlatform.IntegrationTests/EnglishLearningPlatform.IntegrationTests.csproj \
    -c Release --no-build --no-restore --logger 'trx;LogFileName=integration.trx' --results-directory "$elp_results_directory"

check_candidate
printf 'Local release gate passed for %s. Evidence: %s\n' "$elp_expected_sha" "$elp_results_directory"
printf 'Match this SHA to the green GitHub CI run and complete the rehearsal before tagging.\n'
