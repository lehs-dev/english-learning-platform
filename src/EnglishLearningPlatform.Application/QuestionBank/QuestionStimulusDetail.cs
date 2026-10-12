using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.QuestionBank;
public sealed class QuestionStimulusDetail
{
    public Guid Id { get; set; }
    public StimulusType StimulusType { get; set; }
    public string? Title { get; set; }
    public string? ContentText { get; set; }
    public string? ResourceUrl { get; set; }
}
