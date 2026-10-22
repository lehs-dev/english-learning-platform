using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Learning.Access;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Learning;

public sealed class LearningAccessServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(AppRoles.Admin)]
    [InlineData("Guest")]
    public async Task UnsupportedOrInactiveRole_IsDeniedBeforeResourceLookup(string? role)
    {
        using var db = Context();
        var resource = new ResourceReader(LearningResourceType.Course, null);
        var service = Service(db, role, resource);

        Assert.Equal(LearningAccessResult.Forbidden, await service.CheckAccessAsync(Guid.NewGuid(),
            LearningResourceType.Course, Guid.NewGuid(), LearningOperation.ViewContent));
        Assert.Equal(0, resource.Reads);
    }

    [Theory]
    [InlineData(LearningOperation.ViewContent, LearningAccessResult.Allowed)]
    [InlineData(LearningOperation.ManageContent, LearningAccessResult.Allowed)]
    [InlineData(LearningOperation.RecordProgress, LearningAccessResult.Forbidden)]
    public async Task TeacherOwner_UsesCurrentOwnershipAndAllowedOperations(
        LearningOperation operation, LearningAccessResult expected)
    {
        using var db = Context();
        var owner = Guid.NewGuid();
        var resource = new ResourceReader(LearningResourceType.Lesson,
            new(Guid.NewGuid(), owner, CourseStatus.Draft, false));
        using var cancellation = new CancellationTokenSource();
        var resourceId = Guid.NewGuid();

        var result = await Service(db, AppRoles.Teacher, resource).CheckAccessAsync(owner,
            LearningResourceType.Lesson, resourceId, operation, cancellation.Token);

        Assert.Equal(expected, result);
        Assert.Equal((resourceId, cancellation.Token), resource.LastRead);
    }

    [Fact]
    public async Task TeacherCannotReadOrEditAnotherOwnersResource()
    {
        using var db = Context();
        var resource = new ResourceReader(LearningResourceType.Course, new(Guid.NewGuid(), Guid.NewGuid(), CourseStatus.Published, true));
        var service = Service(db, AppRoles.Teacher, resource);

        Assert.Equal(LearningAccessResult.Forbidden, await service.CheckAccessAsync(Guid.NewGuid(),
            LearningResourceType.Course, Guid.NewGuid(), LearningOperation.ManageContent));
    }

    [Fact]
    public async Task MissingResourceOrReader_ReturnsNotFound()
    {
        using var db = Context();
        var service = Service(db, AppRoles.Teacher, new ResourceReader(LearningResourceType.Course, null));

        Assert.Equal(LearningAccessResult.NotFound, await service.CheckAccessAsync(Guid.NewGuid(),
            LearningResourceType.Course, Guid.NewGuid(), LearningOperation.ViewContent));
        Assert.Equal(LearningAccessResult.NotFound, await service.CheckAccessAsync(Guid.NewGuid(),
            (LearningResourceType)99, Guid.NewGuid(), LearningOperation.ViewContent));
    }

    [Theory]
    [InlineData(CourseStatus.Draft, true, LearningOperation.ViewContent)]
    [InlineData(CourseStatus.Published, false, LearningOperation.ViewContent)]
    [InlineData(CourseStatus.Archived, true, LearningOperation.RecordProgress)]
    [InlineData(CourseStatus.Published, true, LearningOperation.ManageContent)]
    public async Task UnavailableOrReadOnlyResources_RejectStudentsBeforeEnrollmentQuery(
        CourseStatus status, bool visible, LearningOperation operation)
    {
        using var db = Context();
        var resource = new ResourceReader(LearningResourceType.Lesson, new(Guid.NewGuid(), Guid.NewGuid(), status, visible));

        Assert.Equal(LearningAccessResult.Forbidden, await Service(db, AppRoles.Student, resource)
            .CheckAccessAsync(Guid.NewGuid(), LearningResourceType.Lesson, Guid.NewGuid(), operation));
    }

    [Fact]
    public async Task AdditionalResourceReader_ExtendsLookupWithoutChangingPermissionService()
    {
        using var db = Context();
        var owner = Guid.NewGuid();
        var resourceType = (LearningResourceType)99;
        var resource = new ResourceReader(resourceType, new(Guid.NewGuid(), owner, CourseStatus.Published, true));

        Assert.Equal(LearningAccessResult.Allowed, await Service(db, AppRoles.Teacher, resource)
            .CheckAccessAsync(owner, resourceType, Guid.NewGuid(), LearningOperation.ManageContent));
    }

    [Fact]
    public void DuplicateResourceReaders_FailAtComposition()
    {
        Assert.Throws<ArgumentException>(() => new LearningResourceAccessReaderRegistry([
            new ResourceReader(LearningResourceType.Course, null), new ResourceReader(LearningResourceType.Course, null)]));
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>().Options);

    private static LearningAccessService Service(AppDbContext db, string? role, params ILearningResourceAccessReader[] readers) =>
        new(db, new RoleReader(role), new LearningResourceAccessReaderRegistry(readers));

    private sealed class RoleReader(string? role) : IActiveUserRoleReader
    {
        public Task<string?> GetActiveRoleAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(role);
    }

    private sealed class ResourceReader(LearningResourceType resourceType, LearningResourceAccessInfo? resource) : ILearningResourceAccessReader
    {
        public LearningResourceType ResourceType => resourceType;
        public int Reads { get; private set; }
        public (Guid, CancellationToken) LastRead { get; private set; }

        public Task<LearningResourceAccessInfo?> ReadAsync(Guid resourceId, CancellationToken ct = default)
        {
            Reads++;
            LastRead = (resourceId, ct);
            return Task.FromResult(resource);
        }
    }
}
