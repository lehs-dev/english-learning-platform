using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.QuestionBank;
public sealed class QuestionListItem
{
    public Guid Id {get; set;}
    public string Content {get; set;} = string.Empty;
    public string? Level {get; set;}
    public string? Difficulty {get; set;}
    public EnglishSkill PrimarySkill {get; set;}
    public QuestionStatus Status {get; set;}

    public int OptionCount {get; set;}
    public DateTimeOffset CreatedAtUtc {get; set;}

}
