using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Infrastructure.Persistence;
using EnglishLearningPlatform.Infrastructure.Practices;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Practice;

public sealed class TeacherPracticeServiceContractTests
{
    [Fact]
    public void Constructor_NullDb_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TeacherPracticeService(null!, new CreatePracticeValidator()));

        Assert.Equal("db", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullValidator_ThrowsArgumentNullException()
    {
        using var db = CreateDbWithoutConnecting();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TeacherPracticeService(db, null!));

        Assert.Equal("validator", exception.ParamName);
    }

    [Fact]
    public async Task CreateDraftAsync_NullRequest_ThrowsBeforeDatabaseAccess()
    {
        using var db = CreateDbWithoutConnecting();
        var service = new TeacherPracticeService(db, new CreatePracticeValidator());

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.CreateDraftAsync(Guid.NewGuid(), null!));

        Assert.Equal("request", exception.ParamName);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task CreateDraftAsync_CanceledToken_ThrowsBeforeDatabaseAccess()
    {
        using var db = CreateDbWithoutConnecting();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new TeacherPracticeService(db, new CreatePracticeValidator());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CreateDraftAsync(Guid.NewGuid(),
                new CreatePracticeRequest { Title = "Grammar practice" }, cancellation.Token));

        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static AppDbContext CreateDbWithoutConnecting()
    {
        // Các test này phải kết thúc trước mọi truy vấn SQL.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=PracticeContractTests;Trusted_Connection=True;")
            .Options;
        return new AppDbContext(options);
    }
}
