using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishLearningPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEstudy2Features : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QuestionType",
                table: "Questions",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "MultipleChoice");

            migrationBuilder.AlterColumn<Guid>(
                name: "SelectedOptionId",
                table: "PracticeAnswers",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<bool>(
                name: "IsCorrect",
                table: "PracticeAnswers",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StructuredAnswerJson",
                table: "PracticeAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TextAnswer",
                table: "PracticeAnswers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FlashcardDecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerTeacherUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlashcardDecks", x => x.Id);
                    table.CheckConstraint("CK_FlashcardDecks_Status", "[Status] IN (N'Draft', N'Published', N'Archived')");
                    table.ForeignKey(
                        name: "FK_FlashcardDecks_AspNetUsers_OwnerTeacherUserId",
                        column: x => x.OwnerTeacherUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FlashcardDecks_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FlashcardDecks_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GamificationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WindowKey = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Xp = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamificationEvents", x => x.Id);
                    table.CheckConstraint("CK_GamificationEvents_EventType", "[EventType] IN (N'LessonCompleted', N'PracticeSubmitted', N'PracticeHighScorer', N'FlashcardsReviewed', N'DailyGoalReached')");
                    table.CheckConstraint("CK_GamificationEvents_Xp", "[Xp] > 0");
                    table.ForeignKey(
                        name: "FK_GamificationEvents_AspNetUsers_StudentUserId",
                        column: x => x.StudentUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LearningPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAssessmentAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TimezoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningPlans", x => x.Id);
                    table.CheckConstraint("CK_LearningPlans_Status", "[Status] IN (N'Active', N'Completed', N'Archived')");
                    table.ForeignKey(
                        name: "FK_LearningPlans_AspNetUsers_StudentUserId",
                        column: x => x.StudentUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningPlans_Attempts_SourceAssessmentAttemptId",
                        column: x => x.SourceAssessmentAttemptId,
                        principalTable: "Attempts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PracticeSubmissionHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceCreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SourceUpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StudentUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PracticeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeSubmissionHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuestionAcceptedAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnswerText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionAcceptedAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionAcceptedAnswers_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StudentAchievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BadgeCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AwardedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentAchievements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentAchievements_AspNetUsers_StudentUserId",
                        column: x => x.StudentUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Flashcards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeckId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FrontText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BackText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Example = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Pronunciation = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AudioResourceUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flashcards", x => x.Id);
                    table.CheckConstraint("CK_Flashcards_OrderIndex", "[OrderIndex] >= 0");
                    table.ForeignKey(
                        name: "FK_Flashcards_FlashcardDecks_DeckId",
                        column: x => x.DeckId,
                        principalTable: "FlashcardDecks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LearningPlanItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayIndex = table.Column<int>(type: "int", nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "int", nullable: false),
                    Skill = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningPlanItems", x => x.Id);
                    table.CheckConstraint("CK_LearningPlanItems_DayIndex", "[DayIndex] BETWEEN 1 AND 7");
                    table.CheckConstraint("CK_LearningPlanItems_EstimatedMinutes", "[EstimatedMinutes] BETWEEN 1 AND 30");
                    table.CheckConstraint("CK_LearningPlanItems_Position", "[Position] BETWEEN 1 AND 3");
                    table.CheckConstraint("CK_LearningPlanItems_Skill", "[Skill] IN (N'Vocabulary', N'Grammar', N'Reading', N'Listening')");
                    table.CheckConstraint("CK_LearningPlanItems_Status", "[Status] IN (N'Pending', N'InProgress', N'Completed', N'Skipped', N'Unavailable')");
                    table.CheckConstraint("CK_LearningPlanItems_TargetType", "[TargetType] IN (N'Course', N'Lesson', N'Practice', N'Assessment', N'FlashcardDeck')");
                    table.ForeignKey(
                        name: "FK_LearningPlanItems_LearningPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "LearningPlans",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PracticeAnswerHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HistoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAnswerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceCreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SourceUpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StructuredAnswerJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PracticeQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SelectedOptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TextAnswer = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    QuestionContent = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SelectedAnswer = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CorrectAnswersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeAnswerHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PracticeAnswerHistories_PracticeSubmissionHistories_HistoryId",
                        column: x => x.HistoryId,
                        principalTable: "PracticeSubmissionHistories",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FlashcardReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlashcardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rating = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlashcardReviews", x => x.Id);
                    table.CheckConstraint("CK_FlashcardReviews_Rating", "[Rating] IN (N'Again', N'Hard', N'Good')");
                    table.ForeignKey(
                        name: "FK_FlashcardReviews_AspNetUsers_StudentUserId",
                        column: x => x.StudentUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FlashcardReviews_Flashcards_FlashcardId",
                        column: x => x.FlashcardId,
                        principalTable: "Flashcards",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FlashcardReviewStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlashcardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastReviewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextDueAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Repetitions = table.Column<int>(type: "int", nullable: false),
                    LastRating = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlashcardReviewStates", x => x.Id);
                    table.CheckConstraint("CK_FlashcardReviewStates_Rating", "[LastRating] IN (N'Again', N'Hard', N'Good')");
                    table.CheckConstraint("CK_FlashcardReviewStates_Repetitions", "[Repetitions] >= 0");
                    table.ForeignKey(
                        name: "FK_FlashcardReviewStates_AspNetUsers_StudentUserId",
                        column: x => x.StudentUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FlashcardReviewStates_Flashcards_FlashcardId",
                        column: x => x.FlashcardId,
                        principalTable: "Flashcards",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Questions_QuestionType",
                table: "Questions",
                sql: "[QuestionType] IN (N'MultipleChoice', N'FillBlank', N'Matching', N'Ordering')");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardDecks_CourseId_Status",
                table: "FlashcardDecks",
                columns: new[] { "CourseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardDecks_LessonId",
                table: "FlashcardDecks",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardDecks_OwnerTeacherUserId_Status",
                table: "FlashcardDecks",
                columns: new[] { "OwnerTeacherUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardReviews_FlashcardId",
                table: "FlashcardReviews",
                column: "FlashcardId");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardReviews_ReviewRequestId",
                table: "FlashcardReviews",
                column: "ReviewRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardReviews_StudentUserId_ReviewedAtUtc",
                table: "FlashcardReviews",
                columns: new[] { "StudentUserId", "ReviewedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardReviewStates_FlashcardId",
                table: "FlashcardReviewStates",
                column: "FlashcardId");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardReviewStates_StudentUserId_FlashcardId",
                table: "FlashcardReviewStates",
                columns: new[] { "StudentUserId", "FlashcardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardReviewStates_StudentUserId_NextDueAtUtc",
                table: "FlashcardReviewStates",
                columns: new[] { "StudentUserId", "NextDueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Flashcards_DeckId_OrderIndex",
                table: "Flashcards",
                columns: new[] { "DeckId", "OrderIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GamificationEvents_StudentUserId_EventType_SourceId_WindowKey",
                table: "GamificationEvents",
                columns: new[] { "StudentUserId", "EventType", "SourceId", "WindowKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GamificationEvents_StudentUserId_OccurredAtUtc",
                table: "GamificationEvents",
                columns: new[] { "StudentUserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningPlanItems_PlanId_DayIndex_Position",
                table: "LearningPlanItems",
                columns: new[] { "PlanId", "DayIndex", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearningPlans_SourceAssessmentAttemptId",
                table: "LearningPlans",
                column: "SourceAssessmentAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningPlans_StudentUserId",
                table: "LearningPlans",
                column: "StudentUserId",
                unique: true,
                filter: "[Status] = N'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_LearningPlans_StudentUserId_StartDate",
                table: "LearningPlans",
                columns: new[] { "StudentUserId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PracticeAnswerHistories_HistoryId_PracticeQuestionId",
                table: "PracticeAnswerHistories",
                columns: new[] { "HistoryId", "PracticeQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSubmissionHistories_StudentUserId_PracticeId_SubmittedAtUtc",
                table: "PracticeSubmissionHistories",
                columns: new[] { "StudentUserId", "PracticeId", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionAcceptedAnswers_QuestionId_OrderIndex",
                table: "QuestionAcceptedAnswers",
                columns: new[] { "QuestionId", "OrderIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentAchievements_StudentUserId_BadgeCode",
                table: "StudentAchievements",
                columns: new[] { "StudentUserId", "BadgeCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refuse a downgrade that would erase new learning records or invent MCQ option IDs.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [FlashcardDecks])
                    OR EXISTS (SELECT 1 FROM [FlashcardReviews])
                    OR EXISTS (SELECT 1 FROM [LearningPlans])
                    OR EXISTS (SELECT 1 FROM [GamificationEvents])
                    OR EXISTS (SELECT 1 FROM [StudentAchievements])
                    OR EXISTS (SELECT 1 FROM [PracticeSubmissionHistories])
                    OR EXISTS (SELECT 1 FROM [QuestionAcceptedAnswers])
                    OR EXISTS (SELECT 1 FROM [Questions] WHERE [QuestionType] <> N'MultipleChoice')
                    OR EXISTS (SELECT 1 FROM [PracticeAnswers] WHERE [SelectedOptionId] IS NULL)
                THROW 51000, 'Estudy 2.0 downgrade blocked: preserve or export learning data before removing this schema.', 1;
                """);
            migrationBuilder.DropTable(
                name: "FlashcardReviews");

            migrationBuilder.DropTable(
                name: "FlashcardReviewStates");

            migrationBuilder.DropTable(
                name: "GamificationEvents");

            migrationBuilder.DropTable(
                name: "LearningPlanItems");

            migrationBuilder.DropTable(
                name: "PracticeAnswerHistories");

            migrationBuilder.DropTable(
                name: "QuestionAcceptedAnswers");

            migrationBuilder.DropTable(
                name: "StudentAchievements");

            migrationBuilder.DropTable(
                name: "Flashcards");

            migrationBuilder.DropTable(
                name: "LearningPlans");

            migrationBuilder.DropTable(
                name: "PracticeSubmissionHistories");

            migrationBuilder.DropTable(
                name: "FlashcardDecks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Questions_QuestionType",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "QuestionType",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "IsCorrect",
                table: "PracticeAnswers");

            migrationBuilder.DropColumn(
                name: "StructuredAnswerJson",
                table: "PracticeAnswers");

            migrationBuilder.DropColumn(
                name: "TextAnswer",
                table: "PracticeAnswers");

            migrationBuilder.AlterColumn<Guid>(
                name: "SelectedOptionId",
                table: "PracticeAnswers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
