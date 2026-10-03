using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class HealthCheckTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public HealthCheckTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Home_ReturnsSuccess()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/");
        response.EnsureSuccessStatusCode();
    }
}
