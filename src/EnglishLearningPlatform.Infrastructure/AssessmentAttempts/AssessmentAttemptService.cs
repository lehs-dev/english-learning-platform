using System.Data;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.AssessmentAttempts;

public sealed class AssessmentAttemptService(
    AppDbContext db,
    UserManager<ApplicationUser> users) : IAttemptService
{
    public async Task<AttemptStartResult> StartAsync(
        Guid studentId,
        Guid assessmentId,
        CancellationToken ct = default
    )
    {
        //Đọc scope trước để giữ cùng thứ tự khóa với Learning:
        //Course -> Assessment -> Attempt.
        var snapshot = await db.Assessments.AsNoTracking()
            .Where(a => a.Id == assessmentId)
            .Select(a => new { a.CourseId })
            .SingleOrDefaultAsync(ct);

        if (snapshot is null)
        {
            return new(AttemptStartStatus.NotFound);
        }

        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct
        );

        Course? course = null;

        if (snapshot.CourseId is Guid courseId)
        {
            course = await db.Courses.FromSqlInterpolated(
                $"SELECT * FROM Courses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {courseId}"
            ).SingleOrDefaultAsync(ct);
        }

        // Mọi Start cùng Assessment đi qua khóa này.
        var assessment = await db.Assessments.FromSqlInterpolated(
            $"SELECT * FROM Assessments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {assessmentId}"
        ).SingleOrDefaultAsync(ct);

        if (assessment is null)
        {
            return new(AttemptStartStatus.Unavailable);
        }

        //Quyền Student cũng phải được kiểm tra ở Server
        var student = await users.FindByIdAsync(studentId.ToString());

        if (student is null || student.AccountStatus != AccountStatus.Active)
        {
            return new(AttemptStartStatus.Forbidden);
        }

        var roles = await users.GetRolesAsync(student);

        if (roles.Count != 1 || roles[0] != AppRoles.Student)
        {
            return new(AttemptStartStatus.Forbidden);
        }

        if (assessment.Status != AssessmentStatus.Published)
        {
            return new(AttemptStartStatus.Unavailable);
        }

        if (assessment.CourseId.HasValue)
        {
            if (course is null || course.Status is not (CourseStatus.Published or CourseStatus.Unpublished) || course.OwnerTeacherUserId != assessment.OwnerTeacherUserId)
            {
                return new(AttemptStartStatus.Unavailable);
            }

            var enrolled = await db.Enrollments.Valid().AnyAsync(
                e => e.StudentUserId == studentId &&
                    e.CourseId == course.Id,
                    ct
            );

            if (!enrolled)
            {
                return new(AttemptStartStatus.Forbidden);
            }
        }

        var now = DateTimeOffset.UtcNow;

        var active = await db.Attempts.SingleOrDefaultAsync(
            a => a.StudentUserId == studentId &&
                 a.AssessmentId == assessmentId &&
                 a.Status == AttemptStatus.InProgress,
                 ct
        );

        if (active is not null)
        {
            if (active.DeadlineUtc <= now)
            {
                return new(AttemptStartStatus.NeedsFinalization, active.Id, active.DeadlineUtc);
            }
        }
        
        //Independent Assessment của Teacher Disabled
        // không nhận Attempt mới
        if (!assessment.CourseId.HasValue)
        {
            var ownerAvailable = await db.Users.AnyAsync(
                u => u.Id == assessment.OwnerTeacherUserId &&
                     u.AccountStatus != AccountStatus.Disabled,

                     ct
            );

            if (!ownerAvailable)
            {
                return new(AttemptStartStatus.Unavailable);
            }
        }

        var usedAttempts = await db.Attempts.CountAsync(
            a => a.StudentUserId == studentId &&
                 a.AssessmentId == assessmentId,
                 ct
        );

        if (usedAttempts >= assessment.MaxAttempts)
        {
            return new(AttemptStartStatus.LimitReached);
        }

        //Assessment Published phải có đề hợp lệ.
        var questionCount = await db.AssessmentQuestions.CountAsync(
            q => q.AssessmentId == assessmentId, ct
        );

        if (questionCount < 5 || assessment.DurationMinutes <= 0)
        {
            return new(AttemptStartStatus.Unavailable);
        }

        var attempt = new Attempt
        {
            StudentUserId = studentId,
            AssessmentId = assessmentId,
            Status = AttemptStatus.InProgress,
            StartedAtUtc = now,
            DeadlineUtc = now.AddMinutes(assessment.DurationMinutes)
        };

        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new(AttemptStartStatus.Started, attempt.Id, attempt.DeadlineUtc);

    }
}
