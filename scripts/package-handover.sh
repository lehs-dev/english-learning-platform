#!/usr/bin/env bash
set -euo pipefail
elp_handover_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$elp_handover_root"
# All exports use the editable Markdown sources; no network package or office suite is required.
dotnet run --project scripts/HandoverExport/HandoverExport.csproj -c Release -- "$elp_handover_root"
if [[ "${1:-}" == "--bundle" ]]; then
    elp_bundle_output="${ELP_HANDOVER_BUNDLE:-/tmp/elp-handover.tar.gz}"
    # Explicit file allowlist avoids .env, secrets, database dumps, build outputs and attachment data.
    tar -czf "$elp_bundle_output" docs/handover \
        docs/project/son-demo-fallback.md docs/acceptance/evidence/son-sprint-16-10-evidence.md \
        docs/architecture/assessment-student-queries.md docs/architecture/payment-sandbox.md \
        scripts/package-handover.sh scripts/HandoverExport/Program.cs scripts/HandoverExport/HandoverExport.csproj
    printf 'Handover bundle: %s\n' "$elp_bundle_output"
fi
