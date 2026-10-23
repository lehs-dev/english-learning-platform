using System.Data.Common;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure;
using EnglishLearningPlatform.Infrastructure.Persistence;
using EnglishLearningPlatform.Infrastructure.Practices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Practice;

public sealed class TeacherPracticeQueryServiceContractTests
{
    [Fact]
    public void Constructor_NullDb_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TeacherPracticeQueryService(null!));

        Assert.Equal("db", exception.ParamName);
    }

    [Fact]
    public async Task SearchAsync_NullRequest_ThrowsBeforeDatabaseAccess()
    {
        using var db = CreateDbWithoutConnecting();
        var service = new TeacherPracticeQueryService(db);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.SearchAsync(Guid.NewGuid(), null!));

        Assert.Equal("request", exception.ParamName);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SearchAsync_CanceledToken_ThrowsBeforeDatabaseAccess()
    {
        using var db = CreateDbWithoutConnecting();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new TeacherPracticeQueryService(db);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SearchAsync(Guid.NewGuid(),
                new TeacherPracticeSearchRequest(), cancellation.Token));

        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchAsync_EmptyActor_DeniesBeforeDatabaseAccessOrFilterValidation(
        bool invalidFilters)
    {
        using var db = CreateDbWithoutConnecting();
        var service = new TeacherPracticeQueryService(db);
        var request = new TeacherPracticeSearchRequest();
        if (invalidFilters)
        {
            request.PageNumber = 0;
            request.PageSize = 101;
            request.Status = (PracticeStatus)999;
            request.CourseId = Guid.Empty;
        }

        var outcome = await service.SearchAsync(Guid.Empty, request);

        Assert.Equal(LearningAccessResult.Forbidden, outcome.Access);
        Assert.Null(outcome.Value);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void AddInfrastructure_RegistersQueryServiceOnceAsScoped()
    {
        // Chỉ kiểm tra đăng ký, không sử dụng connection này để truy vấn SQL.
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] =
                "Server=localhost;Database=PracticeQueryDiTests;Trusted_Connection=True;"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);

        var registration = Assert.Single(services,
            item => item.ServiceType == typeof(ITeacherPracticeQueryService));
        Assert.Equal(typeof(TeacherPracticeQueryService), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
    }

    private static AppDbContext CreateDbWithoutConnecting()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=PracticeQueryContractTests;Trusted_Connection=True;")
            .AddInterceptors(new RejectDatabaseConnectionInterceptor())
            .Options;
        return new AppDbContext(options);
    }

    private sealed class RejectDatabaseConnectionInterceptor : DbConnectionInterceptor
    {
        // Nếu các hàng rào đầu hàm bị bỏ, test phải fail ngay, không thử kết nối SQL.
        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            throw new InvalidOperationException("Test này không được mở kết nối database.");
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Test này không được mở kết nối database.");
        }
    }
}
