using EnglishLearningPlatform.Application;
using EnglishLearningPlatform.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Development upgrades its local database before queries or demo seeding use the model.
// Production still requires the explicit deployment mode; it never migrates implicitly.
var migrateOnly = args.Contains("--migrate", StringComparer.Ordinal);
var seedDemoOnly = args.Contains("--seed-demo", StringComparer.Ordinal);
if (seedDemoOnly && !app.Environment.IsDevelopment())
    throw new InvalidOperationException("--seed-demo requires the Development environment.");
if (seedDemoOnly && migrateOnly)
    throw new InvalidOperationException("Use --seed-demo or --migrate separately.");
var migrateOnStartup = app.Environment.IsDevelopment() &&
    builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup");
if (migrateOnly || migrateOnStartup || seedDemoOnly)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EnglishLearningPlatform.Infrastructure.Persistence.AppDbContext>();
    await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync(db.Database);
    if (migrateOnly) return;
}

if (app.Environment.IsDevelopment() &&
    (seedDemoOnly || builder.Configuration.GetValue<bool>("LearningDemo:Enabled")))
{
    using var scope = app.Services.CreateScope();
    await EnglishLearningPlatform.Infrastructure.Learning.LearningDemoSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<EnglishLearningPlatform.Infrastructure.Persistence.AppDbContext>(),
        scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<EnglishLearningPlatform.Infrastructure.Identity.ApplicationUser>>(),
        builder.Configuration["LearningDemo:Password"] ?? "",
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
