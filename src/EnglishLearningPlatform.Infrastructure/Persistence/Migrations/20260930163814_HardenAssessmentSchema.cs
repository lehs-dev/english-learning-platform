using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishLearningPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenAssessmentSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Questions_OwnerTeacherUserId",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Practices_OwnerTeacherUserId",
                table: "Practices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Assessments_MaxAttempts",
                table: "Assessments");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_OwnerTeacherUserId_Status_PrimarySkill",
                table: "Questions",
                columns: new[] { "OwnerTeacherUserId", "Status", "PrimarySkill" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Questions_PrimarySkill",
                table: "Questions",
                sql: "[PrimarySkill] IN (N'Reading', N'Listening', N'Vocabulary', N'Grammar')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Questions_Status",
                table: "Questions",
                sql: "[Status] IN (N'Draft', N'Published', N'Archived')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_QuestionOptions_OrderIndex",
                table: "QuestionOptions",
                sql: "[OrderIndex] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_Practices_OwnerTeacherUserId_Status",
                table: "Practices",
                columns: new[] { "OwnerTeacherUserId", "Status" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Practices_Status",
                table: "Practices",
                sql: "[Status] IN (N'Draft', N'Published', N'Unpublished')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PracticeQuestions_OrderIndex",
                table: "PracticeQuestions",
                sql: "[OrderIndex] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttemptSkillResults_Skill",
                table: "AttemptSkillResults",
                sql: "[Skill] IN (N'Reading', N'Listening', N'Vocabulary', N'Grammar')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Attempts_FinalizationReason",
                table: "Attempts",
                sql: "[FinalizationReason] IS NULL OR [FinalizationReason] IN (N'ManualSubmit', N'DeadlineElapsed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Attempts_Status",
                table: "Attempts",
                sql: "[Status] IN (N'InProgress', N'Submitted', N'AutoSubmitted', N'Graded')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Assessments_MaxAttempts",
                table: "Assessments",
                sql: "[MaxAttempts] >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Assessments_PlacementPassingScore",
                table: "Assessments",
                sql: "[AssessmentType] <> N'PlacementTest' OR [PassingScore] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Assessments_SkillAssessmentTarget",
                table: "Assessments",
                sql: "[AssessmentType] <> N'SkillAssessment' OR [TargetSkill] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Assessments_Status",
                table: "Assessments",
                sql: "[Status] IN (N'Draft', N'Published', N'Unpublished', N'Archived')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Assessments_TargetSkill",
                table: "Assessments",
                sql: "[TargetSkill] IS NULL OR [TargetSkill] IN (N'Reading', N'Listening', N'Vocabulary', N'Grammar')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Assessments_Type",
                table: "Assessments",
                sql: "[AssessmentType] IN (N'PlacementTest', N'SkillAssessment', N'PracticeExam')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AssessmentQuestions_OrderIndex",
                table: "AssessmentQuestions",
                sql: "[OrderIndex] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Questions_OwnerTeacherUserId_Status_PrimarySkill",
                table: "Questions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Questions_PrimarySkill",
                table: "Questions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Questions_Status",
                table: "Questions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_QuestionOptions_OrderIndex",
                table: "QuestionOptions");

            migrationBuilder.DropIndex(
                name: "IX_Practices_OwnerTeacherUserId_Status",
                table: "Practices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Practices_Status",
                table: "Practices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PracticeQuestions_OrderIndex",
                table: "PracticeQuestions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AttemptSkillResults_Skill",
                table: "AttemptSkillResults");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Attempts_FinalizationReason",
                table: "Attempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Attempts_Status",
                table: "Attempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Assessments_MaxAttempts",
                table: "Assessments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Assessments_PlacementPassingScore",
                table: "Assessments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Assessments_SkillAssessmentTarget",
                table: "Assessments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Assessments_Status",
                table: "Assessments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Assessments_TargetSkill",
                table: "Assessments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Assessments_Type",
                table: "Assessments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AssessmentQuestions_OrderIndex",
                table: "AssessmentQuestions");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_OwnerTeacherUserId",
                table: "Questions",
                column: "OwnerTeacherUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Practices_OwnerTeacherUserId",
                table: "Practices",
                column: "OwnerTeacherUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Assessments_MaxAttempts",
                table: "Assessments",
                sql: "[MaxAttempts] > 0");
        }
    }
}
