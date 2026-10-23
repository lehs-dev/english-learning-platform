using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Persistence;

/// <summary>
/// SQL Server update/key-range locks used inside the caller's transaction.
/// Queries acquire locks when enumerated; callers retain control of tracking and cardinality.
/// </summary>
public static class SqlServerLockingQueries
{
    public static IQueryable<Course> CoursesForUpdate(this AppDbContext db, Guid id, Guid? ownerTeacherId = null) =>
        ownerTeacherId.HasValue
            ? db.Courses.FromSqlInterpolated(
                $"SELECT * FROM Courses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id} AND OwnerTeacherUserId = {ownerTeacherId.Value}")
            : db.Courses.FromSqlInterpolated(
                $"SELECT * FROM Courses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}");

    public static IQueryable<Assessment> AssessmentsForUpdate(this AppDbContext db, Guid id, Guid? ownerTeacherId = null) =>
        ownerTeacherId.HasValue
            ? db.Assessments.FromSqlInterpolated(
                $"SELECT * FROM Assessments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id} AND OwnerTeacherUserId = {ownerTeacherId.Value}")
            : db.Assessments.FromSqlInterpolated(
                $"SELECT * FROM Assessments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}");

    public static IQueryable<Practice> PracticesForUpdate(this AppDbContext db, Guid id, Guid? ownerTeacherId = null) =>
        ownerTeacherId.HasValue
            ? db.Practices.FromSqlInterpolated(
                $"SELECT * FROM Practices WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id} AND OwnerTeacherUserId = {ownerTeacherId.Value}")
            : db.Practices.FromSqlInterpolated(
                $"SELECT * FROM Practices WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}");

    public static IQueryable<Attempt> AttemptsForUpdate(this AppDbContext db, Guid id) =>
        db.Attempts.FromSqlInterpolated($"SELECT * FROM Attempts WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}");

    public static IQueryable<Question> QuestionsForUpdate(this AppDbContext db, Guid id) =>
        db.Questions.FromSqlInterpolated($"SELECT * FROM Questions WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}");

    public static IQueryable<Stimulus> StimuliForUpdate(this AppDbContext db, Guid id) =>
        db.Stimuli.FromSqlInterpolated($"SELECT * FROM Stimuli WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}");

    public static IQueryable<Enrollment> EnrollmentsForUpdate(this AppDbContext db, Guid studentId, Guid courseId) =>
        db.Enrollments.FromSqlInterpolated(
            $"SELECT * FROM Enrollments WITH (UPDLOCK, HOLDLOCK) WHERE StudentUserId = {studentId} AND CourseId = {courseId}");

    public static IQueryable<Payment> PaymentsForUpdate(this AppDbContext db, string providerTransactionId) =>
        db.Payments.FromSqlInterpolated(
            $"SELECT * FROM Payment WITH (UPDLOCK, HOLDLOCK) WHERE ProviderTransactionId = {providerTransactionId}");

    public static Task AcquireCommerceLockAsync(this AppDbContext db, CancellationToken ct = default) =>
        db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = N'ELP.Commerce',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @result < 0 THROW 51000, 'Commerce lock unavailable.', 1;
            """, ct);

    // Serializes administrative writes so concurrent demotions cannot remove the last active admin.
    public static Task AcquireAdminManagementLockAsync(this AppDbContext db, CancellationToken ct = default) =>
        db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource=N'ELP.AdminManagement', @LockMode='Exclusive',
                @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51000, 'Cannot acquire administration lock.', 1;
            """, ct);
}
