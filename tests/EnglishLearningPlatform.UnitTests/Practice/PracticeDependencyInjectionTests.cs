using EnglishLearningPlatform.Application;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Infrastructure;
using EnglishLearningPlatform.Infrastructure.Practices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Practice;

public sealed class PracticeDependencyInjectionTests
{
    [Fact]
    public void AddApplication_RegistersCreatePracticeValidatorOnceAsScoped()
    {
        var services = new ServiceCollection();

        services.AddApplication();

        var registration = Assert.Single(services,
            item => item.ServiceType == typeof(ICreatePracticeValidator));
        Assert.Equal(typeof(CreatePracticeValidator), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
    }

    [Fact]
    public void AddApplication_ResolvesValidatorWithSeparateInstancesPerScope()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<ICreatePracticeValidator>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<ICreatePracticeValidator>();
        var second = secondScope.ServiceProvider.GetRequiredService<ICreatePracticeValidator>();

        Assert.IsType<CreatePracticeValidator>(first);
        Assert.Same(first, sameScope);
        Assert.NotSame(first, second);
        Assert.Empty(first.Validate(new CreatePracticeRequest { Title = "Grammar practice" }));
    }

    [Fact]
    public void AddInfrastructure_RegistersTeacherPracticeServiceOnceAsScoped()
    {
        // Chỉ kiểm tra đăng ký DI, không kết nối đến database này.
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] =
                "Server=localhost;Database=PracticeDiTests;Trusted_Connection=True;"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);

        var registration = Assert.Single(services,
            item => item.ServiceType == typeof(ITeacherPracticeService));
        Assert.Equal(typeof(TeacherPracticeService), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
    }
}
