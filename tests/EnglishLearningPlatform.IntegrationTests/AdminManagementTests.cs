using System.Net;
using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class AdminManagementTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Teacher)]
    [InlineData(AppRoles.Admin)]
    public async Task CreateUser_MvcAssignsExactlyRequestedRole_HashesPassword_AndAuditsOnce(string role)
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        using var csrf = await client.PostAsync("/Admin/CreateUser", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        var token = await IdentityTestHelpers.GetTokenAsync(client, "/Admin/CreateUser");
        var email = $"managed-{Guid.NewGuid():N}@example.test";
        const string password = "TestOnly_Admin123!";
        var form = new Dictionary<string,string> { ["__RequestVerificationToken"] = token,
            ["FullName"] = "Managed user", ["Email"] = email, ["Password"] = password, ["Role"] = role };
        using var response = await client.PostAsync("/Admin/CreateUser", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var target = await users.FindByEmailAsync(email);
        Assert.NotNull(target);
        Assert.Equal(new[] {role}, await users.GetRolesAsync(target));
        Assert.True(await users.CheckPasswordAsync(target,password));
        using var duplicate = await client.PostAsync("/Admin/CreateUser", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.DoesNotContain(password, await duplicate.Content.ReadAsStringAsync());
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = Assert.Single(await db.AuditLogs.Where(a=>a.TargetUserId==target.Id).ToListAsync());
        Assert.Equal("CreateUser",audit.Action);
        Assert.DoesNotContain(password,audit.AfterData!);
        Assert.DoesNotContain(target.PasswordHash!,audit.AfterData!);
        Assert.Equal(1,await db.Users.CountAsync(u=>u.Email==email));
    }

    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Teacher)]
    public async Task CreateUser_NonAdminCannotCreateAnyRole(string role)
    {
        var actor=await IdentityTestHelpers.CreateUserAsync(factory,role);
        using var scope=factory.Services.CreateScope();
        var service=scope.ServiceProvider.GetRequiredService<IAdminManagementService>();
        Assert.False((await service.CreateUserAsync(actor.Id,"Forbidden user",$"denied-{Guid.NewGuid():N}@example.test","TestOnly_Admin123!",AppRoles.Admin)).Success);
        using var client=IdentityTestHelpers.CreateClient(factory);
        using var login=await IdentityTestHelpers.LoginAsync(client,actor);
        using var response=await client.GetAsync("/Admin/CreateUser");
        Assert.Contains("AccessDenied",response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task CreateUser_RejectsUnknownRoleWithoutPersistingUser()
    {
        var admin=await IdentityTestHelpers.CreateUserAsync(factory,AppRoles.Admin);
        using var scope=factory.Services.CreateScope();
        var service=scope.ServiceProvider.GetRequiredService<IAdminManagementService>();
        var email=$"bad-role-{Guid.NewGuid():N}@example.test";
        Assert.False((await service.CreateUserAsync(admin.Id,"Invalid role",email,"TestOnly_Admin123!","Guest")).Success);
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(u=>u.Email==email));
        using var client=IdentityTestHelpers.CreateClient(factory);
        using var login=await IdentityTestHelpers.LoginAsync(client,admin);
        var token=await IdentityTestHelpers.GetTokenAsync(client,"/Admin/CreateUser");
        using var response=await client.PostAsync("/Admin/CreateUser",new FormUrlEncodedContent(new Dictionary<string,string>{
            ["__RequestVerificationToken"]=token,["FullName"]="Invalid form",["Email"]=email,["Role"]="Guest",["Password"]="short"}));
        var html=await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Contains("validation-summary-errors",html);
        Assert.DoesNotContain("value=\"short\"",html);
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(u=>u.Email==email));
    }

    [Fact]
    public async Task CreateTeacher_HashesPassword_AssignsOnlyTeacher_AuditsAndRejectsDuplicate()
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        var email = $"teacher-{Guid.NewGuid():N}@example.test";
        const string password = "TestOnly_Admin123!";
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminManagementService>();
        Assert.True((await service.CreateTeacherAsync(admin.Id, "New teacher", email, password)).Success);
        Assert.False((await service.CreateTeacherAsync(admin.Id, "Duplicate", email, password)).Success);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var teacher = await users.FindByEmailAsync(email);
        Assert.NotNull(teacher);
        Assert.True(await users.CheckPasswordAsync(teacher, password));
        Assert.Equal(new[] { AppRoles.Teacher }, await users.GetRolesAsync(teacher));
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLogs.SingleAsync(a => a.TargetUserId == teacher.Id);
        Assert.Equal("CreateTeacher", audit.Action);
        Assert.DoesNotContain(password, audit.AfterData!);
        Assert.DoesNotContain(teacher.PasswordHash!, audit.AfterData!);
    }

    [Fact]
    public async Task ChangeRoleAndLock_InvalidateExistingCookie_UnlockClearsIdentityLockout()
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminManagementService>();
        Assert.True((await service.UpdateUserAsync(admin.Id, student.Id, AppRoles.Teacher, AccountStatus.Locked)).Success);
        using var denied = await client.GetAsync("/Profile");
        Assert.Contains("/Account/Login", denied.Headers.Location!.ToString());
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var target = await db.Users.SingleAsync(u => u.Id == student.Id);
        target.LockoutEnd = DateTimeOffset.UtcNow.AddDays(1); target.AccessFailedCount = 5;
        await db.SaveChangesAsync();
        Assert.True((await service.UpdateUserAsync(admin.Id, student.Id, AppRoles.Teacher, AccountStatus.Active)).Success);
        Assert.Null(target.LockoutEnd); Assert.Equal(0, target.AccessFailedCount);
        target.LockoutEnd = DateTimeOffset.UtcNow.AddDays(1); target.AccessFailedCount = 5;
        await db.SaveChangesAsync();
        Assert.True((await service.UpdateUserAsync(admin.Id, student.Id, AppRoles.Teacher, AccountStatus.Active)).Success);
        Assert.Null(target.LockoutEnd); Assert.Equal(0, target.AccessFailedCount);
        Assert.True((await service.UpdateUserAsync(admin.Id, student.Id, AppRoles.Teacher, AccountStatus.Disabled)).Success);
        Assert.Equal(4, await db.AuditLogs.CountAsync(a => a.TargetUserId == student.Id));
        Assert.Equal(new[] { AppRoles.Teacher }, await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().GetRolesAsync(target));
    }

    [Fact]
    public async Task ConcurrentAdminDemotions_LeaveOneActiveAdmin_AndOnlyAuditSuccessfulWrite()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.ExecuteUpdateAsync(s => s.SetProperty(u => u.AccountStatus, AccountStatus.Disabled));
        }
        var first = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        var second = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        async Task<AdminChangeResult> Demote(Guid actor)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IAdminManagementService>()
                .UpdateUserAsync(actor, actor, AppRoles.Student, AccountStatus.Active);
        }
        var results = await Task.WhenAll(Demote(first.Id), Demote(second.Id));
        Assert.Single(results, r => r.Success);
        using var check = factory.Services.CreateScope();
        var dbCheck = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await dbCheck.UserRoles.CountAsync(ur => (ur.UserId == first.Id || ur.UserId == second.Id) &&
            dbCheck.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Admin)));
        Assert.Equal(1, await dbCheck.AuditLogs.CountAsync(a => a.TargetUserId == first.Id || a.TargetUserId == second.Id));
        var remaining = results[0].Success ? second : first;
        var service = check.ServiceProvider.GetRequiredService<IAdminManagementService>();
        Assert.False((await service.UpdateUserAsync(remaining.Id, remaining.Id, AppRoles.Admin, AccountStatus.Locked)).Success);
        Assert.False((await service.UpdateUserAsync(remaining.Id, remaining.Id, AppRoles.Admin, AccountStatus.Disabled)).Success);
    }

    [Fact]
    public async Task Controller_RequiresAdminAndCsrf_ValidatesFormAndSupportsRoleFilter()
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        using var csrf = await client.PostAsync("/Admin/UpdateUser", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        var token = await IdentityTestHelpers.GetTokenAsync(client, "/Admin/CreateTeacher");
        var email = $"ui-teacher-{Guid.NewGuid():N}@example.test";
        using var created = await client.PostAsync("/Admin/CreateTeacher", new FormUrlEncodedContent(new Dictionary<string,string>
        { ["__RequestVerificationToken"] = token, ["FullName"] = "UI teacher", ["Email"] = email, ["Password"] = "TestOnly_Admin123!" }));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var html = await client.GetStringAsync($"/Admin/Users?search={email}&role=Teacher");
        Assert.Contains(email, html);
        Assert.DoesNotContain($"<td>{email}</td>", await client.GetStringAsync($"/Admin/Users?search={email}&role=Student"));
        Assert.Contains(email, await client.GetStringAsync($"/Admin/Audit?search={email}"));
        using var other = IdentityTestHelpers.CreateClient(factory);
        using var otherLogin = await IdentityTestHelpers.LoginAsync(other, student);
        using var forbidden = await other.GetAsync("/Admin/Audit");
        Assert.Contains("AccessDenied", forbidden.Headers.Location!.ToString());
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminManagementService>();
        Assert.False((await service.CreateTeacherAsync(student.Id, "No access", "no@example.test", "TestOnly_Admin123!")).Success);
        Assert.False((await service.UpdateUserAsync(student.Id, admin.Id, AppRoles.Student, AccountStatus.Active)).Success);
        Assert.Null(await service.AuditAsync(student.Id, null, 1));
    }
}
