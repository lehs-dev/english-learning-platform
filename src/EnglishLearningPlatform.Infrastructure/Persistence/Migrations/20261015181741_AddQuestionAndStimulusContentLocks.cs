using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishLearningPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionAndStimulusContentLocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContentLockedAtUtc",
                table: "Stimuli",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContentLockedAtUtc",
                table: "Questions",
                type: "datetimeoffset",
                nullable: true);
            // Ghi nhận khóa cho dữ liệu cũ theo các điều kiện
            // đang được QuestionAuthoringService kiểm tra.
            // Không thay đổi nội dung câu hỏi, đáp án hoặc kết quả.
            migrationBuilder.Sql(@"
                -- Đây là thời điểm migration ghi nhận khóa,
                -- không phải thời điểm Publish lần đầu trong quá khứ.
                DECLARE @lockedAtUtc datetimeoffset =
                    TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');

                UPDATE q
                SET q.ContentLockedAtUtc = @lockedAtUtc
                FROM dbo.Questions AS q
                WHERE q.ContentLockedAtUtc IS NULL
                AND
                (
                    -- Giữ nguyên quy tắc khóa theo trạng thái Question hiện có.
                    q.[Status] IN (N'Published', N'Archived')

                    -- Question đang thuộc Assessment không còn Draft.
                    OR EXISTS
                    (
                        SELECT 1
                        FROM dbo.AssessmentQuestions AS aq
                        INNER JOIN dbo.Assessments AS a
                            ON a.Id = aq.AssessmentId
                        WHERE aq.QuestionId = q.Id
                            AND a.[Status] <> N'Draft'
                    )

                    -- Question đang thuộc Practice không còn Draft.
                    OR EXISTS
                    (
                        SELECT 1
                        FROM dbo.PracticeQuestions AS pq
                        INNER JOIN dbo.Practices AS p
                            ON p.Id = pq.PracticeId
                        WHERE pq.QuestionId = q.Id
                            AND p.[Status] <> N'Draft'
                    )

                    -- Assessment đã có Attempt, kể cả chưa Submit.
                    OR EXISTS
                    (
                        SELECT 1
                        FROM dbo.AssessmentQuestions AS aq
                        INNER JOIN dbo.Attempts AS attempt
                            ON attempt.AssessmentId = aq.AssessmentId
                        WHERE aq.QuestionId = q.Id
                    )

                    -- Practice đã có kết quả làm bài.
                    OR EXISTS
                    (
                        SELECT 1
                        FROM dbo.PracticeQuestions AS pq
                        INNER JOIN dbo.PracticeSubmissions AS submission
                            ON submission.PracticeId = pq.PracticeId
                        WHERE pq.QuestionId = q.Id
                    )
                );

                -- Stimulus dùng chung cũng bị khóa nếu có ít nhất
                -- một Question tham chiếu đã được ghi nhận khóa.
                UPDATE s
                SET s.ContentLockedAtUtc = @lockedAtUtc
                FROM dbo.Stimuli AS s
                WHERE s.ContentLockedAtUtc IS NULL
                AND EXISTS
                (
                    SELECT 1
                    FROM dbo.Questions AS q
                    WHERE q.StimulusId = s.Id
                        AND q.ContentLockedAtUtc IS NOT NULL
                );
            ");
        }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentLockedAtUtc",
                table: "Stimuli");

            migrationBuilder.DropColumn(
                name: "ContentLockedAtUtc",
                table: "Questions");
        }
    }
}
