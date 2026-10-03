namespace EnglishLearningPlatform.Application.QuestionBank;
public sealed record QuestionError(
    string Field,
    string Code,
    string Message
);