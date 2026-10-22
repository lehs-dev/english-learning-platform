using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Identity;

public sealed class ActiveUserValidatorTests
{
    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Teacher)]
    [InlineData(AppRoles.Admin)]
    public async Task ActiveUserWithExactlyTheRequestedRole_IsAccepted(string role)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        using var users = new StubUserManager(user, role);
        var validator = new ActiveUserValidator(users);

        Assert.True(await validator.IsActiveRoleAsync(user.Id, role));
        Assert.Equal(role == AppRoles.Student, await validator.IsActiveStudentAsync(user.Id));
        Assert.Equal(role == AppRoles.Teacher, await validator.IsActiveTeacherAsync(user.Id));
        Assert.Equal(role == AppRoles.Admin, await validator.IsActiveAdminAsync(user.Id));
    }

    [Theory]
    [InlineData(AccountStatus.Locked)]
    [InlineData(AccountStatus.Disabled)]
    public async Task InactiveUser_IsDenied(AccountStatus status)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), AccountStatus = status };
        using var users = new StubUserManager(user, AppRoles.Student);

        Assert.False(await new ActiveUserValidator(users).IsActiveStudentAsync(user.Id));
        Assert.Equal(0, users.RoleReads);
    }

    [Theory]
    [InlineData()]
    [InlineData(AppRoles.Teacher)]
    [InlineData(AppRoles.Student, AppRoles.Teacher)]
    [InlineData(AppRoles.Student, AppRoles.Student)]
    [InlineData(AppRoles.Student, "Guest")]
    public async Task MissingWrongOrMultipleRoles_AreDenied(params string[] roles)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        using var users = new StubUserManager(user, roles);

        Assert.False(await new ActiveUserValidator(users).IsActiveStudentAsync(user.Id));
    }

    [Fact]
    public async Task MissingUser_IsDenied()
    {
        using var users = new StubUserManager(null, AppRoles.Student);

        Assert.False(await new ActiveUserValidator(users).IsActiveStudentAsync(Guid.NewGuid()));
        Assert.Equal(0, users.RoleReads);
    }

    [Fact]
    public async Task EmptyIdAndUnknownRequestedRole_AreDeniedBeforeIdentityReads()
    {
        using var users = new StubUserManager(null);
        var validator = new ActiveUserValidator(users);

        Assert.False(await validator.IsActiveStudentAsync(Guid.Empty));
        Assert.False(await validator.IsActiveRoleAsync(Guid.NewGuid(), "Guest"));
        Assert.Equal(0, users.UserReads);
    }

    [Fact]
    public async Task CancellationBeforeIdentityRead_IsPropagated()
    {
        using var users = new StubUserManager(null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ActiveUserValidator(users).IsActiveStudentAsync(Guid.NewGuid(), cancellation.Token));
        Assert.Equal(0, users.UserReads);
    }

    [Fact]
    public async Task CancellationDuringIdentityRead_PreventsRoleRead()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        using var cancellation = new CancellationTokenSource();
        using var users = new StubUserManager(user, AppRoles.Student) { AfterUserRead = cancellation.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ActiveUserValidator(users).IsActiveStudentAsync(user.Id, cancellation.Token));
        Assert.Equal(0, users.RoleReads);
    }

    private sealed class StubUserManager(ApplicationUser? user, params string[] roles)
        : UserManager<ApplicationUser>(
            new UserStore<ApplicationUser, IdentityRole<Guid>, AppDbContext, Guid>(
                new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options)),
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(), [], [],
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!,
            NullLogger<UserManager<ApplicationUser>>.Instance)
    {
        public override bool SupportsQueryableUsers => false;
        public int UserReads { get; private set; }
        public int RoleReads { get; private set; }
        public Action? AfterUserRead { get; init; }

        public override Task<ApplicationUser?> FindByIdAsync(string userId)
        {
            UserReads++;
            AfterUserRead?.Invoke();
            return Task.FromResult(user?.Id.ToString() == userId ? user : null);
        }

        public override Task<IList<string>> GetRolesAsync(ApplicationUser applicationUser)
        {
            RoleReads++;
            return Task.FromResult<IList<string>>(roles);
        }
    }
}
