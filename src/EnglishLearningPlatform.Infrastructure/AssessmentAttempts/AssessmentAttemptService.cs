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
    UserManager<ApplicationUser> users,
    IExamScoringService scoring) : IAttemptService
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

            return new(AttemptStartStatus.Resumed, active.Id, active.DeadlineUtc);
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


    public async Task<AttemptFinalizeResult> FinalizeAsync(
        Guid studentId,
        Guid attemptId,
        CancellationToken ct = default)
    {
        var snapshot = await db.Attempts.AsNoTracking()
            .Where(a => a.Id == attemptId)
            .Select(a => new
            {
                a.StudentUserId,
                a.AssessmentId,
                a.Assessment.CourseId
            })
            .SingleOrDefaultAsync(ct);

        if (snapshot is null)
            return new(AttemptFinalizeStatus.NotFound);

        // Không để Student xem hoặc chấm Attempt của người khác.
        if (snapshot.StudentUserId != studentId)
            return new(AttemptFinalizeStatus.NotFound);

        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);

        // Giữ thứ tự khóa giống StartAsync:
        // Course -> Assessment -> Attempt.
        if (snapshot.CourseId is Guid courseId)
        {
            await db.Courses.FromSqlInterpolated(
                $"SELECT * FROM Courses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {courseId}")
                .SingleOrDefaultAsync(ct);
        }

        var assessment = await db.Assessments.FromSqlInterpolated(
            $"SELECT * FROM Assessments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {snapshot.AssessmentId}")
            .SingleOrDefaultAsync(ct);

        if (assessment is null ||
            assessment.CourseId != snapshot.CourseId)
            return new(AttemptFinalizeStatus.Unavailable);

        var attempt = await db.Attempts.FromSqlInterpolated(
            $"SELECT * FROM Attempts WITH (UPDLOCK, HOLDLOCK) WHERE Id = {attemptId}")
            .SingleOrDefaultAsync(ct);

        if (attempt is null ||
            attempt.StudentUserId != studentId ||
            attempt.AssessmentId != assessment.Id)
            return new(AttemptFinalizeStatus.NotFound);

        // Request thứ hai đến sau request đầu:
        // đọc kết quả đã lưu, không chấm lại.
        if (attempt.Status == AttemptStatus.Graded)
        {
            return new(
                AttemptFinalizeStatus.AlreadyGraded,
                attempt.Id,
                attempt.OverallScore,
                attempt.Passed,
                attempt.FinalizationReason);
        }

        if (attempt.Status is not (
            AttemptStatus.InProgress or
            AttemptStatus.Submitted or
            AttemptStatus.AutoSubmitted))
            return new(AttemptFinalizeStatus.Unavailable);

        var now = DateTimeOffset.UtcNow;

        var reason = attempt.FinalizationReason ??
            (now >= attempt.DeadlineUtc
                ? AttemptFinalizationReason.DeadlineElapsed
                : AttemptFinalizationReason.ManualSubmit);

        // Lấy câu hỏi và đáp án chuẩn từ database,
        // tuyệt đối không nhận đáp án đúng/điểm từ client.
        var assessmentForScoring = await db.Assessments
            .AsNoTracking()
            .Include(a => a.Questions)
            .ThenInclude(aq => aq.Question)
            .ThenInclude(q => q.Options)
            .AsSplitQuery()
            .SingleAsync(a => a.Id == assessment.Id, ct);

        var responses = await db.AttemptAnswers
            .AsNoTracking()
            .Where(a => a.AttemptId == attemptId)
            .Select(a => new QuestionResponse(
                a.AssessmentQuestion.QuestionId,
                (Guid?)a.SelectedOptionId))
            .ToListAsync(ct);

        if (assessmentForScoring.Questions.Count == 0)
            return new(AttemptFinalizeStatus.Unavailable);

        var result = scoring.Score(assessmentForScoring, responses);

        if (result.MaxScore <= 0m)
            return new(AttemptFinalizeStatus.Unavailable);

        attempt.OverallScore = result.Percentage;

        // So sánh ngưỡng bằng điểm gốc,
        // không dùng Percentage đã làm tròn.
        attempt.Passed = assessment.PassingScore.HasValue
            ? result.Score * 100m >=
              assessment.PassingScore.Value * result.MaxScore
            : null;

        attempt.Status = AttemptStatus.Graded;
        attempt.FinalizationReason = reason;
        attempt.FinalizedAtUtc = now;
        attempt.UpdatedAtUtc = now;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new(
            AttemptFinalizeStatus.Graded,
            attempt.Id,
            attempt.OverallScore,
            attempt.Passed,
            reason);
    }


    public async Task<AttemptSaveStatus> SaveAnswerAsync(
        Guid studentId,
        Guid attemptId,
        Guid assessmentQuestionId,
        Guid? selectedOptionId,
        CancellationToken ct = default)
    {
        var snapshot = await db.Attempts.AsNoTracking()
            .Where(a => a.Id == attemptId)
            .Select(a => new
            {
                a.StudentUserId,
                a.AssessmentId,
                a.Assessment.CourseId
            })
            .SingleOrDefaultAsync(ct);

        if (snapshot is null || snapshot.StudentUserId != studentId)
            return AttemptSaveStatus.NotFound;

        var student = await users.FindByIdAsync(studentId.ToString());

        if (student is null ||
            student.AccountStatus != AccountStatus.Active)
            return AttemptSaveStatus.Forbidden;

        var roles = await users.GetRolesAsync(student);

        if (roles.Count != 1 || roles[0] != AppRoles.Student)
            return AttemptSaveStatus.Forbidden;

        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);

        // Cùng thứ tự khóa với Start và Finalize.
        if (snapshot.CourseId is Guid courseId)
        {
            await db.Courses.FromSqlInterpolated(
                $"SELECT * FROM Courses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {courseId}")
                .SingleOrDefaultAsync(ct);
        }

        var assessment = await db.Assessments.FromSqlInterpolated(
            $"SELECT * FROM Assessments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {snapshot.AssessmentId}")
            .SingleOrDefaultAsync(ct);

        if (assessment is null ||
            assessment.CourseId != snapshot.CourseId)
            return AttemptSaveStatus.Unavailable;

        var attempt = await db.Attempts.FromSqlInterpolated(
            $"SELECT * FROM Attempts WITH (UPDLOCK, HOLDLOCK) WHERE Id = {attemptId}")
            .SingleOrDefaultAsync(ct);

        if (attempt is null ||
            attempt.StudentUserId != studentId ||
            attempt.AssessmentId != assessment.Id)
            return AttemptSaveStatus.NotFound;

        if (attempt.Status != AttemptStatus.InProgress)
            return AttemptSaveStatus.AlreadyFinalized;

        var now = DateTimeOffset.UtcNow;

        if (now >= attempt.DeadlineUtc)
            return AttemptSaveStatus.Expired;

        // Kiểm tra câu hỏi thuộc chính Assessment đang làm.
        var questionId = await db.AssessmentQuestions
            .AsNoTracking()
            .Where(q =>
                q.Id == assessmentQuestionId &&
                q.AssessmentId == assessment.Id)
            .Select(q => (Guid?)q.QuestionId)
            .SingleOrDefaultAsync(ct);

        if (!questionId.HasValue)
            return AttemptSaveStatus.InvalidAnswer;

        // Không được chọn option thuộc câu hỏi khác.
        if (selectedOptionId.HasValue)
        {
            var validOption = await db.QuestionOptions
                .AsNoTracking()
                .AnyAsync(o =>
                    o.Id == selectedOptionId.Value &&
                    o.QuestionId == questionId.Value,
                    ct);

            if (!validOption)
                return AttemptSaveStatus.InvalidAnswer;
        }

        var answer = await db.AttemptAnswers
            .SingleOrDefaultAsync(a =>
                a.AttemptId == attemptId &&
                a.AssessmentQuestionId == assessmentQuestionId,
                ct);

        if (!selectedOptionId.HasValue)
        {
            if (answer is not null)
            {
                db.AttemptAnswers.Remove(answer);
                await db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
            return AttemptSaveStatus.Cleared;
        }

        if (answer is null)
        {
            db.AttemptAnswers.Add(new AttemptAnswer
            {
                AttemptId = attemptId,
                AssessmentQuestionId = assessmentQuestionId,
                SelectedOptionId = selectedOptionId.Value,
                SavedAtUtc = now
            });
        }
        else
        {
            answer.SelectedOptionId = selectedOptionId.Value;
            answer.SavedAtUtc = now;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return AttemptSaveStatus.Saved;
    }

}
