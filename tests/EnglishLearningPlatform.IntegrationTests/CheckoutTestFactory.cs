using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Persistence;
using EnglishLearningPlatform.Infrastructure.Commerce;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// All writes/migrations happen only in DatabaseFixture's isolated GUID database.
public sealed class CheckoutTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly DatabaseFixture _database = new();
    public EnrollmentFailureInterceptor Faults { get; } = new();
    public CheckoutTransactionFaults TransactionFaults { get; } = new();
    public CheckoutLogCapture Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CheckoutSandbox:Enabled"] = "true", ["LearningDemo:Enabled"] = "false"
        }));
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(s => s.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>));
            if (descriptor is not null) services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(options => options.UseSqlServer(_database.ConnectionString).AddInterceptors(Faults));
            services.Replace(ServiceDescriptor.Scoped<ICheckoutTransactionFactory>(provider =>
                new FaultingCheckoutTransactionFactory(new CheckoutTransactionFactory(provider.GetRequiredService<AppDbContext>()), TransactionFaults)));
            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }
}

public sealed class EnrollmentFailureInterceptor : SaveChangesInterceptor
{
    public bool FailEnrollment { get; set; }
    public bool SawSavedPayment { get; private set; }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (FailEnrollment && eventData.Context is AppDbContext db &&
            db.ChangeTracker.Entries<Enrollment>().Any(e => e.State == EntityState.Added))
        {
            var paymentId = db.ChangeTracker.Entries<Enrollment>().First(e => e.State == EntityState.Added).Entity.PaymentId;
            SawSavedPayment = await db.Payments.AsNoTracking().AnyAsync(p => p.Id == paymentId, cancellationToken);
            throw new DbUpdateException("Injected failure after Payment save, before Enrollment save.");
        }
        return result;
    }
}
