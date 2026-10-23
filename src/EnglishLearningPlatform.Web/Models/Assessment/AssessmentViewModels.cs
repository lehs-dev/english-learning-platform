using EnglishLearningPlatform.Application.Assessment;

namespace EnglishLearningPlatform.Web.Models.Assessment;

public sealed record AssessmentTakePage(AssessmentOverviewModel? Overview, AssessmentTakingSheet? Sheet);

// Trang hết giờ chỉ cần ID lượt làm, không mang theo nội dung hoặc đáp án.
public sealed record AssessmentExpiredPage(Guid AttemptId);

public sealed class AssessmentSubmissionInput
{
    public List<AssessmentAnswerInput> Answers { get; set; } = [];
}

public sealed class AssessmentAnswerInput
{
    public Guid AssessmentQuestionId { get; set; }
    public Guid? SelectedOptionId { get; set; }
}
