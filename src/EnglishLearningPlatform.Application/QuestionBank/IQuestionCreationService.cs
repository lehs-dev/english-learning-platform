namespace EnglishLearningPlatform.Application.QuestionBank;

public interface IQuestionCreationService
{
    Task<QuestionWriteResult> CreateAsync(Guid actorUserId,
    CreateQuestionRequest request,
    CancellationToken cancellationToken = default);
}
