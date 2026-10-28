using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishLearningPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPracticeVersionsAndSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PracticeQuestions_PracticeId_OrderIndex",
                table: "PracticeQuestions");

            migrationBuilder.DropIndex(
                name: "IX_PracticeQuestions_PracticeId_QuestionId",
                table: "PracticeQuestions");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "PracticeSubmissions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Practices",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "PracticeQuestions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "PracticeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PracticeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeSessions", x => x.Id);
                    table.CheckConstraint("CK_PracticeSessions_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_PracticeSessions_AspNetUsers_StudentUserId",
                        column: x => x.StudentUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PracticeSessions_Practices_PracticeId",
                        column: x => x.PracticeId,
                        principalTable: "Practices",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PracticeQuestions_PracticeId_Version_OrderIndex",
                table: "PracticeQuestions",
                columns: new[] { "PracticeId", "Version", "OrderIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PracticeQuestions_PracticeId_Version_QuestionId",
                table: "PracticeQuestions",
                columns: new[] { "PracticeId", "Version", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessions_PracticeId",
                table: "PracticeSessions",
                column: "PracticeId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessions_StudentUserId_PracticeId",
                table: "PracticeSessions",
                columns: new[] { "StudentUserId", "PracticeId" },
                unique: true,
                filter: "[SubmittedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PracticeSessions");

            migrationBuilder.DropIndex(
                name: "IX_PracticeQuestions_PracticeId_Version_OrderIndex",
                table: "PracticeQuestions");

            migrationBuilder.DropIndex(
                name: "IX_PracticeQuestions_PracticeId_Version_QuestionId",
                table: "PracticeQuestions");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "PracticeSubmissions");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Practices");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "PracticeQuestions");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeQuestions_PracticeId_OrderIndex",
                table: "PracticeQuestions",
                columns: new[] { "PracticeId", "OrderIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PracticeQuestions_PracticeId_QuestionId",
                table: "PracticeQuestions",
                columns: new[] { "PracticeId", "QuestionId" },
                unique: true);
        }
    }
}
