using EnglishLearningPlatform.Domain.Entities;

namespace EnglishLearningPlatform.Application.Assessment;

public interface IExamScoringService
{
    ScoringResult Score(Exam exam, IReadOnlyCollection<QuestionResponse> responses);
}
