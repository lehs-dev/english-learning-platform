using EnglishLearningPlatform.Application.Assessment;
using Microsoft.Extensions.DependencyInjection;
using EnglishLearningPlatform.Application.QuestionBank;

namespace EnglishLearningPlatform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IExamScoringService, ExamScoringService>();
        services.AddScoped<ICreateQuestionValidator, CreateQuestionValidator>();
        return services;
    }
}
