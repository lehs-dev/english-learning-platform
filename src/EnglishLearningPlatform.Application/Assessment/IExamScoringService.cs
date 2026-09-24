using AssessmentEntity = EnglishLearningPlatform.Domain.Entities.Assessment;

namespace EnglishLearningPlatform.Application.Assessment;

public interface IExamScoringService
{
    ScoringResult Score(
        AssessmentEntity assessment,
        IReadOnlyCollection<QuestionResponse> responses);
}
