using System.Data.Common;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Persistence;

public sealed class SqlServerLockingQueriesTests
{
    [Theory]
    [InlineData("Courses")]
    [InlineData("Assessments")]
    [InlineData("Practices")]
    [InlineData("Attempts")]
    [InlineData("Questions")]
    [InlineData("Stimuli")]
    public void EntityLockQuery_PreservesLockHintsAndParameterizesId(string table)
    {
        using var db = CreateContext();
        var id = Guid.NewGuid();
        IQueryable query = table switch
        {
            "Courses" => db.CoursesForUpdate(id),
            "Assessments" => db.AssessmentsForUpdate(id),
            "Practices" => db.PracticesForUpdate(id),
            "Attempts" => db.AttemptsForUpdate(id),
            "Questions" => db.QuestionsForUpdate(id),
            "Stimuli" => db.StimuliForUpdate(id),
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };

        using var command = query.CreateDbCommand();

        Assert.Contains($"SELECT * FROM {table} WITH (UPDLOCK, HOLDLOCK) WHERE Id = @p0", command.CommandText);
        Assert.DoesNotContain("OwnerTeacherUserId =", command.CommandText);
        Assert.Equal(id, Assert.Single(command.Parameters.Cast<DbParameter>()).Value);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("Courses")]
    [InlineData("Assessments")]
    [InlineData("Practices")]
    public void OwnedEntityLockQuery_AppliesOwnerInsideLockedSql(string table)
    {
        using var db = CreateContext();
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        IQueryable query = table switch
        {
            "Courses" => db.CoursesForUpdate(id, owner),
            "Assessments" => db.AssessmentsForUpdate(id, owner),
            "Practices" => db.PracticesForUpdate(id, owner),
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };

        using var command = query.CreateDbCommand();

        Assert.Contains($"SELECT * FROM {table} WITH (UPDLOCK, HOLDLOCK) WHERE Id = @p0 AND OwnerTeacherUserId = @p1",
            command.CommandText);
        Assert.Equal(new object[] { id, owner }, command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value));
    }

    [Fact]
    public void EnrollmentLockQuery_LocksTheStudentAndCourseKeyRange()
    {
        using var db = CreateContext();
        var studentId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        using var command = db.EnrollmentsForUpdate(studentId, courseId).CreateDbCommand();

        Assert.Contains("SELECT * FROM Enrollments WITH (UPDLOCK, HOLDLOCK) WHERE StudentUserId = @p0 AND CourseId = @p1",
            command.CommandText);
        Assert.Equal(new object[] { studentId, courseId }, command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value));
    }

    [Fact]
    public void PaymentLockQuery_KeepsProviderTransactionIdInAParameter()
    {
        using var db = CreateContext();
        const string transactionId = "provider-id'; DROP TABLE Payment;--";

        using var command = db.PaymentsForUpdate(transactionId).CreateDbCommand();

        Assert.Contains("SELECT * FROM Payment WITH (UPDLOCK, HOLDLOCK) WHERE ProviderTransactionId = @p0", command.CommandText);
        Assert.DoesNotContain(transactionId, command.CommandText);
        Assert.Equal(transactionId, Assert.Single(command.Parameters.Cast<DbParameter>()).Value);
    }

    [Fact]
    public void LockedQuery_RemainsComposableWithCallerTrackingAndIncludes()
    {
        using var db = CreateContext();
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();

        using var command = db.AssessmentsForUpdate(id, owner).AsNoTracking()
            .Include(assessment => assessment.Questions).AsSingleQuery().CreateDbCommand();

        Assert.Contains("FROM Assessments WITH (UPDLOCK, HOLDLOCK) WHERE Id = @p0 AND OwnerTeacherUserId = @p1",
            command.CommandText);
        Assert.Contains("LEFT JOIN", command.CommandText);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        // Generating SQL commands only uses provider metadata; no connection is opened.
        .UseSqlServer("Server=localhost;Database=LockingQueryUnitTests;Trusted_Connection=True;").Options);
}
