using System.Linq;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class IntegrationTestFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly DatabaseFixture _database = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var dbContextDescriptor = services.SingleOrDefault(
                service =>
                    service.ServiceType ==
                    typeof(IDbContextOptionsConfiguration<AppDbContext>));

            if (dbContextDescriptor is not null)
            {
                services.Remove(dbContextDescriptor);
            }

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlServer(_database.ConnectionString);
            });
        });
    }

    public async Task InitializeAsync()
    {
        // 1. Start SQL Server test container.
        await _database.InitializeAsync();

        // 2. Starting to access Services boots the ASP.NET Core test app.
        using var scope = Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        // 3. Apply InitialCreate, SeedIdentityRoles, etc.
        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        // Dispose ASP.NET Core before destroying its database.
        await base.DisposeAsync();

        await _database.DisposeAsync();
    }
}