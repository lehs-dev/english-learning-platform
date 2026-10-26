using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.QuestionBank;
public sealed class QuestionDetail
{   //Id of the question
    public Guid Id {get; set;}
    //Content of the question
    public string Content { get; set; } = string.Empty;
    public EnglishSkill PrimarySkill { get; set; }

    //infomation non-required
    public string? Level { get; set; }
    public string? Explanation { get; set; }
    public QuestionStatus Status { get; set; } = QuestionStatus.Draft;
    public QuestionStimulusDetail? Stimulus { get; set; }
    public IReadOnlyList<QuestionOptionItem> Options { get; set; } = new List<QuestionOptionItem>();
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }

}
