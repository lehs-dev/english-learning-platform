using EnglishLearningPlatform.Application;
using EnglishLearningPlatform.Application.QuestionBank;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.QuestionBank;
public sealed class QuestionBankDependencyInjectionTests
{
  [Fact]
  public void AddApplication_RegistersCreateQuestionValidatorAsScoped()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        var registration = Assert.Single(services.Where(service => service.ServiceType == typeof(ICreateQuestionValidator)));
        Assert.Equal(typeof(CreateQuestionValidator), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);

    }
}