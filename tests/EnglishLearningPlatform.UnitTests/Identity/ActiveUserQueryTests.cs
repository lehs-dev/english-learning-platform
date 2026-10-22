using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Identity;

public sealed class ActiveUserQueryTests
{
    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Teacher)]
    [InlineData(AppRoles.Admin)]
    public void ExactlyOneKnownRoleOnAnActiveAccount_IsAvailable(string name)
    {
        var (user, roles, assignments) = Account(AccountStatus.Active, name);

        var result = Assert.Single(ActiveUserQueries.SelectActiveRoles(new[] { user }.AsQueryable(),
            assignments.AsQueryable(), roles.AsQueryable()));

        Assert.Equal(user.Id, result.UserId);
        Assert.Equal(name, result.Role);
    }

    [Theory]
    [InlineData(AccountStatus.Locked, AppRoles.Student)]
    [InlineData(AccountStatus.Disabled, AppRoles.Teacher)]
    [InlineData(AccountStatus.Active, "Guest")]
    [InlineData(AccountStatus.Active, "teacher")]
    [InlineData(AccountStatus.Active)]
    [InlineData(AccountStatus.Active, AppRoles.Student, AppRoles.Teacher)]
    [InlineData(AccountStatus.Active, AppRoles.Teacher, AppRoles.Teacher)]
    [InlineData(AccountStatus.Active, AppRoles.Admin, "Guest")]
    public void InactiveUnknownMissingOrMultipleRoles_AreUnavailable(AccountStatus status, params string[] roleNames)
    {
        var (user, roles, assignments) = Account(status, roleNames);

        Assert.Empty(ActiveUserQueries.SelectActiveRoles(new[] { user }.AsQueryable(),
            assignments.AsQueryable(), roles.AsQueryable()));
    }

    [Fact]
    public void AnotherUsersRoles_DoNotChangeTheAccountRoleCount()
    {
        var (student, studentRoles, studentAssignments) = Account(AccountStatus.Active, AppRoles.Student);
        var (teacher, teacherRoles, teacherAssignments) = Account(AccountStatus.Active, AppRoles.Teacher, AppRoles.Admin);

        var result = ActiveUserQueries.SelectActiveRoles(new[] { student, teacher }.AsQueryable(),
            studentAssignments.Concat(teacherAssignments).AsQueryable(), studentRoles.Concat(teacherRoles).AsQueryable());

        Assert.Equal(student.Id, Assert.Single(result).UserId);
    }

    [Fact]
    public void DanglingRoleAssignment_DoesNotGrantAccess()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        var assignments = new[] { new IdentityUserRole<Guid> { UserId = user.Id, RoleId = Guid.NewGuid() } };

        Assert.Empty(ActiveUserQueries.SelectActiveRoles(new[] { user }.AsQueryable(),
            assignments.AsQueryable(), Array.Empty<IdentityRole<Guid>>().AsQueryable()));
    }

    [Fact]
    public void AccountRoleQueriesTranslateToParameterizedSqlWithoutAConnection()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=RoleQueryTests;Trusted_Connection=True;TrustServerCertificate=True").Options);
        var userId = Guid.NewGuid();
        var roleName = AppRoles.Teacher;
        var query = db.ActiveRoleAssignments().Where(user => user.UserId == userId && user.Role == roleName);

        using var command = query.CreateDbCommand();

        Assert.Contains("COUNT", command.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(command.Parameters.Cast<System.Data.Common.DbParameter>(), parameter => Equals(parameter.Value, userId));
        Assert.Contains(command.Parameters.Cast<System.Data.Common.DbParameter>(), parameter => Equals(parameter.Value, roleName));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static (ApplicationUser User, IdentityRole<Guid>[] Roles, IdentityUserRole<Guid>[] Assignments)
        Account(AccountStatus status, params string[] roleNames)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), AccountStatus = status };
        var roles = roleNames.Select(name => new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = name }).ToArray();
        return (user, roles, roles.Select(role => new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id }).ToArray());
    }
}
