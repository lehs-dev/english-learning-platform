using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace EnglishLearningPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedIdentityRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { new Guid("b1edc10a-02c9-4b16-8f6b-000000000001"), "f93b8ecb-9c3c-4bbd-9d19-000000000001", "Student", "STUDENT" },
                    { new Guid("b1edc10a-02c9-4b16-8f6b-000000000002"), "f93b8ecb-9c3c-4bbd-9d19-000000000002", "Teacher", "TEACHER" },
                    { new Guid("b1edc10a-02c9-4b16-8f6b-000000000003"), "f93b8ecb-9c3c-4bbd-9d19-000000000003", "Admin", "ADMIN" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("b1edc10a-02c9-4b16-8f6b-000000000001"));

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("b1edc10a-02c9-4b16-8f6b-000000000002"));

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("b1edc10a-02c9-4b16-8f6b-000000000003"));
        }
    }
}
