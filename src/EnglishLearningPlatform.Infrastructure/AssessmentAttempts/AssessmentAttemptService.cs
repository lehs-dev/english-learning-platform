using System.Data;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Gamification;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.AssessmentAttempts;

public sealed class AssessmentAttemptService(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    IExamScoringService scoring,
    // Dùng chung quy tắc kiểm tra định nghĩa đề với Teacher Publish.
    IAssessmentDefinitionValidator definitionValidator,
    TimeProvider timeProvider,
    IActiveUserValidator? activeUsers = null) : IAttemptService
{
    private readonly IActiveUserValidator activeUserValidator = activeUsers ?? new ActiveUserValidator(users);

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
            course = await db.CoursesForUpdate(courseId).SingleOrDefaultAsync(ct);
        }

        // Mọi Start cùng Assessment đi qua khóa này.
        var assessment = await db.AssessmentsForUpdate(assessmentId).SingleOrDefaultAsync(ct);

        if (assessment is null || assessment.CourseId != snapshot.CourseId)
        {
            return new(AttemptStartStatus.Unavailable);
        }

        //Quyền Student cũng phải được kiểm tra ở Server
        if (!await activeUserValidator.IsActiveStudentAsync(studentId, ct))
        {
            return new(AttemptStartStatus.Forbidden);
        }

        if (assessment.CourseId.HasValue)
        {
            if (course is null || course.OwnerTeacherUserId != assessment.OwnerTeacherUserId)
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

        // Đồng hồ server được dùng chung với query; test có thể cố định đúng tick deadline.
        var now = timeProvider.GetUtcNow();

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
                var finalized = await FinalizeLockedAsync(
                    active,
                    assessment,
                    now,
                    ct);

                if (finalized.Status != AttemptFinalizeStatus.Graded)
                    return new(AttemptStartStatus.Unavailable);

                await tx.CommitAsync(ct);

                // Trả kết quả lượt cũ để giao diện có thể dẫn
                // học viên đến trang Result.
                // Không tự tạo lượt làm mới trong request này.
                return new(
                    AttemptStartStatus.ExpiredFinalized,
                    active.Id,
                    active.DeadlineUtc);
            }

            return new(AttemptStartStatus.Resumed, active.Id, active.DeadlineUtc);
        }

        // Withdrawal blocks new attempts, while an existing attempt keeps
        // its original deadline and can still be resumed or finalized.
        if (assessment.Status != AssessmentStatus.Published ||
            (course is not null && course.Status is not (CourseStatus.Published or CourseStatus.Unpublished)))
        {
            return new(AttemptStartStatus.Unavailable);
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

        // Assessment Published phải có đề hợp lệ.
        // Chỉ tới đây khi không có Attempt đang làm cần Resume/finalize.
        // Không chuyển đoạn kiểm tra này lên trước phần xử lý active Attempt.

        // Đọc dữ liệu thật, gồm Options và Stimulus để kiểm tra toàn bộ đề.
        // AsNoTracking: các entity này chỉ dùng để kiểm tra, không sửa nội dung.
        var questions = await db.AssessmentQuestions
            .AsNoTracking()
            .Where(item => item.AssessmentId == assessmentId)
            .Include(item => item.Question)
                .ThenInclude(question => question.Options)
            .Include(item => item.Question)
                .ThenInclude(question => question.AcceptedAnswers)
            .Include(item => item.Question)
                .ThenInclude(question => question.Stimulus)
            .AsSplitQuery()
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .ToListAsync(ct);

        // Đọc các Course đang chọn đề này làm Final.
        // Cần kiểm tra cả tham chiếu Final, không chỉ CourseId của Assessment.
        var finalCourses = await db.Courses
            .AsNoTracking()
            .Where(item => item.FinalAssessmentId == assessmentId)
            .ToListAsync(ct);

        // Đây là đề đã Published, không phải lần Publish đầu tiên.
        // Câu Archived đã thuộc đề vẫn được kiểm tra theo ExistingUse.
        var definitionErrors = definitionValidator.Validate(
            assessment,
            questions,
            course,
            finalCourses,
            isFirstPublish: false);

        if (definitionErrors.Count > 0)
        {
            // Dừng trước khi tạo Attempt.
            // Không trả chi tiết lỗi nội dung hoặc đáp án cho Student.
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


    public Task<AttemptFinalizeResult> FinalizeAsync(
        Guid studentId,
        Guid attemptId,
        CancellationToken ct = default) =>
        FinalizeCoreAsync(studentId, attemptId, onlyExpired: false, ct);

    public Task<AttemptFinalizeResult> FinalizeExpiredAsync(
        Guid studentId,
        Guid attemptId,
        CancellationToken ct = default) =>
        FinalizeCoreAsync(studentId, attemptId, onlyExpired: true, ct);

    // Hai đường dùng chung quyền, thứ tự khóa và thao tác chấm; không có công thức chấm thứ hai.
    private async Task<AttemptFinalizeResult> FinalizeCoreAsync(
        Guid studentId, Guid attemptId, bool onlyExpired, CancellationToken ct)
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
            await db.CoursesForUpdate(courseId)
                .SingleOrDefaultAsync(ct);
        }

        var assessment = await db.AssessmentsForUpdate(snapshot.AssessmentId)
            .SingleOrDefaultAsync(ct);

        if (assessment is null ||
            assessment.CourseId != snapshot.CourseId)
            return new(AttemptFinalizeStatus.Unavailable);

        var attempt = await db.AttemptsForUpdate(attemptId)
            .SingleOrDefaultAsync(ct);

        if (attempt is null ||
            attempt.StudentUserId != studentId ||
            attempt.AssessmentId != assessment.Id)
            return new(AttemptFinalizeStatus.NotFound);

        if (!await activeUserValidator.IsActiveStudentAsync(studentId, ct))
            return new(AttemptFinalizeStatus.Forbidden);

        var now = timeProvider.GetUtcNow();
        if (onlyExpired)
        {
            // F5 kết quả đã chấm không sửa timestamp/điểm hoặc chạy lại completion.
            if (attempt.Status == AttemptStatus.Graded)
                return new(AttemptFinalizeStatus.AlreadyGraded, attempt.Id,
                    attempt.OverallScore, attempt.Passed, attempt.FinalizationReason);

            // Kiểm tra lại SAU khi lấy khóa: không tin deadline/status từ trang hoặc query cũ.
            if (attempt.Status != AttemptStatus.InProgress || now < attempt.DeadlineUtc)
                return new(AttemptFinalizeStatus.Unavailable);
        }

        var result = await FinalizeLockedAsync(
            attempt,
            assessment,
            now,
            ct);

        if (result.Status is AttemptFinalizeStatus.Graded or AttemptFinalizeStatus.AlreadyGraded)
        {
            await tx.CommitAsync(ct);
        }

        return result;
    }


    public Task<AttemptSaveStatus> SaveAnswerAsync(
        Guid studentId,
        Guid attemptId,
        Guid assessmentQuestionId,
        Guid? selectedOptionId,
        CancellationToken ct = default) =>
        SaveResponsesAsync(studentId, attemptId, [new(assessmentQuestionId, selectedOptionId)], ct);

    public async Task<AttemptSaveStatus> SaveResponsesAsync(
        Guid studentId,
        Guid attemptId,
        IReadOnlyList<AssessmentResponse> responses,
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

        if (!await activeUserValidator.IsActiveStudentAsync(studentId, ct))
            return AttemptSaveStatus.Forbidden;

        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);

        // Cùng thứ tự khóa với Start và Finalize.
        if (snapshot.CourseId is Guid courseId)
        {
            await db.CoursesForUpdate(courseId)
                .SingleOrDefaultAsync(ct);
        }

        var assessment = await db.AssessmentsForUpdate(snapshot.AssessmentId)
            .SingleOrDefaultAsync(ct);

        if (assessment is null ||
            assessment.CourseId != snapshot.CourseId)
            return AttemptSaveStatus.Unavailable;

        var attempt = await db.AttemptsForUpdate(attemptId)
            .SingleOrDefaultAsync(ct);

        if (attempt is null ||
            attempt.StudentUserId != studentId ||
            attempt.AssessmentId != assessment.Id)
            return AttemptSaveStatus.NotFound;

        if (attempt.Status != AttemptStatus.InProgress)
            return AttemptSaveStatus.AlreadyFinalized;

        // Kiểm lại trong transaction: quyền lúc mở trang không phải quyền lúc ghi.
        if (!await HasCurrentCourseAccessAsync(studentId, assessment, ct))
            return AttemptSaveStatus.Forbidden;

        var now = timeProvider.GetUtcNow();

        if (now >= attempt.DeadlineUtc)
        {
            // Không ghi nhận đáp án mới đã đến muộn.
            // Chỉ chấm các câu được lưu trước đó.
            var finalized = await FinalizeLockedAsync(
                attempt,
                assessment,
                now,
                ct);

            if (finalized.Status != AttemptFinalizeStatus.Graded)
                return AttemptSaveStatus.Unavailable;

            await tx.CommitAsync(ct);
            return AttemptSaveStatus.Expired;
        }

        var questionIds = responses.Select(response => response.AssessmentQuestionId).ToHashSet();
        if (questionIds.Count != responses.Count || questionIds.Contains(Guid.Empty))
            return AttemptSaveStatus.InvalidAnswer;

        var questions = await db.AssessmentQuestions
            .AsNoTracking()
            .Where(q => q.AssessmentId == assessment.Id && questionIds.Contains(q.Id))
            .Include(q => q.Question).ThenInclude(q => q.Options)
            .ToDictionaryAsync(q => q.Id, ct);

        if (questions.Count != questionIds.Count)
            return AttemptSaveStatus.InvalidAnswer;

        // Validate the complete batch before writing any rows. A bad answer must
        // not change previously saved responses from the same request.
        var validated = new List<AssessmentResponse>(responses.Count);
        foreach (var response in responses)
        {
            var value = ValidateResponse(questions[response.AssessmentQuestionId].Question, response);
            if (value is null)
                return AttemptSaveStatus.InvalidAnswer;
            validated.Add(value);
        }

        var savedAnswers = await db.AttemptAnswers
            .Where(answer => answer.AttemptId == attemptId && questionIds.Contains(answer.AssessmentQuestionId))
            .ToDictionaryAsync(answer => answer.AssessmentQuestionId, ct);

        bool saved = false;
        foreach (var response in validated)
        {
            savedAnswers.TryGetValue(response.AssessmentQuestionId, out var answer);
            if (!response.SelectedOptionId.HasValue && response.TextAnswer is null && response.StructuredAnswerJson is null)
            {
                if (answer is not null)
                    db.AttemptAnswers.Remove(answer);
                continue;
            }

            if (answer is null)
            {
                answer = new AttemptAnswer
                {
                    AttemptId = attemptId,
                    AssessmentQuestionId = response.AssessmentQuestionId
                };
                db.AttemptAnswers.Add(answer);
            }

            answer.SelectedOptionId = response.SelectedOptionId;
            answer.TextAnswer = response.TextAnswer;
            answer.StructuredAnswerJson = response.StructuredAnswerJson;
            answer.SavedAtUtc = now;
            saved = true;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return saved ? AttemptSaveStatus.Saved : AttemptSaveStatus.Cleared;
    }

    private static AssessmentResponse? ValidateResponse(Question question, AssessmentResponse response)
    {
        switch (question.QuestionType)
        {
            case QuestionType.MultipleChoice:
                return response.TextAnswer is null && response.StructuredAnswerJson is null &&
                    (!response.SelectedOptionId.HasValue || question.Options.Any(o => o.Id == response.SelectedOptionId))
                    ? response : null;
            case QuestionType.FillBlank:
            case QuestionType.Dictation:
                if (response.SelectedOptionId.HasValue || response.StructuredAnswerJson is not null ||
                    response.TextAnswer is { Length: > 500 })
                    return null;
                return string.IsNullOrWhiteSpace(response.TextAnswer) ? response with { TextAnswer = null } : response;
            case QuestionType.Matching:
            case QuestionType.Ordering:
                if (response.SelectedOptionId.HasValue || response.TextAnswer is not null)
                    return null;
                var evaluation = StructuredQuestionEvaluator.Evaluate(question, response.StructuredAnswerJson);
                return evaluation.IsValid ? response with { StructuredAnswerJson = evaluation.CanonicalJson } : null;
            default:
                return null;
        }
    }


    // Không xét Published/Archived ở đây: lượt đã Start được hoàn thành khi đề rút.
    // Chỉ xét scope, cùng owner Course và Enrollment/thanh toán hiện còn hợp lệ.
    private async Task<bool> HasCurrentCourseAccessAsync(Guid studentId, Assessment assessment, CancellationToken ct)
    {
        if (!assessment.CourseId.HasValue)
            return true; // Đề Independent không yêu cầu Enrollment.

        return await db.Enrollments.Valid().AnyAsync(e => e.StudentUserId == studentId &&
            e.CourseId == assessment.CourseId && e.Course.OwnerTeacherUserId == assessment.OwnerTeacherUserId, ct);
    }

    private async Task<AttemptFinalizeResult> FinalizeLockedAsync(
        Attempt attempt,
        Assessment assessment,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Caller đã mở transaction và lấy các khóa cần thiết.
        // Hàm này KHÔNG tự mở hoặc commit transaction.

        if (attempt.Status == AttemptStatus.Graded)
        {
            // Replays can repair completion missed before this integration,
            // while preserving the original grade and finalization timestamp.
            if (assessment.CourseId is Guid completedCourseId)
                await CourseCompletion.ReconcileAsync(db, completedCourseId, ct);

            return new(
                AttemptFinalizeStatus.AlreadyGraded,
                attempt.Id,
                attempt.OverallScore,
                attempt.Passed,
                attempt.FinalizationReason);
        }

        if (attempt.AssessmentId != assessment.Id ||
            attempt.Status is not (
                AttemptStatus.InProgress or
                AttemptStatus.Submitted or
                AttemptStatus.AutoSubmitted))
        {
            return new(AttemptFinalizeStatus.Unavailable);
        }

        var reason = attempt.FinalizationReason ??
            (now >= attempt.DeadlineUtc
                ? AttemptFinalizationReason.DeadlineElapsed
                : AttemptFinalizationReason.ManualSubmit);

        var assessmentForScoring = await db.Assessments
            .AsNoTracking()
            .Include(a => a.Questions)
                .ThenInclude(aq => aq.Question)
                    .ThenInclude(q => q.Options)
            .Include(a => a.Questions)
                .ThenInclude(aq => aq.Question)
                    .ThenInclude(q => q.AcceptedAnswers)
            .AsSplitQuery()
            .SingleAsync(a => a.Id == assessment.Id, ct);

        var answers = await db.AttemptAnswers
            .Where(a => a.AttemptId == attempt.Id)
            .ToListAsync(ct);

        if (assessmentForScoring.Questions.Count == 0)
            return new(AttemptFinalizeStatus.Unavailable);

        var byId = assessmentForScoring.Questions.ToDictionary(item => item.Id);
        var responses = new List<QuestionResponse>(answers.Count);
        var grading = new List<(AttemptAnswer Answer, bool Correct)>(answers.Count);
        foreach (var answer in answers)
        {
            if (!byId.TryGetValue(answer.AssessmentQuestionId, out var item))
                continue;
            var response = new QuestionResponse(item.QuestionId, answer.SelectedOptionId, answer.TextAnswer,
                answer.StructuredAnswerJson);
            responses.Add(response);
            grading.Add((answer, AssessmentAnswerScoring.IsCorrect(item.Question, response)));
        }

        var result = scoring.Score(
            assessmentForScoring, responses);

        if (result.MaxScore <= 0m)
            return new(AttemptFinalizeStatus.Unavailable);

        foreach (var (answer, correct) in grading)
            answer.IsCorrect = correct;

        attempt.OverallScore = result.Percentage;

        // So sánh điểm gốc trước khi làm tròn.
        attempt.Passed = assessment.PassingScore.HasValue
            ? result.Score * 100m >=
              assessment.PassingScore.Value * result.MaxScore
            : null;

        attempt.Status = AttemptStatus.Graded;
        attempt.FinalizationReason = reason;
        attempt.FinalizedAtUtc = now;
        attempt.UpdatedAtUtc = now;

        // Persist the weighted skill breakdown with the grade. The Attempt lock
        // and the graded replay branch ensure concurrent submits write it once.
        foreach (var skillQuestions in assessmentForScoring.Questions
                     .GroupBy(q => q.Question.PrimarySkill))
        {
            var skillAssessment = new Assessment { Questions = skillQuestions.ToList() };
            var skillScore = scoring.Score(skillAssessment, responses);
            db.AttemptSkillResults.Add(new AttemptSkillResult
            {
                AttemptId = attempt.Id,
                Skill = skillQuestions.Key,
                Score = skillScore.Percentage,
                QuestionCount = skillQuestions.Count()
            });
        }

        await db.SaveChangesAsync(ct);

        // The caller already holds the Course lock. Grade and completion
        // commit together, including lazy finalization from Start/Save.
        if (assessment.CourseId is Guid courseId)
            await CourseCompletion.ReconcileAsync(db, courseId, ct);

        var lastAnswerAt = grading
            .Where(item => item.Answer.SelectedOptionId.HasValue || !string.IsNullOrWhiteSpace(item.Answer.TextAnswer) ||
                !string.IsNullOrWhiteSpace(item.Answer.StructuredAnswerJson))
            .Max(item => (DateTimeOffset?)item.Answer.SavedAtUtc);
        if (lastAnswerAt.HasValue)
        {
            // A later visit can trigger deadline grading. Credit the day on which the learner last answered.
            var studiedAt = reason == AttemptFinalizationReason.DeadlineElapsed ? lastAnswerAt.Value : now;
            await StudentActivityDays.RecordAsync(db, attempt.StudentUserId, studiedAt, ct);
        }

        return new(
            AttemptFinalizeStatus.Graded,
            attempt.Id,
            attempt.OverallScore,
            attempt.Passed,
            reason);
    }


}
