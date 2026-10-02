namespace EnglishLearningPlatform.Application.QuestionBank;
public sealed record QuestionOptionRequest(
    string Content,
    bool IsCorrect);