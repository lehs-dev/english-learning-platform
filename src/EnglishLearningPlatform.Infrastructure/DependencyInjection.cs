using EnglishLearningPlatform.Infrastructure.Authorization;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Learning.Content;
using EnglishLearningPlatform.Infrastructure.Learning.Access;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Infrastructure.QuestionBank;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Infrastructure.Commerce;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Infrastructure.AssessmentAttempts;
using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Infrastructure.Administration;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Infrastructure.Practices;
using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Infrastructure.Stimuli;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using EnglishLearningPlatform.Infrastructure.AssessmentAuthoring;

namespace EnglishLearningPlatform.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.User.RequireUniqueEmail = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = true;
            options.EventsType = typeof(AccountStatusCookieEvents);
        });

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IHostEnvironment>((options, environment) =>
                options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always);

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
        services.AddScoped<IRegistrationService, IdentityRegistrationService>();
        services.AddScoped<ILoginService, IdentityLoginService>();
        services.AddScoped<ILogoutService, IdentityLogoutService>();
        services.AddScoped<AccountStatusCookieEvents>();
        services.AddPlatformAuthorization();
        services.AddScoped<IProfileService, IdentityProfileService>();
        services.AddScoped<ActiveUserValidator>();
        services.AddScoped<IActiveUserValidator>(provider => provider.GetRequiredService<ActiveUserValidator>());
        services.AddScoped<IActiveUserRoleReader>(provider => provider.GetRequiredService<ActiveUserValidator>());
        services.AddScoped<ILearningAccessService, LearningAccessService>();
        services.AddScoped<LearningResourceAccessReaderRegistry>();
        services.AddScoped<ILearningResourceAccessReader, CourseLearningResourceReader>();
        services.AddScoped<ILearningResourceAccessReader, ModuleLearningResourceReader>();
        services.AddScoped<ILearningResourceAccessReader, LessonLearningResourceReader>();
        services.AddScoped<ILearningResourceAccessReader, LessonResourceLearningResourceReader>();
        services.AddScoped<CourseService>();
        services.AddScoped<ICourseService>(provider => provider.GetRequiredService<CourseService>());
        services.AddScoped<CourseQueries>();
        services.AddScoped<CourseProgressService>();
        services.AddScoped<ICourseCatalogService, CourseCatalogService>();
        services.AddScoped<IStudentLearningCourseService, StudentLearningCourseService>();
        services.AddScoped<ICourseAuthoringService, CourseAuthoringService>();
        services.AddScoped<CourseContentHandlerRegistry>();
        services.AddScoped<ICourseContentHandler, ModuleCourseContentHandler>();
        services.AddScoped<ICourseContentHandler, LessonCourseContentHandler>();
        services.AddScoped<ResourceCourseContentHandler>();
        services.AddScoped<ICourseContentHandler>(provider => provider.GetRequiredService<ResourceCourseContentHandler>());
        services.AddScoped<ICourseResourceDeletionHandler>(provider => provider.GetRequiredService<ResourceCourseContentHandler>());
        services.AddScoped<IQuestionCreationService, QuestionCreationService>();
        services.Configure<HostedPaymentOptions>(configuration.GetSection("Payments"));
        // Khi controller yêu cầu IQuestionQueryService,
        // DI sẽ tạo QuestionQueryService và cung cấp các dependency cần thiết.
        services.AddScoped<IQuestionQueryService, QuestionQueryService>();
        services.AddScoped<IQuestionAuthoringService, QuestionAuthoringService>();
        services.AddSingleton<HostedPaymentGateway>();
        services.AddSingleton<IPaymentGateway>(provider => provider.GetRequiredService<HostedPaymentGateway>());
        services.AddSingleton<IWebhookPaymentGateway>(provider => provider.GetRequiredService<HostedPaymentGateway>());
        services.AddSingleton<LocalSandboxGateway>();
        services.AddSingleton<ISandboxPaymentGateway>(provider => provider.GetRequiredService<LocalSandboxGateway>());
        services.AddScoped<ICheckoutTransactionFactory, CheckoutTransactionFactory>();
        services.AddScoped<CheckoutTransactionExecutor>();
        services.AddScoped<ICheckoutOrderService, CheckoutOrderService>();
        services.AddScoped<IPaymentConfirmationService, PaymentConfirmationService>();
        services.AddScoped<ISandboxCheckoutService, SandboxCheckoutService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IAttemptService, AssessmentAttemptService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAssessmentQueryService, AssessmentQueryService>();
        services.TryAddScoped<IScoringStrategy, WeightedExamScoringStrategy>();
        services.TryAddScoped<EqualWeightPracticeScoringStrategy>();
        services.TryAddScoped<IPracticeScoringStrategy>(provider => provider.GetRequiredService<EqualWeightPracticeScoringStrategy>());
        services.AddScoped<IExamScoringService, ExamScoringService>();
        services.AddScoped<IAdminQueryService, AdminQueryService>();
        services.AddScoped<IAdminDashboardQueryService, AdminDashboardQueryService>();
        services.AddScoped<IAdminOverviewDetailQueryService, AdminOverviewDetailQueryService>();
        services.AddScoped<IAdminManagementService, AdminManagementService>();
        services.AddScoped<IStudentLearningService, StudentLearningService>();
        services.AddScoped<ITeacherCourseReportService, TeacherCourseReportService>();
        services.AddScoped<IPracticeService, PracticeService>();
        services.AddScoped<ITeacherPracticeService, TeacherPracticeService>();
        // Service riêng để lưu danh sách câu hỏi của Practice Draft.
        services.AddScoped<ITeacherPracticeQuestionService,TeacherPracticeQuestionService>();
        // Service đọc danh sách Practice dành cho Teacher.
        services.AddScoped<ITeacherPracticeQueryService,TeacherPracticeQueryService>();
        // Preview Teacher chỉ đọc, không dùng service Student Submit/chấm điểm.
        services.AddScoped<ITeacherPracticePreviewService, TeacherPracticePreviewService>();
        // Tạo ngữ liệu riêng của Teacher, không dùng service chấm bài của Student.
        services.AddScoped<IStimulusCreationService, StimulusCreationService>();
        services.AddSingleton<IStimulusAudioStore>(provider => new FileStimulusAudioStore(
            configuration["StimulusAudio:StoragePath"] ?? Path.Combine(
                provider.GetRequiredService<IHostEnvironment>().ContentRootPath, "App_Data", "stimulus-audio")));
        services.AddScoped<IStimulusAudioAccessService, StimulusAudioAccessService>();
        // Service riêng cho Teacher soạn cấu hình Draft.
        // Không thay thế service Student làm bài.
        services.AddScoped<ITeacherAssessmentDraftService,TeacherAssessmentDraftService>();
        // Soạn câu hỏi và points, tách khỏi cấu hình Draft và luồng Student.
        services.AddScoped<ITeacherAssessmentQuestionService,TeacherAssessmentQuestionService>();
        // Preview chỉ đọc, không sử dụng service Start/Submit của Student.
        services.AddScoped<ITeacherAssessmentPreviewService,TeacherAssessmentPreviewService>();
        // Publish đề đã lưu, tách khỏi service Preview và Student làm bài.
        services.AddScoped<ITeacherAssessmentPublishService, TeacherAssessmentPublishService>();
        // Đổi Independent/Course-linked, không thay Final hoặc service Start.
        services.AddScoped<ITeacherAssessmentScopeService, TeacherAssessmentScopeService>();
        // Final thuộc Course; rút đề chỉ đổi trạng thái, không sửa lịch sử Attempt.
        services.AddScoped<ITeacherCourseFinalService, TeacherCourseFinalService>();
        services.AddScoped<ITeacherAssessmentLifecycleService, TeacherAssessmentLifecycleService>();
        services.AddScoped<ITeacherAssessmentCloneService, TeacherAssessmentCloneService>();
        return services;
    }
}
