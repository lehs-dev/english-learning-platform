using System.ComponentModel.DataAnnotations;
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

    private string TypeLabel => Type == StimulusType.Passage ? "Bài đọc" : "Âm thanh";
    public string Label => string.IsNullOrWhiteSpace(Title) ? TypeLabel : $"{TypeLabel} · {Title}";
}

public sealed class QuestionFormViewModel
{
    public Guid? Id { get; set; }
    public string? Content { get; set; }
    public EnglishSkill? PrimarySkill { get; set; }
    [Required(ErrorMessage = "Vui lòng chọn trình độ từ A1 đến C2.")]
    [RegularExpression("^(A1|A2|B1|B2|C1|C2)$", ErrorMessage = "Trình độ phải là A1, A2, B1, B2, C1 hoặc C2.")]
    public string? Level { get; set; }
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
