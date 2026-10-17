using System.Net;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Every test owns a GUID database; startup must never touch the developer's database.
public sealed class DatabaseStartupTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261008035103_AddCheckoutOrderMetadata";
    private const string LockMigration = "20261015181741_AddQuestionAndStimulusContentLocks";
    private readonly DatabaseFixture database = new();

    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(database.ConnectionString).Options);

    [Fact]
    public async Task Development_FreshDatabase_IsMigratedBeforeServingRequests()
    {
        await using var factory = new StartupFactory(database.ConnectionString, "Development");
        using var client = factory.CreateClient();
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        await using var db = CreateContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Contains(LockMigration, await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Questions.Select(q => q.ContentLockedAtUtc).ToListAsync());
        Assert.Empty(await db.Stimuli.Select(s => s.ContentLockedAtUtc).ToListAsync());
    }

    [Fact]
    public async Task Development_UpgradesOldDatabaseAndRepeatedStartupPreservesGrades()
    {
        await using var db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var teacher = new ApplicationUser { Id = Guid.NewGuid(), UserName = "startup-teacher" };
        var student = new ApplicationUser { Id = Guid.NewGuid(), UserName = "startup-student" };
        db.Users.AddRange(teacher, student);
        var startedAt = new DateTimeOffset(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);
        var attempt = new Attempt
        {
            StudentUserId = student.Id,
            Assessment = new Assessment { OwnerTeacherUserId = teacher.Id, Title = "Existing result",
                DurationMinutes = 20, MaxAttempts = 3, AssessmentType = AssessmentType.PlacementTest },
            Status = AttemptStatus.Graded, OverallScore = 80m,
            StartedAtUtc = startedAt, DeadlineUtc = startedAt.AddMinutes(20),
            FinalizedAtUtc = startedAt.AddMinutes(10), FinalizationReason = AttemptFinalizationReason.ManualSubmit
        };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();

        for (var run = 0; run < 2; run++)
        {
            await using var factory = new StartupFactory(database.ConnectionString, "Development");
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        }

        db.ChangeTracker.Clear();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Empty(await db.Questions.Select(q => q.ContentLockedAtUtc).ToListAsync());
        Assert.Empty(await db.Stimuli.Select(s => s.ContentLockedAtUtc).ToListAsync());
        var saved = await db.Attempts.SingleAsync();
        Assert.Equal(attempt.Id, saved.Id);
        Assert.Equal(AttemptStatus.Graded, saved.Status);
        Assert.Equal(80m, saved.OverallScore);
        Assert.Equal(attempt.DeadlineUtc, saved.DeadlineUtc);
        Assert.Equal(attempt.FinalizedAtUtc, saved.FinalizedAtUtc);
        Assert.Equal(AttemptFinalizationReason.ManualSubmit, saved.FinalizationReason);
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public async Task ProductionOrDisabledSetting_LeavesSchemaForExplicitMigration(string environment, bool enabled)
    {
        await using var db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var appliedBefore = (await db.Database.GetAppliedMigrationsAsync()).ToArray();

        await using var factory = new StartupFactory(database.ConnectionString, environment, enabled);
        using var client = factory.CreateClient();
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(appliedBefore, await db.Database.GetAppliedMigrationsAsync());
        Assert.Contains(LockMigration, await db.Database.GetPendingMigrationsAsync());
    }

    private sealed class StartupFactory(string connectionString, string environment, bool? enabled = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var settings = new Dictionary<string, string?> { ["LearningDemo:Enabled"] = "false" };
                if (enabled.HasValue)
                    settings["Database:ApplyMigrationsOnStartup"] = enabled.Value.ToString();
                configuration.AddInMemoryCollection(settings);
            });
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(s =>
                    s.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>));
                services.Remove(descriptor);
                services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
            });
        }
    }
}
