using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishLearningPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEstudyM2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LearningPlanItems_DayIndex",
                table: "LearningPlanItems");

            migrationBuilder.AddColumn<string>(
                name: "StructuredDefinitionJson",
                table: "Questions",
                type: "nvarchar(max)",
                maxLength: 32768,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationDays",
                table: "LearningPlans",
                type: "int",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AlterColumn<string>(
                name: "SelectedAnswer",
                table: "PracticeAnswerHistories",
                type: "nvarchar(max)",
                maxLength: 4096,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SelectedOptionId",
                table: "AttemptAnswers",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<bool>(
                name: "IsCorrect",
                table: "AttemptAnswers",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StructuredAnswerJson",
                table: "AttemptAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TextAnswer",
                table: "AttemptAnswers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_LearningPlans_DurationDays",
                table: "LearningPlans",
                sql: "[DurationDays] IN (7, 14, 30)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LearningPlanItems_DayIndex",
                table: "LearningPlanItems",
                sql: "[DayIndex] BETWEEN 1 AND 30");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Questions] WHERE [StructuredDefinitionJson] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [AssessmentQuestions] a JOIN [Questions] q ON q.[Id] = a.[QuestionId]
                        WHERE q.[QuestionType] <> N'MultipleChoice')
                    OR EXISTS (SELECT 1 FROM [AttemptAnswers] WHERE [SelectedOptionId] IS NULL OR [TextAnswer] IS NOT NULL
                        OR [StructuredAnswerJson] IS NOT NULL OR [IsCorrect] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [LearningPlans] WHERE [DurationDays] <> 7)
                    OR EXISTS (SELECT 1 FROM [LearningPlanItems] WHERE [DayIndex] > 7)
                    OR EXISTS (SELECT 1 FROM [PracticeAnswerHistories] WHERE DATALENGTH([SelectedAnswer]) > 2000)
                THROW 51000, 'Estudy M2 downgrade blocked because learning records require the newer schema.', 1;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_LearningPlans_DurationDays",
                table: "LearningPlans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LearningPlanItems_DayIndex",
                table: "LearningPlanItems");

            migrationBuilder.DropColumn(
                name: "StructuredDefinitionJson",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "DurationDays",
                table: "LearningPlans");

            migrationBuilder.DropColumn(
                name: "IsCorrect",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "StructuredAnswerJson",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "TextAnswer",
                table: "AttemptAnswers");

            migrationBuilder.AlterColumn<Guid>(
                name: "SelectedOptionId",
                table: "AttemptAnswers",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SelectedAnswer",
                table: "PracticeAnswerHistories",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldMaxLength: 4096,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_LearningPlanItems_DayIndex",
                table: "LearningPlanItems",
                sql: "[DayIndex] BETWEEN 1 AND 7");
        }
    }
}
