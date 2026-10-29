using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Practice;

public sealed record PracticeOption(Guid Id, string Content);
public sealed record PracticeQuestionSheet(Guid Id, string Content, EnglishSkill Skill,
    string? Passage, string? AudioUrl, IReadOnlyList<PracticeOption> Options);
public sealed record PracticeSheet(Guid Id, Guid CourseId, string CourseTitle, string Title,
    string? Description, IReadOnlyList<PracticeQuestionSheet> Questions)
{
    public bool HasResult { get; init; }
    public int Version { get; init; } = 1;
    public Guid? SessionId { get; init; }
}
public sealed record PracticeResponse(Guid PracticeQuestionId, Guid? SelectedOptionId);
public sealed record PracticeQuestionFeedback(Guid Id, string Content, string? SelectedAnswer,
    string CorrectAnswer, bool IsCorrect, string? Explanation, string? Passage, string? AudioUrl);
public sealed record PracticeResult(Guid PracticeId, Guid CourseId, string Title, decimal Score,
    DateTimeOffset SubmittedAtUtc, IReadOnlyList<PracticeQuestionFeedback> Questions)
{
    public int Version { get; init; } = 1;
}
public enum PracticeSubmitStatus { Saved, Forbidden, NotFound, Unavailable, InvalidAnswers }
public sealed record PracticeSubmitResult(PracticeSubmitStatus Status);

public interface IPracticeService
{
    // Mở hoặc tiếp tục phiên đang làm; giữ phiên bản cũ tới khi nộp xong.
    Task<LearningOutcome<PracticeSheet>> BeginAsync(Guid studentId, Guid practiceId, CancellationToken ct = default);
    Task<PracticeSubmitResult> SubmitSessionAsync(Guid studentId, Guid practiceId, Guid sessionId,
        IReadOnlyList<PracticeResponse> responses, CancellationToken ct = default);
    Task<LearningOutcome<PracticeSheet>> GetSheetAsync(Guid studentId, Guid practiceId, CancellationToken ct = default);
    Task<PracticeSubmitResult> SubmitAsync(Guid studentId, Guid practiceId,
        IReadOnlyList<PracticeResponse> responses, CancellationToken ct = default);
    Task<LearningOutcome<PracticeResult>> GetResultAsync(Guid studentId, Guid practiceId, CancellationToken ct = default);
}
