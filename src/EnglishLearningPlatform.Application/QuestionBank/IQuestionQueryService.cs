namespace EnglishLearningPlatform.Application.QuestionBank;
public interface IQuestionQueryService
{   //create question list
    Task<QuestionSearchResult?> SearchAsync(Guid actorUserId, QuestionSearchRequest request, CancellationToken cancellationToken = default);
    //get question detail by id
    Task<QuestionDetail?> GetByIdAsync(Guid actorUserId, Guid questionId, CancellationToken cancellationToken = default);


}