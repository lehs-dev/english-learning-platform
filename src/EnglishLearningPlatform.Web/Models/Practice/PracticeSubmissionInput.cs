namespace EnglishLearningPlatform.Web.Models.Practice;

public sealed class PracticeSubmissionInput
{
    public Guid? SessionId { get; set; }
    public List<PracticeAnswerInput>? Answers { get; set; } = [];
}
public sealed class PracticeAnswerInput
{
    public Guid PracticeQuestionId { get; set; }
    public Guid? SelectedOptionId { get; set; }
    [System.ComponentModel.DataAnnotations.StringLength(500)]
    public string? TextAnswer { get; set; }
    [System.ComponentModel.DataAnnotations.StringLength(4096)]
    public string? StructuredAnswerJson { get; set; }
}
