#!/usr/bin/env bash
set -euo pipefail

elp_repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$elp_repo_root"

usage() {
    cat <<'EOF'
Usage: bash scripts/test-student-flow.sh [options]

Run student regression tests one class at a time with tests/low-memory.runsettings.
  --group all|auth|catalog|learning|practice|checkout|assessment|completion|browser
                Select a journey (default: all .NET groups; browser is explicit).
  --no-build    Use existing Debug binaries.
  --list        List selected classes without building or running tests.
  --results DIR Save individual logs/TRX and a summary here.
  --help        Show this help.

ELP_TEST_SQLSERVER selects a SQL Server; fixtures create/drop their own GUID DBs.
Without it, each fixture starts/disposes its own SQL Server test container.
Browser requires Node, the pinned tests/browser dependencies, and Chromium.
EOF
}

elp_group=all
elp_build=true
elp_list=false
elp_results=
while (( $# )); do
    case "$1" in
        --group|--results)
            if (( $# < 2 )) || [[ "$2" == --* ]]; then usage >&2; exit 2; fi
            if [[ "$1" == --group ]]; then elp_group="$2"; else elp_results="$2"; fi
            shift 2 ;;
        --no-build) elp_build=false; shift ;;
        --list) elp_list=true; shift ;;
        --help|-h) usage; exit 0 ;;
        *) printf 'Unknown option: %s\n' "$1" >&2; usage >&2; exit 2 ;;
    esac
done

case "$elp_group" in
    all) elp_groups=(auth catalog learning practice checkout assessment completion) ;;
    auth|catalog|learning|practice|checkout|assessment|completion|browser) elp_groups=("$elp_group") ;;
    *) printf 'Unknown group: %s\n' "$elp_group" >&2; exit 2 ;;
esac

# Some journeys share a class. In an all-groups run, execute that class only once.
declare -A elp_seen=()
elp_selections=()
select_class() {
    local elp_key="$1:$2"
    if [[ -z "${elp_seen[$elp_key]:-}" ]]; then
        elp_seen[$elp_key]=true
        elp_selections+=("$elp_current_group:$1:$2")
    fi
}
for elp_current_group in "${elp_groups[@]}"; do
    case "$elp_current_group" in
        auth)
            select_class unit ActiveRoleAuthorizationHandlerTests
            select_class integration AccountAuthorizationTests
            select_class integration ProfileAndLoginTests
            select_class integration RoleBasedSmartRoutingTests ;;
        catalog)
            select_class unit CourseRulesTests
            select_class integration CourseFeatureTests ;;
        learning)
            select_class unit LearningAccessServiceTests
            select_class integration LearningAccessTests
            select_class integration EnrollmentProgressTests
            select_class integration LearningDashboardFeatureTests ;;
        practice)
            select_class unit ScoringStrategyTests
            select_class unit PracticeServiceAuthorizationTests
            select_class integration LearningDashboardFeatureTests ;;
        checkout)
            select_class unit PaymentWebhookProcessorTests
            select_class unit PaymentConfirmationServiceTests
            select_class unit SandboxCheckoutServiceTests
            select_class integration CheckoutFlowTests
            select_class integration CheckoutFeatureTests
            # CheckoutMergeTests.cs is another part of CheckoutFlowTests.
            select_class integration TransactionHistoryTests
            select_class integration CommercePaymentTests
            select_class integration PaidEnrollmentAssessmentAccessTests ;;
        assessment)
            select_class unit ExamScoringServiceTests
            select_class unit SkillAnalysisRulesTests
            select_class integration AssessmentQueryServiceTests
            select_class integration AssessmentAttemptStartTests
            select_class integration AssessmentUiFlowTests
            select_class integration AssessmentLifecycleAcceptanceTests ;;
        completion)
            select_class integration CourseCompletionIntegrationTests
            select_class integration LearningCommerceJourneyTests ;;
        browser) select_class browser fixtures ;;
    esac
done

if "$elp_list"; then
    printf '%s\n' "${elp_selections[@]}"
    exit 0
fi

if [[ -z "$elp_results" ]]; then
    mkdir -p TestResults
    elp_results="$(mktemp -d "$elp_repo_root/TestResults/student-flow.XXXXXX")"
fi
mkdir -p "$elp_results"
elp_results="$(cd "$elp_results" && pwd)"
printf 'group\tsuite\tclass_or_file\tstatus\n' > "$elp_results/summary.tsv"
printf 'Sequential student verification. Reports: %s\n' "$elp_results"

if "$elp_build" && [[ "$elp_group" != browser ]]; then
    # One build worker and no compiler/build server left running between groups.
    for elp_project in EnglishLearningPlatform.UnitTests EnglishLearningPlatform.IntegrationTests; do
        if ! dotnet build "tests/$elp_project/$elp_project.csproj" -m:1 /nodeReuse:false \
            -p:UseSharedCompilation=false -p:BuildInParallel=false -p:RestoreDisableParallel=true \
            > "$elp_results/$elp_project.build.log" 2>&1; then
            printf 'Build failed; see %s\n' "$elp_results/$elp_project.build.log" >&2
            exit 1
        fi
    done
fi

for elp_selection in "${elp_selections[@]}"; do
    IFS=: read -r elp_current_group elp_suite elp_class <<< "$elp_selection"
    elp_destination="$elp_results/$elp_current_group/$elp_class"
    elp_args=(--suite "$elp_suite" --no-build --results "$elp_destination")
    if [[ "$elp_suite" != browser ]]; then elp_args+=(--class "$elp_class"); fi
    if bash scripts/test-role-ui.sh "${elp_args[@]}"; then
        printf '%s\t%s\t%s\tPASSED\n' "$elp_current_group" "$elp_suite" "$elp_class" >> "$elp_results/summary.tsv"
    else
        printf '%s\t%s\t%s\tFAILED\n' "$elp_current_group" "$elp_suite" "$elp_class" >> "$elp_results/summary.tsv"
        printf 'Stopped after failure. See %s\n' "$elp_destination" >&2
        exit 1
    fi
done
printf 'Completed %s sequential selections. Summary: %s\n' "${#elp_selections[@]}" "$elp_results/summary.tsv"
