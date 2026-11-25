using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Web.Models;

public sealed record StructuredQuestionInputModel(string Id, string Name, QuestionType QuestionType,
    StructuredQuestionPrompt Prompt, string? SavedJson = null, bool Preview = false);
