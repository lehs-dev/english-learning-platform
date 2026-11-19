#!/usr/bin/env bash
# Run explicitly selected classes sequentially. Never falls back to all tests or a new SQL container.
set -euo pipefail
cd "$(dirname "$0")/.."

case "${1:-unit}" in
  unit)
    project=tests/EnglishLearningPlatform.UnitTests/EnglishLearningPlatform.UnitTests.csproj
    classes=(FillBlankEvaluatorTests StructuredQuestionEvaluatorTests LearningPathPlannerTests FlashcardScheduleTests GamificationPolicyTests ExamScoringServiceTests)
    ;;
  integration)
    : "${ELP_TEST_SQLSERVER:?Set ELP_TEST_SQLSERVER to the existing SQL Server; fixtures create isolated temporary databases.}"
    project=tests/EnglishLearningPlatform.IntegrationTests/EnglishLearningPlatform.IntegrationTests.csproj
    classes=(MixedPracticeSubmissionTests M2PracticeSubmissionTests M2AssessmentFlowTests ContentHistoryProtectionTests FlashcardReviewTests LearningPathAuthorizationTests GamificationRewardTests TeacherAnalyticsAuthorizationTests EstudyMigrationCompatibilityTests M2MigrationCompatibilityTests EstudyWebFlowTests)
    ;;
  *) printf 'Usage: bash scripts/test-estudy2.sh [unit|integration]\n' >&2; exit 2 ;;
esac

dotnet build "$project" --no-restore -m:1
for class in "${classes[@]}"; do
  dotnet test "$project" --no-build --no-restore -m:1 \
    --filter "FullyQualifiedName~${class}" --logger 'console;verbosity=minimal'
done
