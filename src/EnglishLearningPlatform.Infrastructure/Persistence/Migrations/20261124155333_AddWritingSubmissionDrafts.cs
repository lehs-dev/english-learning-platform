using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishLearningPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWritingSubmissionDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WritingSubmissionDrafts",
                columns: table => new
                {
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Response = table.Column<string>(type: "nvarchar(max)", maxLength: 6000, nullable: false),
                    SavedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingSubmissionDrafts", x => new { x.AssignmentId, x.StudentUserId });
                    table.ForeignKey(
                        name: "FK_WritingSubmissionDrafts_AspNetUsers_StudentUserId",
                        column: x => x.StudentUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WritingSubmissionDrafts_WritingAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "WritingAssignments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WritingSubmissionDrafts_StudentUserId",
                table: "WritingSubmissionDrafts",
                column: "StudentUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [WritingSubmissionDrafts])
                THROW 51000, 'Estudy downgrade blocked because there are saved writing drafts.', 1;
                """);

            migrationBuilder.DropTable(
                name: "WritingSubmissionDrafts");
        }
    }
}
