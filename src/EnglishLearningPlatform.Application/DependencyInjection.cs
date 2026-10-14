using EnglishLearningPlatform.Application.Assessment;
using Microsoft.Extensions.DependencyInjection;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Application.Practice;
namespace EnglishLearningPlatform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IExamScoringService, ExamScoringService>();
        services.AddScoped<ICreateQuestionValidator, CreateQuestionValidator>();
        // Validator chỉ kiểm tra dữ liệu đầu vào, không truy cập database.
        services.AddScoped<ICreatePracticeValidator, CreatePracticeValidator>();
        return services;
    }
}
