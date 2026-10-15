namespace EnglishLearningPlatform.Application.QuestionBank;

public interface IQuestionAuthoringService
{
    Task<QuestionEditor?> GetEditorAsync(
        Guid actorUserId,
        Guid questionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QuestionStimulusOption>> GetStimuliAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<QuestionWriteResult> UpdateAsync(
        Guid actorUserId,
        Guid questionId,
        CreateQuestionRequest request,
        CancellationToken cancellationToken = default);
}
