using EnglishLearningPlatform.Application;
using EnglishLearningPlatform.Infrastructure;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using EnglishLearningPlatform.Web.Commerce;
using EnglishLearningPlatform.Web.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IPaymentWebhookProcessor, PaymentWebhookProcessor>();
builder.Services.AddSingleton<LessonMediaStore>();

var app = builder.Build();

// Development upgrades its local database before queries or demo seeding use the model.
// Production still requires the explicit deployment mode; it never migrates implicitly.
var migrateOnly = args.Contains("--migrate", StringComparer.Ordinal);
var seedDemoOnly = args.Contains("--seed-demo", StringComparer.Ordinal);
if (seedDemoOnly && !app.Environment.IsDevelopment())
    throw new InvalidOperationException("--seed-demo requires the Development environment.");
if (seedDemoOnly && migrateOnly)
    throw new InvalidOperationException("Use --seed-demo or --migrate separately.");
var isDevelopment = app.Environment.IsDevelopment();
var runDemoSeed = isDevelopment && !migrateOnly &&
    (seedDemoOnly || builder.Configuration.GetValue<bool>("LearningDemo:Enabled"));
var demoPassword = builder.Configuration["LearningDemo:Password"];
if (runDemoSeed && string.IsNullOrWhiteSpace(demoPassword))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    if (!await db.Database.CanConnectAsync() || !(await db.Database.GetAppliedMigrationsAsync()).Any() ||
        !await LearningDemoSeeder.HasAllAccountsAsync(users))
    {
        app.Logger.LogError("{Reason}", LearningDemoSeeder.PasswordRequiredMessage);
        Environment.ExitCode = 1;
        return;
    }
}

var migrateOnStartup = isDevelopment &&
    builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup");
if (migrateOnly || migrateOnStartup || seedDemoOnly)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    if (migrateOnly) return;
}

if (runDemoSeed)
{
    using var scope = app.Services.CreateScope();
    await LearningDemoSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<AppDbContext>(),
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
        demoPassword,
        scope.ServiceProvider.GetRequiredService<EnglishLearningPlatform.Application.Stimuli.IStimulusAudioStore>());
}

if (seedDemoOnly)
{
    app.Logger.LogInformation("Demo seed completed.");
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program;
