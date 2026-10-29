using System.Data;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using EnglishLearningPlatform.Application.QuestionBank;

namespace EnglishLearningPlatform.Infrastructure.Practices;

public sealed class PracticeService(
    AppDbContext db,
    TimeProvider time,
    IQuestionUsageValidator questionUsageValidator,
    IActiveUserValidator? activeUserValidator,
    IPracticeScoringStrategy scoringStrategy) : IPracticeService
{
    public PracticeService(AppDbContext db, TimeProvider time, IQuestionUsageValidator questionUsageValidator)
        : this(db, time, questionUsageValidator, null, new EqualWeightPracticeScoringStrategy())
    {
    }

    public Task<LearningOutcome<PracticeSheet>> GetSheetAsync(Guid studentId, Guid practiceId, CancellationToken ct = default) =>
        ReadSheetAsync(studentId, practiceId, null, null, ct);

    // Chỉ mở trang Take mới tạo/tiếp tục phiên. Đọc Result hoặc kiểm tra quyền không tạo phiên.
    public async Task<LearningOutcome<PracticeSheet>> BeginAsync(Guid studentId, Guid practiceId, CancellationToken ct = default)
    {
        if (!await IsActiveStudentAsync(studentId, ct)) return new(LearningAccessResult.Forbidden);
        var courseId = await db.Practices.AsNoTracking().Where(p => p.Id == practiceId)
            .Select(p => p.LessonId.HasValue ? (Guid?)p.Lesson!.Module.CourseId : p.Module!.CourseId).SingleOrDefaultAsync(ct);
        if (!courseId.HasValue) return new(LearningAccessResult.NotFound);

        // Cùng thứ tự khóa với Teacher sửa đề và Student nộp bài: Course -> Practice.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.CoursesForUpdate(courseId.Value).SingleAsync(ct);
        var practice = await db.PracticesForUpdate(practiceId).AsNoTracking().SingleOrDefaultAsync(ct);
        if (practice == null) return new(LearningAccessResult.NotFound);
        var session = await db.PracticeSessions.SingleOrDefaultAsync(s => s.PracticeId == practiceId
            && s.StudentUserId == studentId && s.SubmittedAtUtc == null, ct);
        int version = session?.Version ?? practice.Version;
        Guid sessionId = session?.Id ?? Guid.NewGuid();
        var sheet = await ReadSheetAsync(studentId, practiceId, version, sessionId, ct);
        if (sheet.Access != LearningAccessResult.Allowed) return sheet;
        if (session == null)
        {
            db.PracticeSessions.Add(new PracticeSession
            {
                Id = sessionId, StudentUserId = studentId, PracticeId = practiceId, Version = version
            });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return sheet;
    }

    private async Task<LearningOutcome<PracticeSheet>> ReadSheetAsync(Guid studentId, Guid practiceId,
        int? version, Guid? sessionId, CancellationToken ct)
    {
        if (!await IsActiveStudentAsync(studentId, ct)) return new(LearningAccessResult.Forbidden);
        var practice = await db.AvailablePractices(studentId).Where(p => p.Id == practiceId)
            .Select(p => new
            {   // Owner lấy từ database, không lấy từ form của Student.
                p.OwnerTeacherUserId,
                p.Id, p.Title, p.Description, p.Version, HasResult = p.Submissions.Any(s => s.StudentUserId == studentId),
                CourseId = p.LessonId.HasValue ? p.Lesson!.Module.CourseId : p.Module!.CourseId,
                CourseTitle = p.LessonId.HasValue ? p.Lesson!.Module.Course.Title : p.Module!.Course.Title
            }).SingleOrDefaultAsync(ct);
        if (practice is null) return new(LearningAccessResult.NotFound);
        int sheetVersion = version ?? practice.Version;
        var questions = await QuestionsAsync(practiceId, sheetVersion, ct);
                // Kiểm tra trước khi trả nội dung đề cho Student.
        if (!ValidQuestions(practice.OwnerTeacherUserId, questions))
        {
            return new LearningOutcome<PracticeSheet>(
                LearningAccessResult.NotFound);
        }
        return new(LearningAccessResult.Allowed, new(practice.Id, practice.CourseId, practice.CourseTitle, practice.Title,
            practice.Description, questions.Select(q => new PracticeQuestionSheet(q.Id, q.Question.Content, q.Question.PrimarySkill,
                q.Question.Stimulus?.ContentText,
                q.Question.Stimulus?.StimulusType == StimulusType.Audio ? q.Question.Stimulus.ResourceUrl : null,
                q.Question.Options.OrderBy(o => o.OrderIndex).ThenBy(o => o.Id).Select(o => new PracticeOption(o.Id, o.Content)).ToList())).ToList())
                { HasResult = practice.HasResult, Version = sheetVersion, SessionId = sessionId });
    }

    public Task<PracticeSubmitResult> SubmitAsync(Guid studentId, Guid practiceId,
        IReadOnlyList<PracticeResponse> responses, CancellationToken ct = default) =>
        SubmitCoreAsync(studentId, practiceId, null, responses, ct);

    public Task<PracticeSubmitResult> SubmitSessionAsync(Guid studentId, Guid practiceId, Guid sessionId,
        IReadOnlyList<PracticeResponse> responses, CancellationToken ct = default) =>
        SubmitCoreAsync(studentId, practiceId, sessionId, responses, ct);

    private async Task<PracticeSubmitResult> SubmitCoreAsync(Guid studentId, Guid practiceId, Guid? sessionId,
        IReadOnlyList<PracticeResponse> responses, CancellationToken ct)
    {
        if (!await IsActiveStudentAsync(studentId, ct)) return new(PracticeSubmitStatus.Forbidden);
        var courseId = await db.Practices.AsNoTracking().Where(p => p.Id == practiceId)
            .Select(p => p.LessonId.HasValue ? (Guid?)p.Lesson!.Module.CourseId : p.Module!.CourseId).SingleOrDefaultAsync(ct);
        if (!courseId.HasValue) return new(PracticeSubmitStatus.NotFound);

        // Serialize latest-result writes, and use the same Course-first lock order as learning/assessment.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.CoursesForUpdate(courseId.Value).SingleAsync(ct);
        var practice = await db.PracticesForUpdate(practiceId)
            .SingleOrDefaultAsync(ct);
        if (practice is null || !await db.AvailablePractices(studentId).AnyAsync(p => p.Id == practiceId, ct))
            return new(PracticeSubmitStatus.Unavailable);
        await db.Entry(practice).ReloadAsync(ct);
        // Recheck the active role inside the transaction before any result write.
        if (!await IsActiveStudentAsync(studentId, ct)) return new(PracticeSubmitStatus.Forbidden);
        PracticeSession? session = null;
        if (sessionId.HasValue)
        {
            session = await db.PracticeSessions.SingleOrDefaultAsync(s => s.Id == sessionId
                && s.StudentUserId == studentId && s.PracticeId == practiceId, ct);
            if (session == null) return new(PracticeSubmitStatus.NotFound);
            // Replay từ tab đã nộp không được ghi đè kết quả của lượt làm lại sau đó.
            if (session.SubmittedAtUtc.HasValue) return new(PracticeSubmitStatus.Saved);
        }
        else
        {
            // Hỗ trợ form cũ chưa có SessionId; vẫn dùng phiên của đúng học viên nếu đã mở bài.
            session = await db.PracticeSessions.SingleOrDefaultAsync(s => s.PracticeId == practiceId
                && s.StudentUserId == studentId && s.SubmittedAtUtc == null, ct);
        }
        int version = session?.Version ?? practice.Version;
        var questions = await QuestionsAsync(practiceId, version, ct);
                // Kiểm tra lại trước khi chấm hoặc thay thế kết quả đã lưu.
        if (!ValidQuestions(practice.OwnerTeacherUserId, questions))
        {
            return new PracticeSubmitResult(
                PracticeSubmitStatus.Unavailable);
        }
        if (responses is null || responses.Count != questions.Count ||
            responses.Select(r => r.PracticeQuestionId).Distinct().Count() != responses.Count ||
            responses.Any(r => !questions.Any(q => q.Id == r.PracticeQuestionId &&
                (!r.SelectedOptionId.HasValue || q.Question.Options.Any(o => o.Id == r.SelectedOptionId)))))
            return new(PracticeSubmitStatus.InvalidAnswers);

        var responseMap = responses.ToDictionary(r => r.PracticeQuestionId, r => r.SelectedOptionId);
        var scoringItems = questions.Select(q => new QuestionScoringItem(q.Id, 1m,
            q.Question.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList())).ToList();
        var scoringResult = scoringStrategy.CalculateScore(scoringItems, responseMap);
        var submission = await db.PracticeSubmissions.SingleOrDefaultAsync(s => s.StudentUserId == studentId && s.PracticeId == practiceId, ct);
        if (submission is null)
        {
            submission = new PracticeSubmission { StudentUserId = studentId, PracticeId = practiceId };
            db.PracticeSubmissions.Add(submission);
        }
        else
        {
            await db.PracticeAnswers.Where(a => a.PracticeSubmissionId == submission.Id).ExecuteDeleteAsync(ct);
        }
        submission.Score = scoringResult.Percentage;
        submission.Version = version;
        submission.SubmittedAtUtc = time.GetUtcNow();
        if (session != null) session.SubmittedAtUtc = submission.SubmittedAtUtc;
        submission.UpdatedAtUtc = submission.SubmittedAtUtc;
        foreach (var response in responses.Where(r => r.SelectedOptionId.HasValue))
            db.PracticeAnswers.Add(new PracticeAnswer { PracticeSubmissionId = submission.Id,
                PracticeQuestionId = response.PracticeQuestionId, SelectedOptionId = response.SelectedOptionId!.Value });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(PracticeSubmitStatus.Saved);
    }

    public async Task<LearningOutcome<PracticeResult>> GetResultAsync(Guid studentId, Guid practiceId, CancellationToken ct = default)
    {
        if (!await IsActiveStudentAsync(studentId, ct)) return new(LearningAccessResult.Forbidden);
        // Only this student's latest submitted result reveals practice feedback. It remains
        // readable after withdrawal/archive; it never permits a new practice submission.
        var result = await db.PracticeSubmissions.AsNoTracking().Where(s => s.StudentUserId == studentId && s.PracticeId == practiceId)
            .Select(s => new PracticeResult(s.PracticeId,
                s.Practice.LessonId.HasValue ? s.Practice.Lesson!.Module.CourseId : s.Practice.Module!.CourseId,
                s.Practice.Title, s.Score, s.SubmittedAtUtc,
                s.Practice.Questions.Where(q => q.Version == s.Version).OrderBy(q => q.OrderIndex).ThenBy(q => q.Id).Select(q => new PracticeQuestionFeedback(
                    q.Id, q.Question.Content,
                    s.Answers.Where(a => a.PracticeQuestionId == q.Id).Select(a => a.SelectedOption.Content).FirstOrDefault(),
                    q.Question.Options.Where(o => o.IsCorrect).Select(o => o.Content).FirstOrDefault() ?? "",
                    s.Answers.Any(a => a.PracticeQuestionId == q.Id && a.SelectedOption.IsCorrect),
                    q.Question.Explanation,
                    q.Question.Stimulus == null ? null : q.Question.Stimulus.ContentText,
                    q.Question.Stimulus != null && q.Question.Stimulus.StimulusType == StimulusType.Audio
                        ? q.Question.Stimulus.ResourceUrl : null)).ToList()) { Version = s.Version }).SingleOrDefaultAsync(ct);
        return result is null ? new(LearningAccessResult.NotFound) : new(LearningAccessResult.Allowed, result);
    }

    // Legacy callers construct this service without the shared identity validator.
    private Task<bool> IsActiveStudentAsync(Guid studentId, CancellationToken ct) =>
        activeUserValidator?.IsActiveStudentAsync(studentId, ct) ?? db.IsStudentAsync(studentId, ct);

    private Task<List<PracticeQuestion>> QuestionsAsync(Guid id, int version, CancellationToken ct) => db.PracticeQuestions.AsNoTracking()
        .Where(q => q.PracticeId == id && q.Version == version).Include(q => q.Question).ThenInclude(q => q.Options)
        .Include(q => q.Question).ThenInclude(q => q.Stimulus).OrderBy(q => q.OrderIndex).ThenBy(q => q.Id).ToListAsync(ct);

    // private static bool ValidQuestions(IReadOnlyList<PracticeQuestion> questions) => questions.Count > 0 &&
    //     questions.Select(q => q.QuestionId).Distinct().Count() == questions.Count &&
    //     questions.All(q => Enum.IsDefined(q.Question.PrimarySkill) &&
    //         (q.Question.Stimulus?.StimulusType != StimulusType.Audio ||
    //             CourseRules.ValidResource(LessonResourceType.Audio, null, q.Question.Stimulus.ResourceUrl)));
    // Kiểm tra các câu hỏi đã thuộc Practice có sẵn.
    // Hàm không sửa câu hỏi hoặc ghi dữ liệu vào database.
    private bool ValidQuestions(
        Guid ownerTeacherUserId,
        IReadOnlyList<PracticeQuestion> questions)
    {
        if (ownerTeacherUserId == Guid.Empty)
        {
            return false;
        }

        // Không cho trả đề hoặc chấm một Practice rỗng.
        if (questions == null || questions.Count == 0)
        {
            return false;
        }

        // HashSet lưu ID không trùng.
        // Add trả false nếu ID đã xuất hiện trước đó.
        HashSet<Guid> questionIds = new HashSet<Guid>();

        foreach (PracticeQuestion practiceQuestion in questions)
        {
            if (practiceQuestion == null)
            {
                return false;
            }

            // Question đã tải phải khớp với liên kết PracticeQuestion.
            if (practiceQuestion.QuestionId == Guid.Empty
                || practiceQuestion.Question == null
                || practiceQuestion.Question.Id != practiceQuestion.QuestionId)
            {
                return false;
            }

            bool added = questionIds.Add(practiceQuestion.QuestionId);

            if (!added)
            {
                return false;
            }

            // Dùng chế độ ExistingUse vì câu hỏi đã thuộc Practice.
            // Archived không tự làm hỏng bài cũ.
            // Ownership, MCQ, skill và Stimulus vẫn được kiểm tra.
            IReadOnlyList<QuestionError> errors =
                questionUsageValidator.ValidateForExistingUse(
                    ownerTeacherUserId,
                    practiceQuestion.Question);

            if (errors.Count > 0)
            {
                // Không gửi lỗi chứa thông tin nội bộ của đề cho Student.
                return false;
            }
        }

        return true;
    }
}
