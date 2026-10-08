using System.Net;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class AuthorizationIntegrityAcceptanceTests(IntegrationTestFactory factory)
    : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(AppRoles.Student, HttpStatusCode.Forbidden)]
    [InlineData(AppRoles.Teacher, HttpStatusCode.OK)]
    [InlineData(AppRoles.Admin, HttpStatusCode.Forbidden)]
    public async Task ApiTeacherPolicy_Returns401ForGuest_403ForWrongRole_200ForTeacher(
        string? role, HttpStatusCode expected)
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        if (role is not null)
        {
            var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
            using var login = await IdentityTestHelpers.LoginAsync(client, user);
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        }

        using var response = await client.GetAsync("/api/probe/policy/teacher");

        Assert.Equal(expected, response.StatusCode);
        // Identity includes a Location hint with API 401/403; the status does not redirect.
        if (expected == HttpStatusCode.OK)
            Assert.Null(response.Headers.Location);
        else
        {
            Assert.NotNull(response.Headers.Location);
            Assert.Equal(expected == HttpStatusCode.Unauthorized ? "/Account/Login" : "/Account/AccessDenied",
                response.Headers.Location.AbsolutePath);
        }
    }

    [Theory]
    [InlineData(AccountStatus.Locked)]
    [InlineData(AccountStatus.Disabled)]
    public async Task InactiveAccount_RejectsExistingCookieImmediately_ForApiAndMvc(AccountStatus status)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var apiClient = IdentityTestHelpers.CreateClient(factory);
        using var mvcClient = IdentityTestHelpers.CreateClient(factory);
        using var apiLogin = await IdentityTestHelpers.LoginAsync(apiClient, user);
        using var mvcLogin = await IdentityTestHelpers.LoginAsync(mvcClient, user);
        Assert.Equal(HttpStatusCode.Redirect, apiLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, mvcLogin.StatusCode);
        using var before = await apiClient.GetAsync("/api/probe/policy/teacher");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var persisted = await manager.FindByIdAsync(user.Id.ToString());
            Assert.NotNull(persisted);
            persisted.AccountStatus = status;
            Assert.True((await manager.UpdateAsync(persisted)).Succeeded);
        }

        using var apiResponse = await apiClient.GetAsync("/api/probe/policy/teacher");
        Assert.Equal(HttpStatusCode.Unauthorized, apiResponse.StatusCode);
        Assert.NotNull(apiResponse.Headers.Location);
        Assert.Equal("/Account/Login", apiResponse.Headers.Location.AbsolutePath);

        using var mvcResponse = await mvcClient.GetAsync("/TeacherCourses");
        Assert.Equal(HttpStatusCode.Redirect, mvcResponse.StatusCode);
        Assert.Contains("/Account/Login", mvcResponse.Headers.Location!.ToString());
    }

    [Fact]
    public async Task MigratedDatabase_MatchesEfModel_AndCommerceIntegrityConstraints()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());

        (Type Entity, string Table, string Name, bool Unique, string[] Columns)[] indexes =
        [
            (typeof(Enrollment), "Enrollments", "IX_Enrollments_StudentUserId_CourseId", true,
                [nameof(Enrollment.StudentUserId), nameof(Enrollment.CourseId)]),
            (typeof(Payment), "Payment", "IX_Payment_ProviderTransactionId", true,
                [nameof(Payment.ProviderTransactionId)]),
            (typeof(LessonProgress), "LessonProgress", "IX_LessonProgress_EnrollmentId_LessonId", true,
                [nameof(LessonProgress.EnrollmentId), nameof(LessonProgress.LessonId)]),
            (typeof(Order), "Order", "IX_Order_StudentUserId_CourseId", false,
                [nameof(Order.StudentUserId), nameof(Order.CourseId)]),
            (typeof(IdentityUserRole<Guid>), "AspNetUserRoles", "IX_AspNetUserRoles_UserId", true,
                [nameof(IdentityUserRole<Guid>.UserId)])
        ];

        await db.Database.OpenConnectionAsync();
        try
        {
            var actualIndexes = new Dictionary<(string Table, string Name), (bool Unique, bool Disabled, List<string> Columns)>();
            using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = """
                    SELECT t.name, i.name, i.is_unique, i.is_disabled, c.name
                    FROM sys.indexes i
                    JOIN sys.tables t ON t.object_id = i.object_id
                    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                    JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                    WHERE t.name IN (N'Enrollments', N'Payment', N'LessonProgress', N'Order', N'AspNetUserRoles')
                      AND ic.key_ordinal > 0
                    ORDER BY t.name, i.name, ic.key_ordinal
                    """;
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var key = (reader.GetString(0), reader.GetString(1));
                    if (!actualIndexes.TryGetValue(key, out var index))
                        actualIndexes[key] = index = (reader.GetBoolean(2), reader.GetBoolean(3), []);
                    index.Columns.Add(reader.GetString(4));
                }
            }

            foreach (var expected in indexes)
            {
                var entity = db.Model.FindEntityType(expected.Entity)!;
                var modelIndex = entity.GetIndexes().Single(i => i.Properties.Select(p => p.Name).SequenceEqual(expected.Columns));
                Assert.Equal(expected.Table, entity.GetTableName());
                Assert.Equal(expected.Name, modelIndex.GetDatabaseName());
                Assert.Equal(expected.Unique, modelIndex.IsUnique);
                Assert.True(actualIndexes.TryGetValue((expected.Table, expected.Name), out var actual),
                    $"Missing database index {expected.Name}.");
                Assert.Equal(expected.Unique, actual.Unique);
                Assert.False(actual.Disabled);
                Assert.Equal(expected.Columns, actual.Columns);
            }

            (Type Entity, string Property, string Name, string Table, string PrincipalTable)[] foreignKeys =
            [
                (typeof(Enrollment), nameof(Enrollment.StudentUserId), "FK_Enrollments_AspNetUsers_StudentUserId", "Enrollments", "AspNetUsers"),
                (typeof(Enrollment), nameof(Enrollment.CourseId), "FK_Enrollments_Courses_CourseId", "Enrollments", "Courses"),
                (typeof(Enrollment), nameof(Enrollment.PaymentId), "FK_Enrollments_Payment_PaymentId", "Enrollments", "Payment"),
                (typeof(Order), nameof(Order.StudentUserId), "FK_Order_AspNetUsers_StudentUserId", "Order", "AspNetUsers"),
                (typeof(Order), nameof(Order.CourseId), "FK_Order_Courses_CourseId", "Order", "Courses"),
                (typeof(Payment), nameof(Payment.OrderId), "FK_Payment_Order_OrderId", "Payment", "Order"),
                (typeof(LessonProgress), nameof(LessonProgress.EnrollmentId), "FK_LessonProgress_Enrollments_EnrollmentId", "LessonProgress", "Enrollments"),
                (typeof(LessonProgress), nameof(LessonProgress.LessonId), "FK_LessonProgress_Lessons_LessonId", "LessonProgress", "Lessons")
            ];
            var actualForeignKeys = new Dictionary<string, (string Table, string PrincipalTable, string Column, string PrincipalColumn, byte DeleteAction, bool Disabled, bool Untrusted)>();
            using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = """
                    SELECT fk.name, t.name, pt.name, c.name, pc.name,
                           fk.delete_referential_action, fk.is_disabled, fk.is_not_trusted
                    FROM sys.foreign_keys fk
                    JOIN sys.tables t ON t.object_id = fk.parent_object_id
                    JOIN sys.tables pt ON pt.object_id = fk.referenced_object_id
                    JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                    JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
                    JOIN sys.columns pc ON pc.object_id = fkc.referenced_object_id AND pc.column_id = fkc.referenced_column_id
                    WHERE t.name IN (N'Enrollments', N'Payment', N'LessonProgress', N'Order')
                    """;
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    actualForeignKeys.Add(reader.GetString(0), (reader.GetString(1), reader.GetString(2),
                        reader.GetString(3), reader.GetString(4), reader.GetByte(5), reader.GetBoolean(6), reader.GetBoolean(7)));
            }

            foreach (var expected in foreignKeys)
            {
                var modelForeignKey = db.Model.FindEntityType(expected.Entity)!.GetForeignKeys()
                    .Single(fk => fk.Properties.Count == 1 && fk.Properties[0].Name == expected.Property);
                Assert.Equal(DeleteBehavior.NoAction, modelForeignKey.DeleteBehavior);
                Assert.Equal(expected.Name, modelForeignKey.GetConstraintName());
                Assert.True(actualForeignKeys.TryGetValue(expected.Name, out var actual),
                    $"Missing database foreign key {expected.Name}.");
                Assert.Equal(expected.Table, actual.Table);
                Assert.Equal(expected.PrincipalTable, actual.PrincipalTable);
                Assert.Equal(expected.Property, actual.Column);
                Assert.Equal("Id", actual.PrincipalColumn);
                Assert.Equal((byte)0, actual.DeleteAction);
                Assert.False(actual.Disabled);
                Assert.False(actual.Untrusted);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task DuplicateEnrollment_IsRejectedByDatabase_AndOriginalEnrollmentPersists()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Enrollment integrity", Status = CourseStatus.Published };
        var original = new Enrollment { StudentUserId = student.Id, Course = course };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Enrollments.Add(original);
            await db.SaveChangesAsync();
            db.Enrollments.Add(new Enrollment { StudentUserId = student.Id, CourseId = course.Id });

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var sqlException = Assert.IsType<SqlException>(exception.InnerException);
            Assert.True(sqlException.Number is 2601 or 2627);
        }

        using var verification = factory.Services.CreateScope();
        var persisted = await verification.ServiceProvider.GetRequiredService<AppDbContext>().Enrollments.AsNoTracking()
            .Where(e => e.StudentUserId == student.Id && e.CourseId == course.Id).ToListAsync();
        Assert.Equal(original.Id, Assert.Single(persisted).Id);
    }
}
