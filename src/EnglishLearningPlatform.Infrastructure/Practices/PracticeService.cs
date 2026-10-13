using System.Data;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Practices;

public sealed class PracticeService(AppDbContext db, TimeProvider time) : IPracticeService
{
    public async Task<LearningOutcome<PracticeSheet>> GetSheetAsync(Guid studentId, Guid practiceId, CancellationToken ct = default)
    {
        if (!await db.IsStudentAsync(studentId, ct)) return new(LearningAccessResult.Forbidden);
        var practice = await db.AvailablePractices(studentId).Where(p => p.Id == practiceId)
            .Select(p => new
            {
                p.Id, p.Title, p.Description, HasResult = p.Submissions.Any(s => s.StudentUserId == studentId),
                CourseId = p.LessonId.HasValue ? p.Lesson!.Module.CourseId : p.Module!.CourseId,
                CourseTitle = p.LessonId.HasValue ? p.Lesson!.Module.Course.Title : p.Module!.Course.Title
            }).SingleOrDefaultAsync(ct);
        if (practice is null) return new(LearningAccessResult.NotFound);
        var questions = await QuestionsAsync(practiceId, ct);
        if (!ValidQuestions(questions)) return new(LearningAccessResult.NotFound);
        return new(LearningAccessResult.Allowed, new(practice.Id, practice.CourseId, practice.CourseTitle, practice.Title,
            practice.Description, questions.Select(q => new PracticeQuestionSheet(q.Id, q.Question.Content, q.Question.PrimarySkill,
                q.Question.Stimulus?.ContentText,
                q.Question.Stimulus?.StimulusType == StimulusType.Audio ? q.Question.Stimulus.ResourceUrl : null,
                q.Question.Options.OrderBy(o => o.OrderIndex).ThenBy(o => o.Id).Select(o => new PracticeOption(o.Id, o.Content)).ToList())).ToList())
                { HasResult = practice.HasResult });
    }

    public async Task<PracticeSubmitResult> SubmitAsync(Guid studentId, Guid practiceId,
        IReadOnlyList<PracticeResponse> responses, CancellationToken ct = default)
    {
        if (!await db.IsStudentAsync(studentId, ct)) return new(PracticeSubmitStatus.Forbidden);
        var courseId = await db.Practices.AsNoTracking().Where(p => p.Id == practiceId)
            .Select(p => p.LessonId.HasValue ? (Guid?)p.Lesson!.Module.CourseId : p.Module!.CourseId).SingleOrDefaultAsync(ct);
        if (!courseId.HasValue) return new(PracticeSubmitStatus.NotFound);

        // Serialize latest-result writes, and use the same Course-first lock order as learning/assessment.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.Courses.FromSqlInterpolated($"SELECT * FROM Courses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {courseId.Value}").SingleAsync(ct);
        var practice = await db.Practices.FromSqlInterpolated($"SELECT * FROM Practices WITH (UPDLOCK, HOLDLOCK) WHERE Id = {practiceId}")
            .SingleOrDefaultAsync(ct);
        if (practice is null || !await db.AvailablePractices(studentId).AnyAsync(p => p.Id == practiceId, ct))
            return new(PracticeSubmitStatus.Unavailable);
        // Recheck the active role inside the transaction before any result write.
        if (!await db.IsStudentAsync(studentId, ct)) return new(PracticeSubmitStatus.Forbidden);
        var questions = await QuestionsAsync(practiceId, ct);
        if (!ValidQuestions(questions)) return new(PracticeSubmitStatus.Unavailable);
        if (responses is null || responses.Count != questions.Count ||
            responses.Select(r => r.PracticeQuestionId).Distinct().Count() != responses.Count ||
            responses.Any(r => !questions.Any(q => q.Id == r.PracticeQuestionId &&
                (!r.SelectedOptionId.HasValue || q.Question.Options.Any(o => o.Id == r.SelectedOptionId)))))
            return new(PracticeSubmitStatus.InvalidAnswers);

        var responseMap = responses.ToDictionary(r => r.PracticeQuestionId, r => r.SelectedOptionId);
        var correct = questions.Count(q => q.Question.Options.Any(o => o.IsCorrect && o.Id == responseMap[q.Id]));
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
        submission.Score = Math.Round(correct * 100m / questions.Count, 2);
        submission.SubmittedAtUtc = time.GetUtcNow();
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
        if (!await db.IsStudentAsync(studentId, ct)) return new(LearningAccessResult.Forbidden);
        // Only this student's latest submitted result reveals practice feedback. It remains
        // readable after withdrawal/archive; it never permits a new practice submission.
        var result = await db.PracticeSubmissions.AsNoTracking().Where(s => s.StudentUserId == studentId && s.PracticeId == practiceId)
            .Select(s => new PracticeResult(s.PracticeId,
                s.Practice.LessonId.HasValue ? s.Practice.Lesson!.Module.CourseId : s.Practice.Module!.CourseId,
                s.Practice.Title, s.Score, s.SubmittedAtUtc,
                s.Practice.Questions.OrderBy(q => q.OrderIndex).ThenBy(q => q.Id).Select(q => new PracticeQuestionFeedback(
                    q.Id, q.Question.Content,
                    s.Answers.Where(a => a.PracticeQuestionId == q.Id).Select(a => a.SelectedOption.Content).FirstOrDefault(),
                    q.Question.Options.Where(o => o.IsCorrect).Select(o => o.Content).FirstOrDefault() ?? "",
                    s.Answers.Any(a => a.PracticeQuestionId == q.Id && a.SelectedOption.IsCorrect),
                    q.Question.Explanation)).ToList())).SingleOrDefaultAsync(ct);
        return result is null ? new(LearningAccessResult.NotFound) : new(LearningAccessResult.Allowed, result);
    }

    private Task<List<PracticeQuestion>> QuestionsAsync(Guid id, CancellationToken ct) => db.PracticeQuestions.AsNoTracking()
        .Where(q => q.PracticeId == id).Include(q => q.Question).ThenInclude(q => q.Options)
        .Include(q => q.Question).ThenInclude(q => q.Stimulus).OrderBy(q => q.OrderIndex).ThenBy(q => q.Id).ToListAsync(ct);

    private static bool ValidQuestions(IReadOnlyList<PracticeQuestion> questions) => questions.Count > 0 &&
        questions.Select(q => q.QuestionId).Distinct().Count() == questions.Count &&
        questions.All(q => Enum.IsDefined(q.Question.PrimarySkill) &&
            (q.Question.Stimulus?.StimulusType != StimulusType.Audio ||
                CourseRules.ValidResource(LessonResourceType.Audio, null, q.Question.Stimulus.ResourceUrl)));
}
