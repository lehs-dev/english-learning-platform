namespace EnglishLearningPlatform.Domain.Entities;

public sealed class StudentActivityDay
{
    public Guid StudentUserId { get; set; }
    public DateOnly ActivityDate { get; set; }
}
