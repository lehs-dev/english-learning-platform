using EnglishLearningPlatform.Application;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure;
using EnglishLearningPlatform.Infrastructure.Commerce;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Architecture;

public sealed class RefactoringCompatibilityTests
{
    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] =
                "Server=localhost;Database=ArchitectureTests;Trusted_Connection=True;TrustServerCertificate=True"
        }).Build();

    [Fact]
    public void FocusedContractsResolveAlongsideCompatibilityFacadesAndRemainScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        var configuration = Configuration();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.AddApplication();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var courses = firstScope.ServiceProvider.GetRequiredService<ICourseService>();

        Assert.IsType<CourseService>(courses);
        var catalog = Assert.IsType<CourseCatalogService>(firstScope.ServiceProvider.GetRequiredService<ICourseCatalogService>());
        var learning = Assert.IsType<StudentLearningCourseService>(firstScope.ServiceProvider.GetRequiredService<IStudentLearningCourseService>());
        var authoring = Assert.IsType<CourseAuthoringService>(firstScope.ServiceProvider.GetRequiredService<ICourseAuthoringService>());
        Assert.Same(catalog, firstScope.ServiceProvider.GetRequiredService<ICourseCatalogService>());
        Assert.Same(learning, firstScope.ServiceProvider.GetRequiredService<IStudentLearningCourseService>());
        Assert.Same(authoring, firstScope.ServiceProvider.GetRequiredService<ICourseAuthoringService>());
        Assert.NotSame(courses, secondScope.ServiceProvider.GetRequiredService<ICourseService>());
        Assert.NotSame(catalog, secondScope.ServiceProvider.GetRequiredService<ICourseCatalogService>());
        Assert.NotSame(learning, secondScope.ServiceProvider.GetRequiredService<IStudentLearningCourseService>());
        Assert.NotSame(authoring, secondScope.ServiceProvider.GetRequiredService<ICourseAuthoringService>());
        Assert.Same(firstScope.ServiceProvider.GetRequiredService<IActiveUserValidator>(),
            firstScope.ServiceProvider.GetRequiredService<IActiveUserRoleReader>());
        Assert.IsType<CheckoutService>(firstScope.ServiceProvider.GetRequiredService<ICheckoutService>());
        Assert.IsType<CheckoutOrderService>(firstScope.ServiceProvider.GetRequiredService<ICheckoutOrderService>());
        Assert.IsType<PaymentConfirmationService>(firstScope.ServiceProvider.GetRequiredService<IPaymentConfirmationService>());
        Assert.IsType<SandboxCheckoutService>(firstScope.ServiceProvider.GetRequiredService<ISandboxCheckoutService>());
        Assert.Same(provider.GetRequiredService<IPaymentGateway>(), provider.GetRequiredService<IWebhookPaymentGateway>());
        Assert.Same(provider.GetRequiredService<LocalSandboxGateway>(), provider.GetRequiredService<ISandboxPaymentGateway>());
    }

    [Fact]
    public void RefactoredDomainModelStillMatchesTheExistingMigrationSnapshot()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(Configuration().GetConnectionString("DefaultConnection")).Options);

        // This compares models in memory; it does not connect to SQL Server.
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "RefactoringCompatibilityTests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
