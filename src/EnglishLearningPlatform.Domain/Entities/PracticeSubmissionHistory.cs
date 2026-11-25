using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

// Immutable snapshot of a displaced latest-result record. Existing latest-result contracts remain compatible.
public sealed class PracticeSubmissionHistory : BaseEntity
{
    public Guid SourceSubmissionId { get; set; }
    public DateTimeOffset SourceCreatedAtUtc { get; set; }
    public DateTimeOffset? SourceUpdatedAtUtc { get; set; }
    public Guid StudentUserId { get; set; }
    public Guid PracticeId { get; set; }
    public int Version { get; set; }
    public decimal Score { get; set; }
    public DateTimeOffset SubmittedAtUtc { get; set; }
    public ICollection<PracticeAnswerHistory> Answers { get; set; } = new List<PracticeAnswerHistory>();
}

public sealed class PracticeAnswerHistory : BaseEntity
{
    public Guid HistoryId { get; set; }
    public PracticeSubmissionHistory History { get; set; } = null!;
    public Guid? SourceAnswerId { get; set; }
    public DateTimeOffset? SourceCreatedAtUtc { get; set; }
    public DateTimeOffset? SourceUpdatedAtUtc { get; set; }
    public string? StructuredAnswerJson { get; set; }
    public Guid PracticeQuestionId { get; set; }
    public Guid? SelectedOptionId { get; set; }
    public string? TextAnswer { get; set; }
    public string QuestionContent { get; set; } = string.Empty;
    public string? SelectedAnswer { get; set; }
    public string CorrectAnswersJson { get; set; } = "[]";
    public string? Explanation { get; set; }
    public bool IsCorrect { get; set; }
}
