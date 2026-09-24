using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class PracticeQuestion : BaseEntity
{
    public Guid PracticeId { get; set; }
    public Practice Practice { get; set; } = null!;
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public int OrderIndex { get; set; }

    public ICollection<PracticeAnswer> Answers { get; set; } = new List<PracticeAnswer>();
}
