using EnglishLearningPlatform.Application;
using EnglishLearningPlatform.Application.QuestionBank;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using EnglishLearningPlatform.Infrastructure;
using EnglishLearningPlatform.Infrastructure.QuestionBank;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.QuestionBank;
public sealed class QuestionBankDependencyInjectionTests
{
    // Validator mới phải được đăng ký đúng class và đúng lifetime.
    [Fact]
    public void AddApplication_RegistersQuestionUsageValidatorAsScoped()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddApplication();

        ServiceDescriptor registration = Assert.Single(
            services,
            item => item.ServiceType == typeof(IQuestionUsageValidator));

        Assert.Equal(typeof(QuestionUsageValidator), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
    }

    // Resolve thật để phát hiện dependency bị thiếu, không chỉ kiểm tra đăng ký.
    [Fact]
    public void AddApplication_ResolvesQuestionUsageValidatorAndKeepsInstancesScoped()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddApplication();
        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
        using IServiceScope firstScope = provider.CreateScope();
        using IServiceScope secondScope = provider.CreateScope();

        IQuestionUsageValidator first = firstScope.ServiceProvider
            .GetRequiredService<IQuestionUsageValidator>();
        IQuestionUsageValidator firstAgain = firstScope.ServiceProvider
            .GetRequiredService<IQuestionUsageValidator>();
        IQuestionUsageValidator second = secondScope.ServiceProvider
            .GetRequiredService<IQuestionUsageValidator>();

        Assert.IsType<QuestionUsageValidator>(first);
        Assert.Same(first, firstAgain);
        Assert.NotSame(first, second);
        var errors = first.ValidateForNewUse(Guid.NewGuid(), null);
        Assert.Single(errors);
        Assert.Equal("not_found", errors[0].Code);

        // Interface đã có hai chế độ; cùng instance DI phải gọi được cả hai.
        var existingErrors = first.ValidateForExistingUse(Guid.NewGuid(), null);
        Assert.Single(existingErrors);
        Assert.Equal("not_found", existingErrors[0].Code);
    }

  [Fact]
  public void AddApplication_RegistersCreateQuestionValidatorAsScoped()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        var registration = Assert.Single(services, service => service.ServiceType == typeof(ICreateQuestionValidator));
        Assert.Equal(typeof(CreateQuestionValidator), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);

    }
      // Kiểm tra interface được đăng ký đúng class và đúng lifetime.
  [Fact]
  public void AddInfrastructure_RegistersQuestionQueryServiceAsScoped()
  {
      // Arrange: AddInfrastructure yêu cầu có DefaultConnection.
      // Đây chỉ là cấu hình phục vụ test, không kết nối database.
      Dictionary<string, string?> settings =
          new Dictionary<string, string?>();

      settings.Add(
          "ConnectionStrings:DefaultConnection",
          "Server=localhost;Database=QuestionBankDiTests;Trusted_Connection=True;");

      IConfiguration configuration = new ConfigurationBuilder()
          .AddInMemoryCollection(settings)
          .Build();

      ServiceCollection services = new ServiceCollection();

      // Act: gọi đúng hàm đăng ký Infrastructure của dự án.
      services.AddInfrastructure(configuration);

      // Assert.Single yêu cầu có đúng một đăng ký cho interface này.
      // Test sẽ fail nếu chưa đăng ký hoặc đăng ký trùng.
      ServiceDescriptor registration = Assert.Single(
          services,
          item => item.ServiceType == typeof(IQuestionQueryService));

      // Interface phải được ánh xạ tới đúng class xử lý.
      Assert.Equal(
          typeof(QuestionQueryService),
          registration.ImplementationType);

      // Service dùng DbContext nên phải có lifetime Scoped.
      Assert.Equal(
          ServiceLifetime.Scoped,
          registration.Lifetime);
  }
}
