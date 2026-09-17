using EnglishLearningPlatform.Application.Assessment;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishLearningPlatform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IExamScoringService, ExamScoringService>();
        return services;
    }
}
