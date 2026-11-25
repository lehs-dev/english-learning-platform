using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishLearningPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseCollaborationAndActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Questions_QuestionType",
                table: "Questions");

            migrationBuilder.CreateTable(
                name: "CourseDiscussionThreads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseDiscussionThreads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseDiscussionThreads_Enrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Enrollments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StudentActivityDays",
                columns: table => new
                {
                    StudentUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentActivityDays", x => new { x.StudentUserId, x.ActivityDate });
                    table.ForeignKey(
                        name: "FK_StudentActivityDays_AspNetUsers_StudentUserId",
                        column: x => x.StudentUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WritingAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WritingAssignments_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CourseDiscussionMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ThreadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    WrittenByTeacher = table.Column<bool>(type: "bit", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseDiscussionMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseDiscussionMessages_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CourseDiscussionMessages_CourseDiscussionThreads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "CourseDiscussionThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WritingSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Response = table.Column<string>(type: "nvarchar(max)", maxLength: 6000, nullable: false),
                    Feedback = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Score = table.Column<int>(type: "int", nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingSubmissions", x => x.Id);
                    table.CheckConstraint("CK_WritingSubmissions_Review", "([ReviewedAtUtc] IS NULL AND [Feedback] IS NULL AND [Score] IS NULL) OR ([ReviewedAtUtc] IS NOT NULL AND [Feedback] IS NOT NULL AND ([Score] IS NULL OR [Score] BETWEEN 0 AND 100))");
                    table.ForeignKey(
                        name: "FK_WritingSubmissions_AspNetUsers_StudentUserId",
                        column: x => x.StudentUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WritingSubmissions_WritingAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "WritingAssignments",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Questions_QuestionType",
                table: "Questions",
                sql: "[QuestionType] IN (N'MultipleChoice', N'FillBlank', N'Matching', N'Ordering', N'Dictation')");

            migrationBuilder.CreateIndex(
                name: "IX_CourseDiscussionMessages_AuthorUserId",
                table: "CourseDiscussionMessages",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseDiscussionMessages_ThreadId_SequenceNumber",
                table: "CourseDiscussionMessages",
                columns: new[] { "ThreadId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseDiscussionThreads_EnrollmentId",
                table: "CourseDiscussionThreads",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingAssignments_CourseId_CreatedAtUtc_Id",
                table: "WritingAssignments",
                columns: new[] { "CourseId", "CreatedAtUtc", "Id" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_WritingSubmissions_AssignmentId_ReviewedAtUtc_CreatedAtUtc_Id",
                table: "WritingSubmissions",
                columns: new[] { "AssignmentId", "ReviewedAtUtc", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingSubmissions_AssignmentId_StudentUserId",
                table: "WritingSubmissions",
                columns: new[] { "AssignmentId", "StudentUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingSubmissions_StudentUserId",
                table: "WritingSubmissions",
                column: "StudentUserId");

            migrationBuilder.Sql("""
                WITH [ActivityEvidence] AS (
                    SELECT [StudentUserId], [OccurredAtUtc]
                    FROM [GamificationEvents]
                    UNION ALL
                    SELECT e.[StudentUserId], COALESCE(p.[UpdatedAtUtc], p.[CreatedAtUtc])
                    FROM [LessonProgress] AS p
                    INNER JOIN [Enrollments] AS e ON e.[Id] = p.[EnrollmentId]
                    WHERE p.[IsCompleted] = 1
                        AND NOT EXISTS (
                            SELECT 1 FROM [GamificationEvents] AS g
                            WHERE g.[StudentUserId] = e.[StudentUserId]
                                AND g.[SourceId] = p.[LessonId]
                                AND g.[EventType] = N'LessonCompleted'
                        )
                    UNION ALL
                    SELECT [StudentUserId], [SubmittedAtUtc] FROM [PracticeSubmissions]
                    UNION ALL
                    SELECT [StudentUserId], [SubmittedAtUtc] FROM [PracticeSubmissionHistories]
                    UNION ALL
                    SELECT [StudentUserId], [ReviewedAtUtc] FROM [FlashcardReviews]
                    UNION ALL
                    SELECT a.[StudentUserId],
                        CASE WHEN a.[FinalizationReason] = N'DeadlineElapsed'
                            THEN answers.[LastSavedAtUtc] ELSE a.[FinalizedAtUtc] END
                    FROM [Attempts] AS a
                    CROSS APPLY (
                        SELECT MAX(aa.[SavedAtUtc]) AS [LastSavedAtUtc]
                        FROM [AttemptAnswers] AS aa
                        WHERE aa.[AttemptId] = a.[Id]
                            AND (aa.[SelectedOptionId] IS NOT NULL
                                OR NULLIF(LTRIM(RTRIM(aa.[TextAnswer])), N'') IS NOT NULL
                                OR NULLIF(LTRIM(RTRIM(aa.[StructuredAnswerJson])), N'') IS NOT NULL)
                    ) AS answers
                    WHERE a.[Status] = N'Graded' AND a.[FinalizedAtUtc] IS NOT NULL
                        AND answers.[LastSavedAtUtc] IS NOT NULL
                )
                INSERT INTO [StudentActivityDays] ([StudentUserId], [ActivityDate])
                SELECT DISTINCT e.[StudentUserId], CAST(SWITCHOFFSET(e.[OccurredAtUtc], '+07:00') AS date)
                FROM [ActivityEvidence] AS e
                INNER JOIN [AspNetUsers] AS u ON u.[Id] = e.[StudentUserId]
                WHERE e.[OccurredAtUtc] IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [CourseDiscussionThreads])
                    OR EXISTS (SELECT 1 FROM [WritingAssignments])
                    OR EXISTS (SELECT 1 FROM [StudentActivityDays])
                    OR EXISTS (SELECT 1 FROM [Questions] WHERE [QuestionType] = N'Dictation')
                    OR EXISTS (SELECT 1 FROM [LessonResources] WHERE [ResourceType] = N'Image'
                        OR [ResourceUrl] LIKE N'/LessonMedia/%')
                THROW 51000, 'Estudy downgrade blocked because learning records require the newer schema.', 1;
                """);

            migrationBuilder.DropTable(
                name: "CourseDiscussionMessages");

            migrationBuilder.DropTable(
                name: "StudentActivityDays");

            migrationBuilder.DropTable(
                name: "WritingSubmissions");

            migrationBuilder.DropTable(
                name: "CourseDiscussionThreads");

            migrationBuilder.DropTable(
                name: "WritingAssignments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Questions_QuestionType",
                table: "Questions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Questions_QuestionType",
                table: "Questions",
                sql: "[QuestionType] IN (N'MultipleChoice', N'FillBlank', N'Matching', N'Ordering')");
        }
    }
}
