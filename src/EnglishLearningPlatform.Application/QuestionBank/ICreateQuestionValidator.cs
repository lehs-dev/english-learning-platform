namespace EnglishLearningPlatform.Application.QuestionBank;

public interface ICreateQuestionValidator
{
    IReadOnlyList<QuestionError> Validate(CreateQuestionRequest request);
}