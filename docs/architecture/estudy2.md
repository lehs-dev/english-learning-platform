# Estudy 2.0 — M0, M1 and M2

## Scope and product defaults

Decks belong to an owned course and optionally its lesson. Practice earns 5 XP plus 10 XP for a score of at least 80%, each once per Vietnam day and practice. FillBlank uses NFC, trim, collapsed whitespace and OrdinalIgnoreCase, preserving punctuation and spelling. Plans target at most two weak skills. Teacher analytics is restricted to owned courses and exposes names and learning results.

Other initial reward values are centralized in `GamificationPolicy`: a lesson earns 10 XP once ever; reviewing five distinct cards in one deck earns 5 XP once per day; two distinct qualifying activities earn a 10 XP daily goal bonus. A practice's score bonus is not a separate activity. Badges recognize the first lesson, five different practices, and a seven-day streak. Dates and daily windows use `Asia/Ho_Chi_Minh`.

M0 provides the additive schema. M1 adds MCQ/FillBlank practices, the reading workspace, flashcards, seven-day plans, achievements and teacher analytics. M2 enables all four question types in Practice and weighted Assessment and adds 14- and 30-day plans.

## Data and access

`AddEstudy2Features` introduces nullable Practice answers and defaults existing questions to `MultipleChoice`. `AddEstudyM2` adds nullable Assessment response fields and definition storage for Matching/Ordering, widens the text snapshot for Practice retakes, and defaults existing plans to seven days. New plans accept 7, 14 or 30 days. Existing attempt IDs, scores and rows are retained. Both migrations refuse downgrades that would remove learning records.

The existing latest-Practice-result contract is preserved. Before replacing a latest result, its original IDs, timestamps, version, score, raw answers, correctness, question text, explanation and accepted/correct-answer snapshot are copied into `PracticeSubmissionHistories` / `PracticeAnswerHistories` in the same transaction. The EF immutability guard rejects modifications or deletions to these snapshots and protects accepted answers and question type once used/locked. Bulk SQL remains outside the EF guard, as with existing content locks.

Teacher authoring validates each type. FillBlank accepts up to 500 characters. Matching uses 2–10 unique pairs and Ordering uses 2–10 unique items. The worksheet presents opaque choice IDs and shuffled choices. The server awards the full weight of a structured question when every pair or position is correct. Invalid, duplicate or foreign selection IDs are rejected. Practice gives detailed feedback after submission. Assessment stores responses during the timed attempt and reveals the learner's typed or structured answer and points after grading while keeping its answer key private.

Flashcard availability requires a valid enrollment, a published course/deck, matching teacher ownership, and an effective optional lesson belonging to that course. Review keys are globally unique; exact replay returns the existing outcome, changed payloads and early repeat reviews are rejected. Course/deck transactions serialize reviews and authoring; the unique student/card state index is a second safeguard. Published decks must be archived rather than changing previously reviewed card content.

## Plans, rewards and reports

Dashboard and planner share `RepresentativeAssessmentQueries` to select assessment evidence, including the grading snapshot for typed or structured Assessment answers. Planning uses at least three questions per skill and a score below 60%. Newer evidence supersedes older evidence for the same skill. Only enrolled and published activities qualify. Lessons and decks appear once per plan. Practices can recur on different days. Each scheduled day must contain 2–3 activities totaling 15–30 minutes. The duration control disables options for which there is too little content, and an active plan keeps its duration until it expires or is completed.

Completion is checked against server-side learning records for the scheduled Vietnam day. A completed activity does not complete future repetitions. Skips require the owning student. Unavailable content is projected as unavailable without changing historical completion/skip records. A filtered unique index plus a per-student transaction lock prevents duplicate active plans.

Source progress is committed before reward/plan hooks run, avoiding lock inversions across simultaneous activities. Repeated lesson completions and exact practice/flashcard replays can recover hooks after an interrupted request. Reward writes and badges share a per-student transaction lock and unique `(StudentUserId, EventType, SourceId, WindowKey)` keys. Award requests require persisted source evidence and the fixed policy amount. There is no browser endpoint accepting arbitrary XP. A durable background outbox is not included; if a post-commit hook fails, retrying the action recovers it.

Analytics restricts every SQL query to the active teacher's courses and valid enrollments. Filters use inclusive start / exclusive next-day Vietnam boundaries. Counts, averages, skill statistics, most-missed questions and paginated student progress are aggregated in SQL; no payment fields enter the DTO. Practice averages describe latest results; immutable archives are retained separately. Empty datasets show zero progress and absent scores.

## Targeted verification

Never run an unfiltered solution test. The helper runs named classes **sequentially**, builds with one worker, and refuses integration mode unless an existing SQL connection is configured:

```bash
dotnet restore EnglishLearningPlatform.sln --disable-parallel
bash scripts/test-estudy2.sh unit
# Set ELP_TEST_SQLSERVER privately to the existing SQL Server first.
bash scripts/test-estudy2.sh integration
```

The integration fixture replaces any supplied database name with a unique `EnglishLearningPlatformTests_<guid>` database and drops only that temporary database. It does not create a SQL container when `ELP_TEST_SQLSERVER` is set. Do not print connection strings or commit credentials.

Migration commands for review/release:

```bash
dotnet ef migrations script 20261028171139_AddPracticeVersionsAndSessions AddEstudy2Features --idempotent \
  --project src/EnglishLearningPlatform.Infrastructure \
  --startup-project src/EnglishLearningPlatform.Web --output /tmp/estudy2-migration.sql
```

For the M2 upgrade alone, generate the script from `AddEstudy2Features` to `AddEstudyM2` with the same project arguments.

Before release, use a backed-up staging copy containing legacy MCQ results, inspect the generated SQL, apply it, and compare the retained row counts/answers. A downgrade containing Estudy learning data requires a preservation/export plan and is deliberately blocked by the migration.

`tests/browser/estudy2-uat.mjs` runs the teacher and student journey in Chromium against a newly seeded demo database. It takes screenshots of the 390px reading workspace, card flip and weighted Assessment result. With Playwright installed under `tests/browser`, set `ELP_UAT_URL` to the temporary app and `LearningDemo__Password` to that database's seed password, then run `node tests/browser/estudy2-uat.mjs` from the repo root. Evidence defaults to `artifacts/estudy2-uat`.

## M0/M1 verification results — 2026-11-19

Web and integration builds passed with zero warnings/errors. `dotnet ef migrations has-pending-model-changes` reported no pending model changes. Idempotent upgrade SQL was generated at `/tmp/estudy2-migration.sql`; the development database was not migrated.

All 201 tests in the eight explicitly filtered unit classes passed: `FillBlankEvaluatorTests`, `LearningPathPlannerTests`, `FlashcardScheduleTests`, `GamificationPolicyTests`, `CreateQuestionValidatorTests`, `QuestionUsageValidatorTests`, `AssessmentDefinitionValidatorTests`, and `ExamScoringServiceTests`.

The following integration classes ran sequentially, each with its own explicit filter and the existing SQL Server connection. All 155 tests passed; no test SQL containers were created.

| Class | Passed |
| --- | ---: |
| MixedPracticeSubmissionTests | 4 |
| ContentHistoryProtectionTests | 24 |
| FlashcardReviewTests | 6 |
| LearningPathAuthorizationTests | 4 |
| GamificationRewardTests | 3 |
| TeacherAnalyticsAuthorizationTests | 3 |
| EstudyMigrationCompatibilityTests | 4 |
| EstudyWebFlowTests | 3 |
| PracticeQuestionValidationTests | 43 |
| LearningDashboardFeatureTests | 29 |
| TeacherAssessmentQuestionsTests | 31 |
| PracticeVersionMigrationTests | 1 |

Migration tests compare exact legacy table snapshots before/after the upgrade and verify safe rollback/refusal. HTTP tests exercise new pages, anti-forgery checks, review replay/due counts, and XP after five distinct cards. The legacy avatar regression uses temporary storage to avoid modifying private application files. Visual browser UAT, including 390px overflow checks and animation behavior, remains unperformed.

## M2 verification results — 2026-11-19

All 221 tests across ten selected unit classes and 155 tests across sixteen selected integration classes passed. Integration ran one class at a time on the existing SQL Server with disposable fixture databases. The M2 migration test kept M1 attempt and plan rows intact, stored a nullable option for a typed answer, added a day-30 item, and confirmed that downgrade refused to remove those records.

Chromium started with freshly seeded data. Teacher1 authored all four question types and published them in Practice and Assessment. Student1 scored 100 in Practice twice and kept the same 15 XP reward, then resumed Assessment after reloading and earned 12 out of 15 points (80/100). The 30-day plan recorded both a skip and a completed activity. Five card reviews left zero due and awarded 5 XP plus the 10 XP daily goal. Teacher2 was denied access to the selected course's analytics.

The browser run checked the collapsible reading pane and flashcard flip at 390px. `artifacts/estudy2-uat/results.json` and the three accompanying PNGs record the results. The local Development database was backed up and verified before applying `AddEstudyM2`. Existing user, question, attempt, response, submission and plan-item counts matched after the upgrade.
