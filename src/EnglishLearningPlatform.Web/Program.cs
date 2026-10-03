using EnglishLearningPlatform.Application;
using EnglishLearningPlatform.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("LearningDemo:Enabled"))
{
    using var scope = app.Services.CreateScope();
    await EnglishLearningPlatform.Infrastructure.Learning.LearningDemoSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<EnglishLearningPlatform.Infrastructure.Persistence.AppDbContext>(),
        scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<EnglishLearningPlatform.Infrastructure.Identity.ApplicationUser>>(),
        builder.Configuration["LearningDemo:Password"] ?? "");
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
