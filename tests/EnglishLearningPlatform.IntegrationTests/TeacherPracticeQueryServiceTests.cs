using System.Data.SqlTypes;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using EnglishLearningPlatform.Infrastructure.Practices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Fixture tạo SQL container/database test riêng và tự dọn sau khi chạy.
// Mỗi test tạo owner riêng, không xóa dữ liệu của test khác hoặc database demo.
public sealed class TeacherPracticeQueryServiceTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    private static readonly DateTimeOffset CreatedAt =
        new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    public TeacherPracticeQueryServiceTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void ApplicationScope_ResolvesRealQueryServiceWithScopedLifetime()
    {
        using var firstScope = _factory.Services.CreateScope();
        using var secondScope = _factory.Services.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<ITeacherPracticeQueryService>();

        Assert.IsType<TeacherPracticeQueryService>(first);
        Assert.Same(first,
            firstScope.ServiceProvider.GetRequiredService<ITeacherPracticeQueryService>());
        Assert.NotSame(first,
            secondScope.ServiceProvider.GetRequiredService<ITeacherPracticeQueryService>());
    }

    [Fact]
    public async Task SearchAsync_ActiveTeacherWithoutPractices_ReturnsAllowedEmptyResult()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);

        var result = Allowed(await SearchAsync(teacher.Id));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(10, result.PageSize);
    }

    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Admin)]
    public async Task SearchAsync_NonTeacher_ReturnsForbiddenWithoutData(string role)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(_factory, role);

        Denied(await SearchAsync(user.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchAsync_EmptyOrUnknownActor_ReturnsForbiddenWithoutData(bool empty)
    {
        Denied(await SearchAsync(empty ? Guid.Empty : Guid.NewGuid()));
    }

    [Theory]
    [InlineData(AccountStatus.Locked)]
    [InlineData(AccountStatus.Disabled)]
    public async Task SearchAsync_InactiveTeacherWithOwnedPractice_ReturnsForbidden(
        AccountStatus status)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        await AddPracticeAsync(teacher.Id, "Private draft");
        await ChangeStatusAsync(teacher.Id, status);

        Denied(await SearchAsync(teacher.Id));
    }

    [Fact]
    public async Task SearchAsync_UserWithoutRole_ReturnsForbidden()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        await ChangeRoleAsync(teacher.Id, null);

        Denied(await SearchAsync(teacher.Id));
    }

    [Theory]
    [InlineData(AccountStatus.Locked)]
    [InlineData(AccountStatus.Disabled)]
    public async Task SearchAsync_StatusChangedAfterSuccessfulRead_RejectsSameService(
        AccountStatus status)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        await AddPracticeAsync(teacher.Id, "Private draft");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ITeacherPracticeQueryService>();
        // Cố tình giữ bản user Active trong ChangeTracker để phát hiện việc dùng quyền cũ.
        await db.Users.SingleAsync(user => user.Id == teacher.Id);
        Assert.Single(Allowed(await service.SearchAsync(
            teacher.Id, new TeacherPracticeSearchRequest())).Items);

        await ChangeStatusAsync(teacher.Id, status);

        Denied(await service.SearchAsync(teacher.Id, new TeacherPracticeSearchRequest()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Admin)]
    public async Task SearchAsync_RoleRemovedOrReplaced_RejectsSameService(string? replacement)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        await AddPracticeAsync(teacher.Id, "Private draft");
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITeacherPracticeQueryService>();
        Assert.Single(Allowed(await service.SearchAsync(
            teacher.Id, new TeacherPracticeSearchRequest())).Items);

        // Không thêm role thứ hai, không bỏ unique key một role/user của dự án.
        await ChangeRoleAsync(teacher.Id, replacement);

        Denied(await service.SearchAsync(teacher.Id, new TeacherPracticeSearchRequest()));
    }

    [Fact]
    public async Task SearchAsync_DefaultQuery_ReturnsOnlyOwnedPracticesAcrossStatusesAndParents()
    {
        var data = await SeedAsync();
        var draft = await AddPracticeAsync(data.TeacherId, "Own draft", createdAt: CreatedAt);
        var published = await AddPracticeAsync(data.TeacherId, "Own published",
            lessonId: data.Course.LessonId, status: PracticeStatus.Published,
            createdAt: CreatedAt.AddMinutes(1), questionCount: 2);
        var unpublished = await AddPracticeAsync(data.TeacherId, "Own unpublished",
            moduleId: data.Course.ModuleId, status: PracticeStatus.Unpublished,
            createdAt: CreatedAt.AddMinutes(2), questionCount: 1);
        await AddPracticeAsync(data.OtherTeacherId, "Other draft", createdAt: CreatedAt.AddDays(1));
        await AddPracticeAsync(data.OtherTeacherId, "Other published",
            moduleId: data.OtherCourse.ModuleId, status: PracticeStatus.Published, questionCount: 1);

        var result = Allowed(await SearchAsync(data.TeacherId));

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(new[] { unpublished, published, draft }, result.Items.Select(item => item.Id));
        Assert.Equal(new[] { 1, 2, 0 }, result.Items.Select(item => item.QuestionCount));
        Assert.Equal(new[] { PracticeStatus.Unpublished, PracticeStatus.Published, PracticeStatus.Draft },
            result.Items.Select(item => item.Status));
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Lesson")]
    [InlineData("Module")]
    public async Task SearchAsync_ProjectsMetadataAndDirectParentWithoutInventingSecondParent(
        string parent)
    {
        var data = await SeedAsync();
        var practiceId = await AddPracticeAsync(data.TeacherId, "Mapped practice",
            moduleId: parent == "Module" ? data.Course.ModuleId : null,
            lessonId: parent == "Lesson" ? data.Course.LessonId : null,
            createdAt: CreatedAt, questionCount: 3);

        var result = Allowed(await SearchAsync(data.TeacherId));
        var item = Assert.Single(result.Items);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(practiceId, item.Id);
        Assert.Equal("Mapped practice", item.Title);
        Assert.Equal(PracticeStatus.Draft, item.Status);
        Assert.Equal(3, item.QuestionCount);
        Assert.Equal(CreatedAt, item.CreatedAtUtc);
        Assert.Equal(parent == "None" ? (Guid?)null : data.Course.CourseId, item.CourseId);
        Assert.Equal(parent == "None" ? null : data.Course.CourseTitle, item.CourseTitle);
        Assert.Equal(parent == "Module" ? data.Course.ModuleId : (Guid?)null, item.ModuleId);
        Assert.Equal(parent == "Module" ? data.Course.ModuleTitle : null, item.ModuleTitle);
        Assert.Equal(parent == "Lesson" ? data.Course.LessonId : (Guid?)null, item.LessonId);
        Assert.Equal(parent == "Lesson" ? data.Course.LessonTitle : null, item.LessonTitle);
    }

    [Theory]
    [InlineData("Lesson")]
    [InlineData("Module")]
    public async Task SearchAsync_ParentPointsToAnotherOwnersCourse_DoesNotLeakMetadata(string parent)
    {
        var data = await SeedAsync();
        var visible = await AddPracticeAsync(data.TeacherId, "Own unattached draft");
        // Legacy/dữ liệu không đồng nhất: FK vẫn hợp lệ nhưng owner của Course khác.
        await AddPracticeAsync(data.TeacherId, "Must not expose foreign parent",
            moduleId: parent == "Module" ? data.OtherCourse.ModuleId : null,
            lessonId: parent == "Lesson" ? data.OtherCourse.LessonId : null);

        var result = Allowed(await SearchAsync(data.TeacherId));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(visible, Assert.Single(result.Items).Id);
    }

    [Theory]
    [InlineData("Lesson")]
    [InlineData("Module")]
    public async Task SearchAsync_CourseOwnerChanged_DoesNotReturnFormerParentMetadata(string parent)
    {
        var data = await SeedAsync();
        await AddPracticeAsync(data.TeacherId, "Previously valid parent",
            moduleId: parent == "Module" ? data.Course.ModuleId : null,
            lessonId: parent == "Lesson" ? data.Course.LessonId : null);
        Assert.Single(Allowed(await SearchAsync(data.TeacherId)).Items);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await db.Courses.SingleAsync(course => course.Id == data.Course.CourseId);
            course.OwnerTeacherUserId = data.OtherTeacherId;
            await db.SaveChangesAsync();
        }

        var result = Allowed(await SearchAsync(data.TeacherId));

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
        // Người nhận Course cũng không tự nhiên được quyền xem Practice của owner cũ.
        Assert.Empty(Allowed(await SearchAsync(data.OtherTeacherId)).Items);
    }

    [Theory]
    [InlineData(CourseStatus.Draft)]
    [InlineData(CourseStatus.Published)]
    [InlineData(CourseStatus.Unpublished)]
    [InlineData(CourseStatus.Archived)]
    public async Task SearchAsync_TeacherCanManageDraftUnderHiddenParentsAndAnyCourseStatus(
        CourseStatus status)
    {
        var data = await SeedAsync();
        var practiceId = await AddPracticeAsync(data.TeacherId, "Teacher management draft",
            lessonId: data.Course.LessonId);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await db.Courses.SingleAsync(course => course.Id == data.Course.CourseId);
            var module = await db.Modules.SingleAsync(module => module.Id == data.Course.ModuleId);
            var lesson = await db.Lessons.SingleAsync(lesson => lesson.Id == data.Course.LessonId);
            course.Status = status;
            module.Visibility = ModuleVisibility.Hidden;
            lesson.Status = LessonStatus.Hidden;
            await db.SaveChangesAsync();
        }

        var result = Allowed(await SearchAsync(data.TeacherId));

        Assert.Equal(practiceId, Assert.Single(result.Items).Id);
        Assert.Equal(data.Course.CourseTitle, result.Items[0].CourseTitle);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task SearchAsync_BlankSearch_DoesNotFilterOrExposeOtherOwners(string? search)
    {
        var data = await SeedAsync();
        await AddPracticeAsync(data.TeacherId, "Grammar");
        await AddPracticeAsync(data.TeacherId, "Reading");
        await AddPracticeAsync(data.OtherTeacherId, "Other teacher draft");

        var result = Allowed(await SearchAsync(data.TeacherId,
            new TeacherPracticeSearchRequest { Search = search }));

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task SearchAsync_SearchTrimsKeywordAndMatchesTitleSubstringWithoutMutatingRequest()
    {
        var data = await SeedAsync();
        var match = await AddPracticeAsync(data.TeacherId, "Beginner grammar review");
        await AddPracticeAsync(data.TeacherId, "Reading review");
        await AddPracticeAsync(data.OtherTeacherId, "Beginner grammar review");
        var request = new TeacherPracticeSearchRequest { Search = "  grammar  " };

        var result = Allowed(await SearchAsync(data.TeacherId, request));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(match, Assert.Single(result.Items).Id);
        Assert.Equal("  grammar  ", request.Search);
        Assert.Null(request.CourseId);
        Assert.Null(request.Status);
        Assert.Equal(1, request.PageNumber);
        Assert.Equal(10, request.PageSize);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("[")]
    [InlineData("O'Reilly")]
    [InlineData("' OR 1=1 --")]
    public async Task SearchAsync_SpecialCharacters_AreLiteralSearchText(string search)
    {
        var data = await SeedAsync();
        var match = await AddPracticeAsync(data.TeacherId, "Review " + search + " sample");
        await AddPracticeAsync(data.TeacherId, "Unrelated title");
        await AddPracticeAsync(data.OtherTeacherId, "Review " + search + " sample");

        var result = Allowed(await SearchAsync(data.TeacherId,
            new TeacherPracticeSearchRequest { Search = search }));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(match, Assert.Single(result.Items).Id);
    }

    [Theory]
    [InlineData(PracticeStatus.Draft)]
    [InlineData(PracticeStatus.Published)]
    [InlineData(PracticeStatus.Unpublished)]
    public async Task SearchAsync_StatusFilter_ReturnsOnlyOwnedMatchingStatus(PracticeStatus status)
    {
        var data = await SeedAsync();
        var expected = new Dictionary<PracticeStatus, Guid>();
        foreach (PracticeStatus candidate in Enum.GetValues<PracticeStatus>())
        {
            expected[candidate] = await AddPracticeAsync(data.TeacherId, "Status " + candidate,
                moduleId: data.Course.ModuleId, status: candidate, questionCount: 1);
        }
        await AddPracticeAsync(data.OtherTeacherId, "Foreign status match",
            moduleId: data.OtherCourse.ModuleId, status: status, questionCount: 1);

        var result = Allowed(await SearchAsync(data.TeacherId,
            new TeacherPracticeSearchRequest { Status = status }));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(expected[status], Assert.Single(result.Items).Id);
        Assert.Equal(status, result.Items[0].Status);
    }

    [Fact]
    public async Task SearchAsync_CourseFilter_IncludesBothParentTypesButNotUnattachedOrForeignPractices()
    {
        var data = await SeedAsync();
        var modulePractice = await AddPracticeAsync(data.TeacherId, "Module draft",
            moduleId: data.Course.ModuleId, createdAt: CreatedAt);
        var lessonPractice = await AddPracticeAsync(data.TeacherId, "Lesson draft",
            lessonId: data.Course.LessonId, createdAt: CreatedAt.AddMinutes(1));
        await AddPracticeAsync(data.TeacherId, "Other own course",
            moduleId: data.SecondCourse.ModuleId);
        await AddPracticeAsync(data.TeacherId, "Unattached draft");
        // Course ownership không thay thế được Practice ownership.
        await AddPracticeAsync(data.OtherTeacherId, "Foreign Practice in own Course",
            lessonId: data.Course.LessonId);

        var result = Allowed(await SearchAsync(data.TeacherId,
            new TeacherPracticeSearchRequest { CourseId = data.Course.CourseId }));

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(new[] { lessonPractice, modulePractice }, result.Items.Select(item => item.Id));
        Assert.All(result.Items, item => Assert.Equal(data.Course.CourseId, item.CourseId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchAsync_ForeignOrUnknownCourseFilter_ReturnsAllowedEmptyWithoutMetadata(bool unknown)
    {
        var data = await SeedAsync();
        await AddPracticeAsync(data.TeacherId, "Own draft", moduleId: data.Course.ModuleId);
        await AddPracticeAsync(data.OtherTeacherId, "Private foreign draft",
            lessonId: data.OtherCourse.LessonId);
        var courseId = unknown ? Guid.NewGuid() : data.OtherCourse.CourseId;

        var result = Allowed(await SearchAsync(data.TeacherId,
            new TeacherPracticeSearchRequest { CourseId = courseId }));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task SearchAsync_CombinedFilters_AreAppliedBeforeCountingAndPagination()
    {
        var data = await SeedAsync();
        var match = await AddPracticeAsync(data.TeacherId, "Grammar match",
            lessonId: data.Course.LessonId);
        await AddPracticeAsync(data.TeacherId, "Reading match", moduleId: data.Course.ModuleId);
        await AddPracticeAsync(data.TeacherId, "Grammar published", moduleId: data.Course.ModuleId,
            status: PracticeStatus.Published, questionCount: 1);
        await AddPracticeAsync(data.TeacherId, "Grammar other Course",
            moduleId: data.SecondCourse.ModuleId);
        await AddPracticeAsync(data.TeacherId, "Grammar unattached");
        await AddPracticeAsync(data.OtherTeacherId, "Grammar foreign practice",
            moduleId: data.Course.ModuleId);

        var result = Allowed(await SearchAsync(data.TeacherId,
            new TeacherPracticeSearchRequest
            {
                Search = " Grammar ", Status = PracticeStatus.Draft,
                CourseId = data.Course.CourseId, PageSize = 1
            }));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(match, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task SearchAsync_MultiplePages_HaveCorrectTotalsAndNoMissingOrRepeatedItems()
    {
        var data = await SeedAsync();
        var practices = new List<Practice>();
        for (int index = 0; index < 23; index++)
        {
            practices.Add(new Practice
            {
                OwnerTeacherUserId = data.TeacherId, Title = "Draft " + index,
                CreatedAtUtc = CreatedAt.AddMinutes(index)
            });
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Practices.AddRange(practices);
            await db.SaveChangesAsync();
        }
        await AddPracticeAsync(data.OtherTeacherId, "Newer foreign draft", createdAt: CreatedAt.AddDays(1));
        var expected = practices.OrderByDescending(practice => practice.CreatedAtUtc)
            .Select(practice => practice.Id).ToArray();
        var actual = new List<Guid>();

        for (int page = 1; page <= 3; page++)
        {
            var result = Allowed(await SearchAsync(data.TeacherId,
                new TeacherPracticeSearchRequest { PageNumber = page }));

            Assert.Equal(23, result.TotalCount);
            Assert.Equal(3, result.TotalPages);
            Assert.Equal(page, result.PageNumber);
            Assert.Equal(10, result.PageSize);
            Assert.Equal(page == 3 ? 3 : 10, result.Items.Count);
            actual.AddRange(result.Items.Select(item => item.Id));
        }

        Assert.Equal(expected, actual.ToArray());
        Assert.Equal(23, actual.Distinct().Count());
    }

    [Fact]
    public async Task SearchAsync_EqualCreatedAt_UsesStableSqlServerGuidOrderAcrossPages()
    {
        var data = await SeedAsync();
        var ids = new[]
        {
            Guid.Parse("00000001-0000-0000-0000-000000000000"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Guid.Parse("00000000-0000-0000-0000-000000000001")
        };
        foreach (var id in ids)
        {
            await AddPracticeAsync(data.TeacherId, "Equal timestamp", createdAt: CreatedAt, practiceId: id);
        }
        // SQL Server sắp xếp uniqueidentifier khác Guid.CompareTo của .NET.
        var expected = ids.OrderBy(id => new SqlGuid(id)).ToArray();
        var actual = new List<Guid>();
        for (int page = 1; page <= ids.Length; page++)
        {
            var result = Allowed(await SearchAsync(data.TeacherId,
                new TeacherPracticeSearchRequest { PageNumber = page, PageSize = 1 }));
            Assert.Equal(3, result.TotalCount);
            actual.Add(Assert.Single(result.Items).Id);
        }

        Assert.Equal(expected, actual.ToArray());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public async Task SearchAsync_PageBeyondResults_ReturnsEmptyAndPreservesTotalWithoutOverflow(int page)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        await AddPracticeAsync(teacher.Id, "One");
        await AddPracticeAsync(teacher.Id, "Two");

        var result = Allowed(await SearchAsync(teacher.Id,
            new TeacherPracticeSearchRequest { PageNumber = page, PageSize = 100 }));

        Assert.Empty(result.Items);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(page, result.PageNumber);
        Assert.Equal(100, result.PageSize);
    }

    [Theory]
    [InlineData(0, 10, "PageNumber")]
    [InlineData(-1, 10, "PageNumber")]
    [InlineData(int.MinValue, 10, "PageNumber")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, -1, "PageSize")]
    [InlineData(1, int.MinValue, "PageSize")]
    [InlineData(1, 101, "PageSize")]
    [InlineData(1, int.MaxValue, "PageSize")]
    public async Task SearchAsync_InvalidPagination_ThrowsForCorrectField(
        int pageNumber, int pageSize, string expectedField)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SearchAsync(teacher.Id, new TeacherPracticeSearchRequest
            {
                PageNumber = pageNumber, PageSize = pageSize
            }));

        Assert.Equal(expectedField, exception.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task SearchAsync_PageSizeAtLimits_IsAccepted(int pageSize)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);

        var result = Allowed(await SearchAsync(teacher.Id,
            new TeacherPracticeSearchRequest { PageSize = pageSize }));

        Assert.Empty(result.Items);
        Assert.Equal(pageSize, result.PageSize);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public async Task SearchAsync_UndefinedStatus_ThrowsArgumentException(int status)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            SearchAsync(teacher.Id, new TeacherPracticeSearchRequest
            {
                Status = (PracticeStatus)status
            }));

        Assert.Equal("Status", exception.ParamName);
    }

    [Fact]
    public async Task SearchAsync_EmptyCourseId_ThrowsArgumentException()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            SearchAsync(teacher.Id, new TeacherPracticeSearchRequest { CourseId = Guid.Empty }));

        Assert.Equal("CourseId", exception.ParamName);
    }

    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Admin)]
    public async Task SearchAsync_UnauthorizedWithInvalidFilters_DeniesBeforeValidation(string role)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(_factory, role);

        var outcome = await SearchAsync(user.Id, new TeacherPracticeSearchRequest
        {
            PageNumber = 0, PageSize = 101, Status = (PracticeStatus)999, CourseId = Guid.Empty
        });

        Denied(outcome);
    }

    [Fact]
    public async Task SearchAsync_IsReadOnly_DoesNotTrackEntitiesOrCreateAttemptsAndResults()
    {
        var data = await SeedAsync();
        var practiceId = await AddPracticeAsync(data.TeacherId, "Read-only draft",
            lessonId: data.Course.LessonId, createdAt: CreatedAt, questionCount: 2);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await ReadCountsAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<ITeacherPracticeQueryService>();
        Assert.Empty(db.ChangeTracker.Entries());

        var result = Allowed(await service.SearchAsync(data.TeacherId, new TeacherPracticeSearchRequest()));

        Assert.Equal(practiceId, Assert.Single(result.Items).Id);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(before, await ReadCountsAsync(db));
        var stored = await db.Practices.AsNoTracking().SingleAsync(practice => practice.Id == practiceId);
        Assert.Equal("Read-only draft", stored.Title);
        Assert.Equal(PracticeStatus.Draft, stored.Status);
        Assert.Equal(data.TeacherId, stored.OwnerTeacherUserId);
        Assert.Equal(data.Course.LessonId, stored.LessonId);
        Assert.Null(stored.ModuleId);
        Assert.Equal(CreatedAt, stored.CreatedAtUtc);
        Assert.Null(stored.UpdatedAtUtc);
    }

    [Fact]
    public async Task SearchAsync_PendingChangesInScope_AreNotSavedOrReturnedAsPersistedData()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        var practiceId = await AddPracticeAsync(teacher.Id, "Stored title");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Practices.SingleAsync(practice => practice.Id == practiceId);
        stored.Title = "Unsaved edit";
        var pending = new Practice { OwnerTeacherUserId = teacher.Id, Title = "Unsaved draft" };
        db.Practices.Add(pending);
        var service = scope.ServiceProvider.GetRequiredService<ITeacherPracticeQueryService>();

        var result = Allowed(await service.SearchAsync(teacher.Id, new TeacherPracticeSearchRequest()));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Stored title", Assert.Single(result.Items).Title);
        Assert.Equal(EntityState.Added, db.Entry(pending).State);
        Assert.Equal(EntityState.Modified, db.Entry(stored).State);
        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verificationDb.Practices.AnyAsync(practice => practice.Id == pending.Id));
        Assert.Equal("Stored title", await verificationDb.Practices
            .Where(practice => practice.Id == practiceId).Select(practice => practice.Title).SingleAsync());
    }

    [Theory]
    [InlineData("Lesson")]
    [InlineData("Module")]
    public async Task SearchAsync_ParentMovedToForeignCourse_DoesNotExposeNewParentMetadata(string parent)
    {
        var data = await SeedAsync();
        await AddPracticeAsync(data.TeacherId, "Moved parent",
            moduleId: parent == "Module" ? data.Course.ModuleId : null,
            lessonId: parent == "Lesson" ? data.Course.LessonId : null);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (parent == "Lesson")
            {
                var lesson = await db.Lessons.SingleAsync(lesson => lesson.Id == data.Course.LessonId);
                lesson.ModuleId = data.OtherCourse.ModuleId;
                // Nơi đến đã có Lesson ở vị trí 0. Giữ đúng unique key
                // (ModuleId, OrderIndex), không sửa schema để tạo tình huống di chuyển.
                int lastOrder = await db.Lessons
                    .Where(item => item.ModuleId == data.OtherCourse.ModuleId)
                    .MaxAsync(item => (int?)item.OrderIndex) ?? -1;
                lesson.OrderIndex = lastOrder + 1;
            }
            else
            {
                var module = await db.Modules.SingleAsync(module => module.Id == data.Course.ModuleId);
                module.CourseId = data.OtherCourse.CourseId;
                // Tương tự, Module phải có thứ tự còn trống trong Course đích.
                int lastOrder = await db.Modules
                    .Where(item => item.CourseId == data.OtherCourse.CourseId)
                    .MaxAsync(item => (int?)item.OrderIndex) ?? -1;
                module.OrderIndex = lastOrder + 1;
            }
            await db.SaveChangesAsync();
        }

        var result = Allowed(await SearchAsync(data.TeacherId));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    private async Task<LearningOutcome<TeacherPracticeSearchResult>> SearchAsync(
        Guid actorUserId, TeacherPracticeSearchRequest? request = null)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITeacherPracticeQueryService>();
        return await service.SearchAsync(actorUserId, request ?? new TeacherPracticeSearchRequest());
    }

    private static TeacherPracticeSearchResult Allowed(LearningOutcome<TeacherPracticeSearchResult> outcome)
    {
        Assert.Equal(LearningAccessResult.Allowed, outcome.Access);
        return Assert.IsType<TeacherPracticeSearchResult>(outcome.Value);
    }

    private static void Denied(LearningOutcome<TeacherPracticeSearchResult> outcome)
    {
        Assert.Equal(LearningAccessResult.Forbidden, outcome.Access);
        Assert.Null(outcome.Value);
    }

    private async Task<SeedData> SeedAsync()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        var otherTeacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = AddCourse(db, teacher.Id, "Own first");
        var secondCourse = AddCourse(db, teacher.Id, "Own second");
        var otherCourse = AddCourse(db, otherTeacher.Id, "Private other");
        await db.SaveChangesAsync();
        return new SeedData(teacher.Id, otherTeacher.Id, course, secondCourse, otherCourse);
    }

    private static CourseTree AddCourse(AppDbContext db, Guid ownerId, string prefix)
    {
        var course = new Course
        {
            OwnerTeacherUserId = ownerId, Title = prefix + " Course", Status = CourseStatus.Published
        };
        var module = new Module
        {
            CourseId = course.Id, Title = prefix + " Module", OrderIndex = 0,
            Visibility = ModuleVisibility.Visible
        };
        var lesson = new Lesson
        {
            ModuleId = module.Id, Title = prefix + " Lesson", OrderIndex = 0,
            Status = LessonStatus.Published
        };
        module.Lessons.Add(lesson);
        course.Modules.Add(module);
        db.Courses.Add(course);
        return new CourseTree(course.Id, course.Title, module.Id, module.Title, lesson.Id, lesson.Title);
    }

    private async Task<Guid> AddPracticeAsync(Guid ownerId, string title,
        Guid? moduleId = null, Guid? lessonId = null, PracticeStatus status = PracticeStatus.Draft,
        DateTimeOffset? createdAt = null, int questionCount = 0, Guid? practiceId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var practice = new Practice
        {
            Id = practiceId ?? Guid.NewGuid(), OwnerTeacherUserId = ownerId,
            Title = title, ModuleId = moduleId, LessonId = lessonId,
            Status = status, CreatedAtUtc = createdAt ?? CreatedAt
        };
        for (int index = 0; index < questionCount; index++)
        {
            var question = new Question
            {
                OwnerTeacherUserId = ownerId, Content = "Choose the correct answer " + index,
                PrimarySkill = EnglishSkill.Grammar, Status = QuestionStatus.Published
            };
            question.Options.Add(new QuestionOption { Content = "Correct", IsCorrect = true, OrderIndex = 0 });
            question.Options.Add(new QuestionOption { Content = "Incorrect", IsCorrect = false, OrderIndex = 1 });
            practice.Questions.Add(new PracticeQuestion { Question = question, OrderIndex = index });
        }
        db.Practices.Add(practice);
        await db.SaveChangesAsync();
        return practice.Id;
    }

    private async Task ChangeStatusAsync(Guid userId, AccountStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(user => user.Id == userId);
        user.AccountStatus = status;
        await db.SaveChangesAsync();
    }

    private async Task ChangeRoleAsync(Guid userId, string? replacement)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        Assert.True((await users.RemoveFromRoleAsync(user, AppRoles.Teacher)).Succeeded);
        if (replacement != null)
        {
            Assert.True((await users.AddToRoleAsync(user, replacement)).Succeeded);
        }
    }

    private static async Task<TableCounts> ReadCountsAsync(AppDbContext db)
    {
        return new TableCounts(await db.Practices.CountAsync(), await db.PracticeQuestions.CountAsync(),
            await db.PracticeSubmissions.CountAsync(), await db.PracticeAnswers.CountAsync(),
            await db.Attempts.CountAsync(), await db.AttemptAnswers.CountAsync(),
            await db.Questions.CountAsync(), await db.QuestionOptions.CountAsync());
    }

    private sealed record CourseTree(Guid CourseId, string CourseTitle, Guid ModuleId,
        string ModuleTitle, Guid LessonId, string LessonTitle);
    private sealed record SeedData(Guid TeacherId, Guid OtherTeacherId, CourseTree Course,
        CourseTree SecondCourse, CourseTree OtherCourse);
    private sealed record TableCounts(int Practices, int PracticeQuestions, int Submissions,
        int PracticeAnswers, int Attempts, int AttemptAnswers, int Questions, int Options);
}
