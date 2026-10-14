namespace EnglishLearningPlatform.Web.Models.Practice;

public sealed class PracticeSubmissionInput
{
    public List<PracticeAnswerInput>? Answers { get; set; } = [];
}
public sealed class PracticeAnswerInput
{
    public Guid PracticeQuestionId { get; set; }
    public Guid? SelectedOptionId { get; set; }
}
