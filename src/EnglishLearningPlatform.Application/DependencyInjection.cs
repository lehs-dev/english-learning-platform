using EnglishLearningPlatform.Application.Assessment;
using Microsoft.Extensions.DependencyInjection;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.Stimuli;
namespace EnglishLearningPlatform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IExamScoringService, ExamScoringService>();
        services.AddScoped<ICreateQuestionValidator, CreateQuestionValidator>();
        // Validator chỉ kiểm tra dữ liệu đầu vào, không truy cập database.
        services.AddScoped<ICreatePracticeValidator, CreatePracticeValidator>();
        // Cung cấp validator kiểm tra câu hỏi trước khi sử dụng.
        services.AddScoped<IQuestionUsageValidator, QuestionUsageValidator>();
        // Validator ngữ liệu chỉ kiểm tra dữ liệu, không truy cập tài nguyên ngoài.
        services.AddScoped<ICreateStimulusValidator, CreateStimulusValidator>();
        return services;
    }
}
