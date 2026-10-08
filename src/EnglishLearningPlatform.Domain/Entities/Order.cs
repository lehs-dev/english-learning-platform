using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class Order : BaseEntity
{
    public Guid StudentUserId { get; set; }
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string Provider { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
