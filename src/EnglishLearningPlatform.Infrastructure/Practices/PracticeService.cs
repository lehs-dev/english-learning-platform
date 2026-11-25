using System.Data;
using System.Security.Cryptography;
using System.Text;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Gamification;
using EnglishLearningPlatform.Application.LearningPath;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Gamification;
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
    IPracticeScoringStrategy scoringStrategy,
    IGamificationService? gamification = null,
    ILearningPathService? learningPath = null) : IPracticeService
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
                Id = sessionId,
                StudentUserId = studentId,
                PracticeId = practiceId,
                Version = version
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
        var practice = await ReadAvailablePracticeAsync(studentId, practiceId, ct);
        if (practice is null) return new(LearningAccessResult.NotFound);
        int sheetVersion = version ?? practice.Version;
        var questions = await QuestionsAsync(practiceId, sheetVersion, ct);
        // Kiểm tra trước khi trả nội dung đề cho Student.
        if (!ValidQuestions(practice.OwnerTeacherUserId, questions))
        {
            return new LearningOutcome<PracticeSheet>(
                LearningAccessResult.NotFound);
        }
        var sheet = BuildSheet(practice, questions) with { Version = sheetVersion, SessionId = sessionId };
        return new(LearningAccessResult.Allowed, sheet);
    }

    private sealed record AvailablePractice(Guid Id, Guid OwnerTeacherUserId, Guid CourseId, string CourseTitle,
        string Title, string? Description, int Version, bool HasResult);

    private Task<AvailablePractice?> ReadAvailablePracticeAsync(Guid studentId, Guid practiceId, CancellationToken ct) =>
        db.AvailablePractices(studentId).Where(p => p.Id == practiceId)
            .Select(p => new AvailablePractice(p.Id, p.OwnerTeacherUserId,
                p.LessonId.HasValue ? p.Lesson!.Module.CourseId : p.Module!.CourseId,
                p.LessonId.HasValue ? p.Lesson!.Module.Course.Title : p.Module!.Course.Title,
                p.Title, p.Description, p.Version, p.Submissions.Any(s => s.StudentUserId == studentId)))
            .SingleOrDefaultAsync(ct);

    private static PracticeSheet BuildSheet(AvailablePractice practice, IReadOnlyList<PracticeQuestion> questions) =>
        new(practice.Id, practice.CourseId, practice.CourseTitle, practice.Title,
            practice.Description, questions.Select(q => new PracticeQuestionSheet(q.Id, q.Question.Content, q.Question.PrimarySkill,
                q.Question.Stimulus?.StimulusType == StimulusType.Passage ? q.Question.Stimulus.ContentText : null,
                q.Question.Stimulus?.StimulusType == StimulusType.Audio ? q.Question.Stimulus.ResourceUrl : null,
                q.Question.Options.OrderBy(o => o.OrderIndex).ThenBy(o => o.Id).Select(o => new PracticeOption(o.Id, o.Content)).ToList())
                { QuestionType = q.Question.QuestionType, StructuredPrompt = StructuredQuestionEvaluator.GetPrompt(q.Question) }).ToList())
        { HasResult = practice.HasResult, Version = practice.Version };

    public async Task<LearningOutcome<PracticeSheet>> GetMistakeSheetAsync(Guid studentId, Guid practiceId, CancellationToken ct = default)
    {
        var review = await ReadReviewAsync(studentId, practiceId, ct);
        return review.Value is { } source
            ? new(LearningAccessResult.Allowed, BuildSheet(source.Practice, source.Questions) with { ReviewStamp = source.Stamp })
            : new(review.Access);
    }

    public async Task<PracticeReviewResult> CheckMistakesAsync(Guid studentId, Guid practiceId, string reviewStamp,
        IReadOnlyList<PracticeResponse> responses, CancellationToken ct = default)
    {
        var review = await ReadReviewAsync(studentId, practiceId, ct);
        if (review.Access == LearningAccessResult.Forbidden) return new(PracticeReviewStatus.Forbidden);
        if (review.Value is not { } source || !string.Equals(reviewStamp, source.Stamp, StringComparison.Ordinal))
            return new(PracticeReviewStatus.Stale);
        var grades = GradeResponses(source.Questions, responses);
        if (grades is null) return new(PracticeReviewStatus.InvalidAnswers);

        var responseById = responses.ToDictionary(response => response.PracticeQuestionId);
        var feedback = source.Questions.Select(question =>
        {
            var response = responseById[question.Id];
            var grade = grades[question.Id];
            return Feedback(question, new PracticeAnswer
            {
                SelectedOption = question.Question.Options.FirstOrDefault(option => option.Id == response.SelectedOptionId),
                TextAnswer = response.TextAnswer,
                StructuredAnswerJson = grade.StructuredJson,
                IsCorrect = grade.IsCorrect
            });
        }).ToList();

        var result = new PracticeResult(practiceId, source.Practice.CourseId, source.Practice.Title,
            Score(source.Questions, grades, responses), time.GetUtcNow(), feedback) { Version = source.Practice.Version };
        return new(PracticeReviewStatus.Graded, result);
    }

    private sealed record ReviewSource(AvailablePractice Practice, IReadOnlyList<PracticeQuestion> Questions, string Stamp);

    private async Task<LearningOutcome<ReviewSource>> ReadReviewAsync(Guid studentId, Guid practiceId, CancellationToken ct)
    {
        if (!await IsActiveStudentAsync(studentId, ct)) return new(LearningAccessResult.Forbidden);
        var practice = await ReadAvailablePracticeAsync(studentId, practiceId, ct);
        if (practice is not { HasResult: true }) return new(LearningAccessResult.NotFound);

        var submission = await db.PracticeSubmissions.AsNoTracking()
            .Include(s => s.Answers).ThenInclude(a => a.SelectedOption)
            .SingleOrDefaultAsync(s => s.StudentUserId == studentId && s.PracticeId == practiceId &&
                s.Version == practice.Version && s.Version == s.Practice.Version, ct);
        if (submission is null) return new(LearningAccessResult.NotFound);

        var questions = await QuestionsAsync(practiceId, submission.Version, ct);
        if (!ValidQuestions(practice.OwnerTeacherUserId, questions)) return new(LearningAccessResult.NotFound);
        var answers = submission.Answers.ToDictionary(answer => answer.PracticeQuestionId);
        var mistakes = questions.Where(question => !Feedback(question, answers.GetValueOrDefault(question.Id)).IsCorrect).ToList();
        if (mistakes.Count == 0) return new(LearningAccessResult.NotFound);

        // The latest result row is reused on resubmission. Bind to its revision and saved answer rows.
        var answerIds = string.Join(",", submission.Answers.Select(answer => answer.Id).Order());
        var revision = FormattableString.Invariant($"{submission.Id}:{submission.Version}:{submission.SubmittedAtUtc.UtcTicks}:{answerIds}");
        var stamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revision)));
        return new(LearningAccessResult.Allowed, new(practice, mistakes, stamp));
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
            if (session.SubmittedAtUtc.HasValue)
            {
                await tx.CommitAsync(ct);
                await tx.DisposeAsync();
                await TrackLearningAsync(studentId, practiceId, ct);
                return new(PracticeSubmitStatus.Saved);
            }
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
        var grades = GradeResponses(questions, responses);
        if (grades is null) return new(PracticeSubmitStatus.InvalidAnswers);
        var submission = await db.PracticeSubmissions.SingleOrDefaultAsync(s => s.StudentUserId == studentId && s.PracticeId == practiceId, ct);
        if (submission is null)
        {
            submission = new PracticeSubmission { StudentUserId = studentId, PracticeId = practiceId };
            db.PracticeSubmissions.Add(submission);
        }
        else
        {
            await ArchiveLatestAsync(submission, ct);
            await db.PracticeAnswers.Where(a => a.PracticeSubmissionId == submission.Id).ExecuteDeleteAsync(ct);
        }
        submission.Score = Score(questions, grades, responses);
        submission.Version = version;
        submission.SubmittedAtUtc = time.GetUtcNow();
        if (session != null) session.SubmittedAtUtc = submission.SubmittedAtUtc;
        submission.UpdatedAtUtc = submission.SubmittedAtUtc;
        foreach (var response in responses)
        {
            var grade = grades[response.PracticeQuestionId];
            if (!response.SelectedOptionId.HasValue && string.IsNullOrEmpty(response.TextAnswer) && grade.StructuredJson is null)
                continue;
            db.PracticeAnswers.Add(new PracticeAnswer
            {
                PracticeSubmissionId = submission.Id,
                PracticeQuestionId = response.PracticeQuestionId,
                SelectedOptionId = response.SelectedOptionId,
                TextAnswer = response.TextAnswer,
                StructuredAnswerJson = grade.StructuredJson,
                IsCorrect = grade.IsCorrect
            });
        }
        await db.SaveChangesAsync(ct);
        await StudentActivityDays.RecordAsync(db, studentId, submission.SubmittedAtUtc, ct);
        await tx.CommitAsync(ct);
        await tx.DisposeAsync();
        await TrackLearningAsync(studentId, practiceId, ct);
        return new(PracticeSubmitStatus.Saved);
    }

    private async Task TrackLearningAsync(Guid studentId, Guid practiceId, CancellationToken ct)
    {
        if (gamification is not null)
        {
            await gamification.AwardXpAsync(studentId, GamificationEventType.PracticeSubmitted, practiceId, 5, ct);
            await gamification.AwardXpAsync(studentId, GamificationEventType.PracticeHighScorer, practiceId, 10, ct);
        }
        if (learningPath is not null)
            await learningPath.TrackCompletionAsync(studentId, LearningPlanTargetType.Practice, practiceId, ct);
    }

    public async Task<LearningOutcome<PracticeResult>> GetResultAsync(Guid studentId, Guid practiceId, CancellationToken ct = default)
    {
        if (!await IsActiveStudentAsync(studentId, ct)) return new(LearningAccessResult.Forbidden);
        // Only this student's latest submitted result reveals practice feedback. It remains
        // readable after withdrawal/archive; it never permits a new practice submission.
        var submission = await db.PracticeSubmissions.AsNoTracking()
            .Include(s => s.Answers).ThenInclude(a => a.SelectedOption)
            .Include(s => s.Practice).ThenInclude(p => p.Lesson).ThenInclude(l => l!.Module)
            .Include(s => s.Practice).ThenInclude(p => p.Module)
            .SingleOrDefaultAsync(s => s.StudentUserId == studentId && s.PracticeId == practiceId, ct);
        if (submission == null) return new(LearningAccessResult.NotFound);
        var questions = await QuestionsAsync(practiceId, submission.Version, ct);
        var feedback = questions.Select(q => Feedback(q, submission.Answers.FirstOrDefault(a => a.PracticeQuestionId == q.Id))).ToList();
        return new(LearningAccessResult.Allowed, new PracticeResult(practiceId,
            submission.Practice.LessonId.HasValue ? submission.Practice.Lesson!.Module.CourseId : submission.Practice.Module!.CourseId,
            submission.Practice.Title, submission.Score, submission.SubmittedAtUtc, feedback) { Version = submission.Version });
    }

    private sealed record AnswerGrade(bool IsCorrect, string? StructuredJson = null);

    private static Dictionary<Guid, AnswerGrade>? GradeResponses(
        IReadOnlyList<PracticeQuestion> questions, IReadOnlyList<PracticeResponse>? responses)
    {
        if (responses is null || responses.Count != questions.Count) return null;
        var questionById = questions.ToDictionary(q => q.Id, q => q.Question);
        var grades = new Dictionary<Guid, AnswerGrade>();
        foreach (var response in responses)
        {
            if (!questionById.TryGetValue(response.PracticeQuestionId, out var question) ||
                EvaluateResponse(question, response) is not { } grade || !grades.TryAdd(response.PracticeQuestionId, grade))
                return null;
        }
        return grades;
    }

    private decimal Score(IReadOnlyList<PracticeQuestion> questions, Dictionary<Guid, AnswerGrade> grades,
        IReadOnlyList<PracticeResponse> responses)
    {
        var responseById = responses.ToDictionary(response => response.PracticeQuestionId);
        var responseMap = questions.ToDictionary(question => question.Id, question =>
            question.Question.QuestionType == QuestionType.MultipleChoice ? responseById[question.Id].SelectedOptionId
                : grades[question.Id].IsCorrect ? (Guid?)question.Id : null);
        var scoringItems = questions.Select(q => new QuestionScoringItem(q.Id, 1m,
            q.Question.QuestionType == QuestionType.MultipleChoice
                ? q.Question.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToArray()
                : [q.Id])).ToList();
        return scoringStrategy.CalculateScore(scoringItems, responseMap).Percentage;
    }

    private static AnswerGrade? EvaluateResponse(Question question, PracticeResponse response)
    {
        if (StructuredQuestionEvaluator.IsStructured(question.QuestionType))
        {
            if (response.SelectedOptionId.HasValue || !string.IsNullOrEmpty(response.TextAnswer)) return null;
            var result = StructuredQuestionEvaluator.Evaluate(question, response.StructuredAnswerJson);
            return result.IsValid ? new(result.IsCorrect, result.CanonicalJson) : null;
        }

        if (!string.IsNullOrEmpty(response.StructuredAnswerJson)) return null;
        if (question.QuestionType is (QuestionType.FillBlank or QuestionType.Dictation) && !response.SelectedOptionId.HasValue && (response.TextAnswer?.Length ?? 0) <= 500)
            return new(AnswerNormalizer.Matches(response.TextAnswer, question.AcceptedAnswers.Select(a => a.AnswerText)));
        if (question.QuestionType != QuestionType.MultipleChoice || !string.IsNullOrEmpty(response.TextAnswer)) return null;
        if (response.SelectedOptionId is not Guid optionId) return new(false);
        var option = question.Options.FirstOrDefault(o => o.Id == optionId);
        return option is null ? null : new(option.IsCorrect);
    }

    private static PracticeQuestionFeedback Feedback(PracticeQuestion q, PracticeAnswer? answer)
    {
        var question = q.Question;
        string? selected = answer?.SelectedOption?.Content;
        var accepted = question.Options.Where(o => o.IsCorrect).Select(o => o.Content).ToArray();
        bool correct = answer?.SelectedOption?.IsCorrect == true;
        if (question.QuestionType is QuestionType.FillBlank or QuestionType.Dictation)
        {
            selected = answer?.TextAnswer;
            accepted = question.AcceptedAnswers.OrderBy(a => a.OrderIndex).Select(a => a.AnswerText).ToArray();
            correct = AnswerNormalizer.Matches(selected, accepted);
        }
        else if (StructuredQuestionEvaluator.IsStructured(question.QuestionType))
        {
            var result = StructuredQuestionEvaluator.Evaluate(question, answer?.StructuredAnswerJson);
            selected = result.SubmittedAnswer;
            accepted = [result.CorrectAnswer];
            correct = result.IsCorrect;
        }
        return new(q.Id, question.Content, selected, string.Join(" / ", accepted), answer?.IsCorrect ?? correct,
            question.Explanation, question.Stimulus?.ContentText,
            question.Stimulus?.StimulusType == StimulusType.Audio ? question.Stimulus.ResourceUrl : null)
            { QuestionType = question.QuestionType, AcceptedAnswers = accepted };
    }

    private async Task ArchiveLatestAsync(PracticeSubmission submission, CancellationToken ct)
    {
        var answers = await db.PracticeAnswers.AsNoTracking().Where(a => a.PracticeSubmissionId == submission.Id)
            .Include(a => a.SelectedOption).ToListAsync(ct);
        var questions = await QuestionsAsync(submission.PracticeId, submission.Version, ct);
        var history = new PracticeSubmissionHistory
        {
            SourceSubmissionId = submission.Id, SourceCreatedAtUtc = submission.CreatedAtUtc, SourceUpdatedAtUtc = submission.UpdatedAtUtc,
            StudentUserId = submission.StudentUserId, PracticeId = submission.PracticeId,
            Version = submission.Version, Score = submission.Score, SubmittedAtUtc = submission.SubmittedAtUtc
        };
        foreach (var question in questions)
        {
            var answer = answers.FirstOrDefault(a => a.PracticeQuestionId == question.Id);
            var feedback = Feedback(question, answer);
            history.Answers.Add(new PracticeAnswerHistory
            {
                SourceAnswerId = answer?.Id, SourceCreatedAtUtc = answer?.CreatedAtUtc, SourceUpdatedAtUtc = answer?.UpdatedAtUtc,
                StructuredAnswerJson = answer?.StructuredAnswerJson,
                PracticeQuestionId = question.Id, SelectedOptionId = answer?.SelectedOptionId, TextAnswer = answer?.TextAnswer,
                QuestionContent = question.Question.Content, SelectedAnswer = feedback.SelectedAnswer,
                CorrectAnswersJson = System.Text.Json.JsonSerializer.Serialize(feedback.AcceptedAnswers),
                Explanation = feedback.Explanation, IsCorrect = feedback.IsCorrect
            });
        }
        db.Set<PracticeSubmissionHistory>().Add(history);
    }

    // Legacy callers construct this service without the shared identity validator.
    private Task<bool> IsActiveStudentAsync(Guid studentId, CancellationToken ct) =>
        activeUserValidator?.IsActiveStudentAsync(studentId, ct) ?? db.IsStudentAsync(studentId, ct);

    private Task<List<PracticeQuestion>> QuestionsAsync(Guid id, int version, CancellationToken ct) => db.PracticeQuestions.AsNoTracking()
        .Where(q => q.PracticeId == id && q.Version == version).Include(q => q.Question).ThenInclude(q => q.Options)
        .Include(q => q.Question).ThenInclude(q => q.AcceptedAnswers)
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
