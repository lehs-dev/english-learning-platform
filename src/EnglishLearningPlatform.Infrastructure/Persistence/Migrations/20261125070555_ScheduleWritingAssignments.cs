using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishLearningPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduleWritingAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDraft",
                table: "WritingAssignments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishAtUtc",
                table: "WritingAssignments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WritingAssignments_Publication",
                table: "WritingAssignments",
                sql: "([IsDraft] = 0 OR [PublishAtUtc] IS NULL) AND ([PublishAtUtc] IS NULL OR [DueAtUtc] IS NULL OR [DueAtUtc] > [PublishAtUtc])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [WritingAssignments] WHERE [IsDraft] = 1 OR [PublishAtUtc] IS NOT NULL)
                THROW 51000, 'Estudy downgrade blocked because writing assignments require their publication state.', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_WritingAssignments_Publication",
                table: "WritingAssignments");

            migrationBuilder.DropColumn(
                name: "IsDraft",
                table: "WritingAssignments");

            migrationBuilder.DropColumn(
                name: "PublishAtUtc",
                table: "WritingAssignments");
        }
    }
}
