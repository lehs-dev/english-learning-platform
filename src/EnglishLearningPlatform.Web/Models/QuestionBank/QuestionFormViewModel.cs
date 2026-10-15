using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Web.Models.QuestionBank;

public sealed class QuestionOptionFormModel
{
    public string? Content { get; set; }
}

public sealed class StimulusOptionViewModel
{
    public Guid Id { get; init; }
    public StimulusType Type { get; init; }
    public string? Title { get; init; }

    public string Label => string.IsNullOrWhiteSpace(Title)
        ? Type.ToString()
        : $"{Type} · {Title}";
}

public sealed class QuestionFormViewModel
{
    public Guid? Id { get; set; }
    public string? Content { get; set; }
    public EnglishSkill? PrimarySkill { get; set; }
    public string? Level { get; set; }
    public string? Difficulty { get; set; }
    public string? Explanation { get; set; }
    public Guid? StimulusId { get; set; }
    public List<QuestionOptionFormModel> Options { get; set; } =
    [
        new(),
        new()
    ];
    public int? CorrectOptionIndex { get; set; }
    public QuestionStatus Status { get; set; } = QuestionStatus.Draft;
    public bool IsEdit { get; set; }
    public bool ScoringContentLocked { get; set; }
    public string? LockReason { get; set; }
    public IReadOnlyList<StimulusOptionViewModel> Stimuli { get; set; } = [];
}
