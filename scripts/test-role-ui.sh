#!/usr/bin/env bash
set -euo pipefail

elp_repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$elp_repo_root"

usage() {
    cat <<'EOF'
Usage: bash scripts/test-role-ui.sh [options]

Build with one MSBuild worker, discover test classes, and run one class at a time.
  --suite all|unit|integration|browser  Select suite (default: all .NET tests)
  --class TEXT                        Keep classes containing this literal text
  --browser                           Also run browser fixture files sequentially
  --no-build                          Use existing Debug binaries
  --list                              Discover/list selected classes without running tests
  --keep-going                        Continue with remaining classes after a failure
  --results PATH                      Write logs, TRX and summary.tsv here
  --help                              Show this help

ELP_TEST_SQLSERVER is reused when set; each fixture creates its own GUID database.
Otherwise Testcontainers starts/disposes SQL Server for the current class only.
Browser fixtures require npm ci and an installed Playwright Chromium, but no DB.
EOF
}

elp_suite=all
elp_class_filter=
elp_browser=false
elp_build=true
elp_list=false
elp_keep_going=false
elp_results_directory=
while (( $# )); do
    case "$1" in
        --suite|--class|--results)
            if (( $# < 2 )) || [[ "$2" == --* ]]; then
                printf 'Missing value for %s\n' "$1" >&2
                exit 2
            fi
            case "$1" in
                --suite) elp_suite="$2" ;;
                --class) elp_class_filter="$2" ;;
                --results) elp_results_directory="$2" ;;
            esac
            shift 2 ;;
        --browser) elp_browser=true; shift ;;
        --no-build) elp_build=false; shift ;;
        --list) elp_list=true; shift ;;
        --keep-going) elp_keep_going=true; shift ;;
        --help|-h) usage; exit 0 ;;
        *) printf 'Unknown option: %s\n' "$1" >&2; usage >&2; exit 2 ;;
    esac
done
case "$elp_suite" in
    all) elp_projects=(EnglishLearningPlatform.UnitTests EnglishLearningPlatform.IntegrationTests) ;;
    unit) elp_projects=(EnglishLearningPlatform.UnitTests) ;;
    integration) elp_projects=(EnglishLearningPlatform.IntegrationTests) ;;
    browser) elp_projects=(); elp_browser=true ;;
    *) printf 'Unknown suite: %s\n' "$elp_suite" >&2; exit 2 ;;
esac
if [[ "$elp_suite" == browser && -n "$elp_class_filter" ]]; then
    printf '%s\n' '--class applies only to .NET suites.' >&2
    exit 2
fi

if [[ -z "$elp_results_directory" ]]; then
    mkdir -p TestResults
    elp_results_directory="$(mktemp -d "$elp_repo_root/TestResults/role-ui.XXXXXX")"
fi
mkdir -p "$elp_results_directory"
elp_results_directory="$(cd "$elp_results_directory" && pwd)"
elp_settings="$elp_repo_root/tests/low-memory.runsettings"
elp_summary="$elp_results_directory/summary.tsv"
printf 'suite\tclass_or_file\tstatus\n' > "$elp_summary"
printf 'Sequential verification. Reports: %s\n' "$elp_results_directory"

assert_test_results() {
    python3 - "$1" <<'PY'
import pathlib
import re
import sys

try:
    trx = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8-sig")
    counters = re.search(r'<(?:[\w.-]+:)?Counters\b[^>]*\btotal="([0-9]+)"', trx)
    if counters is None or int(counters.group(1)) == 0:
        raise ValueError("No tests ran for the selected class.")
except (OSError, ValueError) as error:
    raise SystemExit(str(error))
PY
}

elp_failures=0
elp_selected=0
for elp_project in "${elp_projects[@]}"; do
    elp_project_file="tests/$elp_project/$elp_project.csproj"
    if "$elp_build"; then
        printf 'Building %s with one worker...\n' "$elp_project"
        if ! dotnet build "$elp_project_file" -m:1 /nodeReuse:false \
            -p:UseSharedCompilation=false -p:BuildInParallel=false -p:RestoreDisableParallel=true \
            > "$elp_results_directory/$elp_project.build.log" 2>&1; then
            printf '%s\tbuild\tFAILED\n' "$elp_project" >> "$elp_summary"
            printf 'Build failed; see %s\n' "$elp_results_directory/$elp_project.build.log" >&2
            exit 1
        fi
    fi

    elp_discovery="$elp_results_directory/$elp_project.discovery.log"
    if ! dotnet test "$elp_project_file" --no-build --no-restore --list-tests \
        --settings "$elp_settings" > "$elp_discovery" 2>&1; then
        printf '%s\tdiscovery\tFAILED\n' "$elp_project" >> "$elp_summary"
        printf 'Discovery failed; see %s\n' "$elp_discovery" >&2
        exit 1
    fi

    # Derive classes from discovered test names, so partial classes run once and
    # new classes are automatically covered. Never derive classes from filenames.
    elp_class_file="$elp_results_directory/$elp_project.classes.txt"
    python3 - "$elp_discovery" "$elp_class_file" <<'PY'
import pathlib
import re
import sys

discovery = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8-sig")
pattern = r"^\s*(EnglishLearningPlatform\.(?:UnitTests|IntegrationTests)(?:\.\w+)+)\.\w+(?:\(|$)"
classes = sorted(set(re.findall(pattern, discovery, flags=re.MULTILINE)))
if not classes:
    raise SystemExit("No test classes found; inspect the discovery log before running tests.")
pathlib.Path(sys.argv[2]).write_text("\n".join(classes) + "\n", encoding="utf-8")
PY
    while IFS= read -r elp_class; do
        [[ "$elp_class" == *"$elp_class_filter"* ]] || continue
        elp_selected=$((elp_selected + 1))
        if "$elp_list"; then
            printf '%s\n' "$elp_class"
            continue
        fi
        printf 'Testing %s...\n' "$elp_class"
        # A previous run in an explicitly reused results directory must never
        # satisfy the nonempty-results check for this invocation.
        rm -f -- "$elp_results_directory/$elp_class.trx"
        if dotnet test "$elp_project_file" --no-build --no-restore \
            --settings "$elp_settings" --filter "FullyQualifiedName~$elp_class." \
            --logger "trx;LogFileName=$elp_class.trx" --results-directory "$elp_results_directory" \
            > "$elp_results_directory/$elp_class.log" 2>&1 && \
            assert_test_results "$elp_results_directory/$elp_class.trx" \
                >> "$elp_results_directory/$elp_class.log" 2>&1; then
            printf '%s\t%s\tPASSED\n' "$elp_project" "$elp_class" >> "$elp_summary"
        else
            printf '%s\t%s\tFAILED\n' "$elp_project" "$elp_class" >> "$elp_summary"
            printf 'Failed: %s (see its log/TRX)\n' "$elp_class" >&2
            elp_failures=$((elp_failures + 1))
            if ! "$elp_keep_going"; then
                printf 'Stopped after failure. Report: %s\n' "$elp_summary" >&2
                exit 1
            fi
        fi
    done < "$elp_class_file"
done

if (( elp_selected == 0 && ${#elp_projects[@]} > 0 )); then
    printf 'No classes matched %s. No tests were run.\n' "$elp_class_filter" >&2
    exit 2
fi

if "$elp_browser"; then
    for elp_browser_file in tests/browser/*.test.mjs; do
        elp_browser_name="$(basename "$elp_browser_file")"
        if "$elp_list"; then
            printf '%s\n' "$elp_browser_file"
            continue
        fi
        printf 'Testing %s...\n' "$elp_browser_file"
        if node --test --test-concurrency=1 "$elp_browser_file" \
            > "$elp_results_directory/$elp_browser_name.log" 2>&1; then
            printf 'browser\t%s\tPASSED\n' "$elp_browser_name" >> "$elp_summary"
        else
            printf 'browser\t%s\tFAILED\n' "$elp_browser_name" >> "$elp_summary"
            printf 'Failed: %s (see its log)\n' "$elp_browser_file" >&2
            elp_failures=$((elp_failures + 1))
            if ! "$elp_keep_going"; then
                printf 'Stopped after failure. Report: %s\n' "$elp_summary" >&2
                exit 1
            fi
        fi
    done
fi

printf 'Selected %s .NET classes. Failures: %s. Report: %s\n' "$elp_selected" "$elp_failures" "$elp_summary"
(( elp_failures == 0 ))
