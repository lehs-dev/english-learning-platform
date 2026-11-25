namespace EnglishLearningPlatform.Domain.Entities;

public sealed class WritingSubmissionDraft
{
    public Guid AssignmentId { get; set; }
    public Guid StudentUserId { get; set; }
    public string Response { get; set; } = string.Empty;
    public DateTimeOffset SavedAtUtc { get; set; }
    public Guid Version { get; set; }
}
