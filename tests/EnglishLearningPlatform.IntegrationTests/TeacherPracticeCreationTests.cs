using System.Data.Common;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using EnglishLearningPlatform.Infrastructure.Practices;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// DatabaseFixture của factory dùng SQL container/database test riêng.
// Không kết nối đến database demo và không sửa dữ liệu của nhóm.
public sealed class TeacherPracticeCreationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public TeacherPracticeCreationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void ApplicationScope_ResolvesTeacherPracticeServiceFromRealRegistrations()
    {
        using var scope = _factory.Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<ITeacherPracticeService>();

        Assert.IsType<TeacherPracticeService>(service);
        Assert.Same(service, scope.ServiceProvider.GetRequiredService<ITeacherPracticeService>());
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Lesson")]
    [InlineData("Module")]
    public async Task CreateDraft_ValidInput_SavesOwnedDraftAndChosenParent(string parent)
    {
        var data = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = Request(data, parent);
        request.Title = "  Grammar practice  ";
        request.Description = "  Description  ";
        var before = DateTimeOffset.UtcNow;

        var result = await DirectService(scope.ServiceProvider).CreateDraftAsync(data.TeacherId, request);

        Assert.True(result.Success);
        Assert.Equal(LearningAccessResult.Allowed, result.Access);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Empty(result.Errors);
        var saved = await db.Practices.AsNoTracking().SingleAsync(p => p.Id == result.Id);
        Assert.Equal(data.TeacherId, saved.OwnerTeacherUserId);
        Assert.Equal(PracticeStatus.Draft, saved.Status);
        Assert.Equal("Grammar practice", saved.Title);
        Assert.Equal("Description", saved.Description);
        Assert.Equal(request.LessonId, saved.LessonId);
        Assert.Equal(request.ModuleId, saved.ModuleId);
        Assert.InRange(saved.CreatedAtUtc, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Null(saved.UpdatedAtUtc);
        Assert.False(await db.PracticeQuestions.AnyAsync(q => q.PracticeId == saved.Id));
        Assert.False(await db.PracticeSubmissions.AnyAsync(s => s.PracticeId == saved.Id));
        Assert.Null(db.Database.CurrentTransaction);
        // Service chuẩn hóa dữ liệu lưu, nhưng không sửa request của caller.
        Assert.Equal("  Grammar practice  ", request.Title);
        Assert.Equal("  Description  ", request.Description);
    }

    [Fact]
    public async Task CreateDraft_NoCoursesOrParent_IsAllowedAndDoesNotCreateStudentActivity()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var result = await DirectService(scope.ServiceProvider).CreateDraftAsync(teacher.Id,
            new CreatePracticeRequest { Title = "Empty draft" });

        Assert.True(result.Success);
        Assert.False(await db.Courses.AnyAsync(c => c.OwnerTeacherUserId == teacher.Id));
        var saved = await db.Practices.AsNoTracking().SingleAsync(p => p.Id == result.Id);
        Assert.Null(saved.LessonId);
        Assert.Null(saved.ModuleId);
        Assert.Equal(PracticeStatus.Draft, saved.Status);
        Assert.False(await db.PracticeQuestions.AnyAsync(q => q.PracticeId == saved.Id));
        Assert.False(await db.PracticeSubmissions.AnyAsync(s => s.PracticeId == saved.Id));
        Assert.False(await db.Attempts.AnyAsync(a => a.StudentUserId == teacher.Id));
    }

    [Theory]
    [InlineData("Lesson", CourseStatus.Draft)]
    [InlineData("Lesson", CourseStatus.Published)]
    [InlineData("Lesson", CourseStatus.Unpublished)]
    [InlineData("Lesson", CourseStatus.Archived)]
    [InlineData("Module", CourseStatus.Draft)]
    [InlineData("Module", CourseStatus.Published)]
    [InlineData("Module", CourseStatus.Unpublished)]
    [InlineData("Module", CourseStatus.Archived)]
    public async Task CreateDraft_ParentVisibilityAndCourseState_DoNotApplyPublishRules(
        string parent, CourseStatus status)
    {
        // TBR16: điều kiện Course chưa Archived thuộc bước Publish.
        // Không biến tạo Draft thành Publish hoặc yêu cầu parent đang visible.
        var data = await SeedAsync(status);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var result = await DirectService(scope.ServiceProvider).CreateDraftAsync(data.TeacherId, Request(data, parent));

        Assert.True(result.Success);
        Assert.Equal(PracticeStatus.Draft,
            await db.Practices.Where(p => p.Id == result.Id).Select(p => p.Status).SingleAsync());
        Assert.Equal(status,
            await db.Courses.Where(c => c.Id == data.CourseId).Select(c => c.Status).SingleAsync());
        Assert.Equal(ModuleVisibility.Hidden,
            await db.Modules.Where(m => m.Id == data.ModuleId).Select(m => m.Visibility).SingleAsync());
        Assert.Equal(LessonStatus.Draft,
            await db.Lessons.Where(l => l.Id == data.LessonId).Select(l => l.Status).SingleAsync());
    }

    [Fact]
    public async Task CreateDraft_MetadataAtDatabaseLimits_IsSaved()
    {
        var data = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = Request(data, "None");
        request.Title = new string('a', 200);
        request.Description = new string('b', 4000);

        var result = await DirectService(scope.ServiceProvider).CreateDraftAsync(data.TeacherId, request);

        Assert.True(result.Success);
        var saved = await db.Practices.AsNoTracking().SingleAsync(p => p.Id == result.Id);
        Assert.Equal(request.Title, saved.Title);
        Assert.Equal(request.Description, saved.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateDraft_OptionalDescription_IsSavedWithoutWarningOrFailure(string? description)
    {
        var data = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = Request(data, "None");
        request.Description = description;

        var result = await DirectService(scope.ServiceProvider).CreateDraftAsync(data.TeacherId, request);

        Assert.True(result.Success);
        Assert.Equal(description?.Trim(),
            await db.Practices.Where(p => p.Id == result.Id).Select(p => p.Description).SingleAsync());
    }

    [Theory]
    [InlineData("NullTitle", "Title")]
    [InlineData("EmptyTitle", "Title")]
    [InlineData("BlankTitle", "Title")]
    [InlineData("LongTitle", "Title")]
    [InlineData("LongDescription", "Description")]
    [InlineData("EmptyLesson", "LessonId")]
    [InlineData("EmptyModule", "ModuleId")]
    [InlineData("BothParents", "LessonId")]
    [InlineData("BothEmptyParents", "ModuleId")]
    public async Task CreateDraft_InvalidInput_ReturnsFieldErrorsAndDoesNotSave(string fault, string field)
    {
        var data = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = Request(data, "None");
        switch (fault)
        {
            case "NullTitle": request.Title = null!; break;
            case "EmptyTitle": request.Title = ""; break;
            case "BlankTitle": request.Title = "   "; break;
            case "LongTitle": request.Title = new string('a', 201); break;
            case "LongDescription": request.Description = new string('b', 4001); break;
            case "EmptyLesson": request.LessonId = Guid.Empty; break;
            case "EmptyModule": request.ModuleId = Guid.Empty; break;
            case "BothParents": request.LessonId = data.LessonId; request.ModuleId = data.ModuleId; break;
            case "BothEmptyParents": request.LessonId = Guid.Empty; request.ModuleId = Guid.Empty; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }

        var result = await DirectService(scope.ServiceProvider).CreateDraftAsync(data.TeacherId, request);

        AssertInvalid(result, field);
        await AssertNothingSavedAsync(db, data.TeacherId);
    }

    [Theory]
    [InlineData("Lesson")]
    [InlineData("Module")]
    public async Task CreateDraft_MissingOrForeignParent_ReturnsSameGenericErrorAndDoesNotSave(string parent)
    {
        var data = await SeedAsync();
        var foreign = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var foreignRequest = Request(foreign, parent);
        var missingRequest = new CreatePracticeRequest
        {
            Title = "Grammar practice",
            LessonId = parent == "Lesson" ? Guid.NewGuid() : null,
            ModuleId = parent == "Module" ? Guid.NewGuid() : null
        };
        var field = parent == "Lesson" ? "LessonId" : "ModuleId";
        var service = DirectService(scope.ServiceProvider);

        var foreignResult = await service.CreateDraftAsync(data.TeacherId, foreignRequest);
        var missingResult = await service.CreateDraftAsync(data.TeacherId, missingRequest);

        AssertInvalid(foreignResult, field);
        AssertInvalid(missingResult, field);
        Assert.Equal(missingResult.Errors[field], foreignResult.Errors[field]);
        await AssertNothingSavedAsync(db, data.TeacherId);
        await AssertNothingSavedAsync(db, foreign.TeacherId);
    }

    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Admin)]
    [InlineData("NoRole")]
    public async Task CreateDraft_NonTeacher_IsForbiddenBeforeValidation(string role)
    {
        var actor = await IdentityTestHelpers.CreateUserAsync(_factory,
            role == "NoRole" ? AppRoles.Teacher : role);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (role == "NoRole")
        {
            await db.UserRoles.Where(r => r.UserId == actor.Id).ExecuteDeleteAsync();
        }
        var validator = new RecordingValidator(new Dictionary<string, string[]>());

        // Đầu vào cũng sai để chứng minh kiểm tra quyền diễn ra trước validation.
        var result = await DirectService(scope.ServiceProvider, validator)
            .CreateDraftAsync(actor.Id, new CreatePracticeRequest());

        AssertForbidden(result);
        Assert.Equal(0, validator.Calls);
        await AssertNothingSavedAsync(db, actor.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateDraft_EmptyOrNonexistentActor_IsForbidden(bool empty)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = empty ? Guid.Empty : Guid.NewGuid();

        var result = await DirectService(scope.ServiceProvider).CreateDraftAsync(actorId,
            new CreatePracticeRequest { Title = "Grammar practice" });

        AssertForbidden(result);
        await AssertNothingSavedAsync(db, actorId);
    }

    [Theory]
    [InlineData(AccountStatus.Locked)]
    [InlineData(AccountStatus.Disabled)]
    public async Task CreateDraft_InactiveTeacher_IsForbidden(AccountStatus status)
    {
        var data = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users.Where(u => u.Id == data.TeacherId)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.AccountStatus, status));

        var result = await DirectService(scope.ServiceProvider).CreateDraftAsync(data.TeacherId, Request(data, "Lesson"));

        AssertForbidden(result);
        await AssertNothingSavedAsync(db, data.TeacherId);
    }

    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Admin)]
    public async Task Database_SecondRoleIsRejectedAndExistingTeacherCanStillCreateDraft(string extraRole)
    {
        var data = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var teacher = await manager.FindByIdAsync(data.TeacherId.ToString());
        Assert.NotNull(teacher);
        // Mapping IdentityUserRoleConfiguration có unique index trên UserId.
        // Trạng thái hai role không thể được lưu; test phải kiểm tra ràng buộc
        // này thay vì bỏ index để tạo một trạng thái không hợp lệ trong database.
        var exception = await Assert.ThrowsAsync<DbUpdateException>(async () =>
            await manager.AddToRoleAsync(teacher, extraRole));
        var sqlException = Assert.IsType<SqlException>(exception.InnerException);
        Assert.True(sqlException.Number is 2601 or 2627);
        Assert.Contains("IX_AspNetUserRoles_UserId", sqlException.Message);
        // Loại bỏ entity đang chờ ghi sau lần SaveChanges bị từ chối.
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.UserRoles.CountAsync(r => r.UserId == data.TeacherId));
        Assert.True(await db.UserRoles.AnyAsync(r => r.UserId == data.TeacherId &&
            db.Roles.Any(role => role.Id == r.RoleId && role.Name == AppRoles.Teacher)));

        var result = await DirectService(scope.ServiceProvider).CreateDraftAsync(data.TeacherId, Request(data, "Module"));

        // Role hiện có vẫn là Teacher; việc thêm role thất bại không tước quyền
        // của tài khoản hợp lệ hoặc làm thay đổi owner của Practice mới.
        Assert.True(result.Success);
        var saved = await db.Practices.AsNoTracking().SingleAsync(p => p.Id == result.Id);
        Assert.Equal(data.TeacherId, saved.OwnerTeacherUserId);
        Assert.Equal(PracticeStatus.Draft, saved.Status);
    }

    [Fact]
    public async Task CreateDraft_UsesInjectedValidatorAndPropagatesErrorsWithoutSaving()
    {
        var data = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var errors = new Dictionary<string, string[]> { ["Title"] = new[] { "TEST_VALIDATOR_REJECTION" } };
        var validator = new RecordingValidator(errors);

        var result = await DirectService(scope.ServiceProvider, validator).CreateDraftAsync(data.TeacherId, Request(data, "None"));

        AssertInvalid(result, "Title");
        Assert.Same(errors, result.Errors);
        Assert.Equal(1, validator.Calls);
        await AssertNothingSavedAsync(db, data.TeacherId);
    }

    [Theory]
    [InlineData("Lesson", "OwnerChanged")]
    [InlineData("Module", "OwnerChanged")]
    [InlineData("Lesson", "ParentMoved")]
    [InlineData("Module", "ParentMoved")]
    [InlineData("Lesson", "ParentDeleted")]
    [InlineData("Module", "ParentDeleted")]
    [InlineData("Lesson", "CourseDeleted")]
    [InlineData("Module", "CourseDeleted")]
    public async Task CreateDraft_ParentChangesAfterInitialLookup_IsRejectedAndTransactionDisposed(
        string parent, string mutation)
    {
        var data = await SeedAsync();
        var foreign = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var seedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = seedDb.Database.GetConnectionString()!;
        var interceptor = new ChangeBeforeReadInterceptor(connection, false, async db =>
        {
            switch (mutation)
            {
                case "OwnerChanged":
                    await db.Courses.Where(c => c.Id == data.CourseId)
                        .ExecuteUpdateAsync(update => update.SetProperty(c => c.OwnerTeacherUserId, foreign.TeacherId));
                    break;
                case "ParentMoved":
                    // Index 1 tránh trùng thứ tự với parent đã có của fixture đích.
                    if (parent == "Lesson")
                        await db.Lessons.Where(l => l.Id == data.LessonId).ExecuteUpdateAsync(update =>
                            update.SetProperty(l => l.ModuleId, foreign.ModuleId).SetProperty(l => l.OrderIndex, 1));
                    else
                        await db.Modules.Where(m => m.Id == data.ModuleId).ExecuteUpdateAsync(update =>
                            update.SetProperty(m => m.CourseId, foreign.CourseId).SetProperty(m => m.OrderIndex, 1));
                    break;
                case "ParentDeleted":
                case "CourseDeleted":
                    await db.Lessons.Where(l => l.Id == data.LessonId).ExecuteDeleteAsync();
                    if (parent == "Module" || mutation == "CourseDeleted")
                        await db.Modules.Where(m => m.Id == data.ModuleId).ExecuteDeleteAsync();
                    if (mutation == "CourseDeleted")
                        await db.Courses.Where(c => c.Id == data.CourseId).ExecuteDeleteAsync();
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(mutation));
            }
        });
        await using var serviceDb = DbWithInterceptor(connection, interceptor);
        var service = new TeacherPracticeService(serviceDb,
            scope.ServiceProvider.GetRequiredService<ICreatePracticeValidator>());

        var result = await service.CreateDraftAsync(data.TeacherId, Request(data, parent));

        Assert.True(interceptor.Applied);
        AssertInvalid(result, parent == "Lesson" ? "LessonId" : "ModuleId");
        Assert.Null(serviceDb.Database.CurrentTransaction);
        await AssertNothingSavedAsync(seedDb, data.TeacherId);
    }

    [Theory]
    [InlineData("None", "Locked")]
    [InlineData("Lesson", "Disabled")]
    [InlineData("Module", "RoleRemoved")]
    [InlineData("None", "RoleRemoved")]
    [InlineData("Lesson", "RoleReplaced")]
    public async Task CreateDraft_RoleOrAccountChangesBeforeSecondCheck_IsForbiddenAndNotSaved(
        string parent, string mutation)
    {
        var data = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var seedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = seedDb.Database.GetConnectionString()!;
        var interceptor = new ChangeBeforeReadInterceptor(connection, true, async db =>
        {
            if (mutation is "Locked" or "Disabled")
            {
                var status = mutation == "Locked" ? AccountStatus.Locked : AccountStatus.Disabled;
                await db.Users.Where(u => u.Id == data.TeacherId)
                    .ExecuteUpdateAsync(update => update.SetProperty(u => u.AccountStatus, status));
            }
            else
            {
                if (mutation is "RoleRemoved" or "RoleReplaced")
                    await db.UserRoles.Where(r => r.UserId == data.TeacherId).ExecuteDeleteAsync();
                if (mutation == "RoleReplaced")
                {
                    var roleId = await db.Roles.Where(r => r.Name == AppRoles.Student).Select(r => r.Id).SingleAsync();
                    db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = data.TeacherId, RoleId = roleId });
                    await db.SaveChangesAsync();
                }
            }
        });
        await using var serviceDb = DbWithInterceptor(connection, interceptor);
        var service = new TeacherPracticeService(serviceDb,
            scope.ServiceProvider.GetRequiredService<ICreatePracticeValidator>());

        var result = await service.CreateDraftAsync(data.TeacherId, Request(data, parent));

        Assert.True(interceptor.Applied);
        AssertForbidden(result);
        Assert.Null(serviceDb.Database.CurrentTransaction);
        await AssertNothingSavedAsync(seedDb, data.TeacherId);
    }

    [Fact]
    public async Task CreateDraft_FailureAfterSave_RollsBackInsertedPractice()
    {
        var data = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var seedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var interceptor = new ThrowAfterPracticeSaveInterceptor();
        await using var serviceDb = DbWithInterceptor(seedDb.Database.GetConnectionString()!, interceptor);
        var service = new TeacherPracticeService(serviceDb,
            scope.ServiceProvider.GetRequiredService<ICreatePracticeValidator>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateDraftAsync(data.TeacherId, Request(data, "Lesson")));

        Assert.Equal("TEST_FAILURE_AFTER_PRACTICE_SAVE", exception.Message);
        Assert.True(interceptor.Applied);
        Assert.Null(serviceDb.Database.CurrentTransaction);
        // Đọc từ context khác để không nhầm entity tracked với dữ liệu đã commit.
        await AssertNothingSavedAsync(seedDb, data.TeacherId);
    }

    [Theory]
    [InlineData("Lesson")]
    [InlineData("Module")]
    public async Task CreateDraft_EnrolledStudentCannotTakeOrSubmitDraftEvenWithValidQuestions(string parent)
    {
        var data = await SeedAsync(CourseStatus.Published, visible: true);
        var student = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var result = await DirectService(scope.ServiceProvider).CreateDraftAsync(data.TeacherId, Request(data, parent));
        Assert.True(result.Success);
        var question = new Question
        {
            OwnerTeacherUserId = data.TeacherId, Content = "Choose the correct answer.",
            PrimarySkill = EnglishSkill.Grammar, Status = QuestionStatus.Published,
            Options = new List<QuestionOption>
            {
                new QuestionOption { Content = "Correct", IsCorrect = true, OrderIndex = 0 },
                new QuestionOption { Content = "Wrong", IsCorrect = false, OrderIndex = 1 }
            }
        };
        var link = new PracticeQuestion { PracticeId = result.Id, Question = question, OrderIndex = 0 };
        db.Enrollments.Add(new Enrollment { StudentUserId = student.Id, CourseId = data.CourseId });
        db.PracticeQuestions.Add(link);
        await db.SaveChangesAsync();
        var studentService = scope.ServiceProvider.GetRequiredService<IPracticeService>();

        var sheet = await studentService.GetSheetAsync(student.Id, result.Id);
        var submit = await studentService.SubmitAsync(student.Id, result.Id,
            new[] { new PracticeResponse(link.Id, question.Options.Single(o => o.IsCorrect).Id) });

        Assert.Equal(LearningAccessResult.NotFound, sheet.Access);
        Assert.Null(sheet.Value);
        Assert.Equal(PracticeSubmitStatus.Unavailable, submit.Status);
        Assert.False(await db.PracticeSubmissions.AnyAsync(s => s.PracticeId == result.Id));
        Assert.False(await db.Attempts.AnyAsync(a => a.StudentUserId == student.Id));
        Assert.Equal(PracticeStatus.Draft,
            await db.Practices.Where(p => p.Id == result.Id).Select(p => p.Status).SingleAsync());
    }

    private async Task<Data> SeedAsync(CourseStatus status = CourseStatus.Draft, bool visible = false)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Practice test course", Status = status };
        var module = new Module
        {
            Course = course, Title = "Practice test module",
            Visibility = visible ? ModuleVisibility.Visible : ModuleVisibility.Hidden
        };
        var lesson = new Lesson
        {
            Module = module, Title = "Practice test lesson",
            Status = visible ? LessonStatus.Published : LessonStatus.Draft
        };
        if (visible)
            lesson.Resources.Add(new LessonResource { ResourceType = LessonResourceType.Text, ContentText = "Lesson content" });
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();
        return new Data(teacher.Id, course.Id, module.Id, lesson.Id);
    }

    private static CreatePracticeRequest Request(Data data, string parent) => new()
    {
        Title = "Grammar practice",
        LessonId = parent == "Lesson" ? data.LessonId : null,
        ModuleId = parent == "Module" ? data.ModuleId : null
    };

    private static TeacherPracticeService DirectService(IServiceProvider services, ICreatePracticeValidator? validator = null)
    {
        // Các test logic gọi production service trực tiếp với database thật của fixture.
        // Đăng ký DI thật được kiểm tra riêng; không đăng ký thay/bỏ qua lỗi DI trong test host.
        return new TeacherPracticeService(services.GetRequiredService<AppDbContext>(),
            validator ?? services.GetRequiredService<ICreatePracticeValidator>());
    }

    private static AppDbContext DbWithInterceptor(string connection, IInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection).AddInterceptors(interceptor).Options;
        return new AppDbContext(options);
    }

    private static void AssertForbidden(WriteOutcome result)
    {
        Assert.False(result.Success);
        Assert.Equal(LearningAccessResult.Forbidden, result.Access);
        Assert.Equal(Guid.Empty, result.Id);
        Assert.Empty(result.Errors);
    }

    private static void AssertInvalid(WriteOutcome result, string field)
    {
        Assert.False(result.Success);
        Assert.Equal(LearningAccessResult.Allowed, result.Access);
        Assert.Equal(Guid.Empty, result.Id);
        Assert.True(result.Errors.ContainsKey(field));
        Assert.NotEmpty(result.Errors[field]);
        Assert.All(result.Errors[field], message => Assert.False(string.IsNullOrWhiteSpace(message)));
    }

    private static async Task AssertNothingSavedAsync(AppDbContext db, Guid actorId)
    {
        Assert.False(await db.Practices.AsNoTracking().AnyAsync(p => p.OwnerTeacherUserId == actorId));
        Assert.False(await db.PracticeSubmissions.AnyAsync(s => s.Practice.OwnerTeacherUserId == actorId));
        Assert.Null(db.Database.CurrentTransaction);
    }

    private sealed record Data(Guid TeacherId, Guid CourseId, Guid ModuleId, Guid LessonId);

    private sealed class RecordingValidator : ICreatePracticeValidator
    {
        private readonly IReadOnlyDictionary<string, string[]> _errors;
        public int Calls { get; private set; }
        public RecordingValidator(IReadOnlyDictionary<string, string[]> errors) { _errors = errors; }
        public IReadOnlyDictionary<string, string[]> Validate(CreatePracticeRequest request)
        {
            Calls++;
            return _errors;
        }
    }

    private sealed class ChangeBeforeReadInterceptor : DbCommandInterceptor
    {
        private readonly string _connection;
        private readonly bool _beforeSecondTeacherCheck;
        private readonly Func<AppDbContext, Task> _mutation;
        private int _teacherChecks;
        public bool Applied { get; private set; }

        public ChangeBeforeReadInterceptor(string connection, bool beforeSecondTeacherCheck, Func<AppDbContext, Task> mutation)
        {
            _connection = connection;
            _beforeSecondTeacherCheck = beforeSecondTeacherCheck;
            _mutation = mutation;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            bool teacherCheck = command.CommandText.Contains("[AspNetUsers]", StringComparison.Ordinal)
                && command.CommandText.Contains("[AspNetUserRoles]", StringComparison.Ordinal)
                && command.CommandText.Contains("[AspNetRoles]", StringComparison.Ordinal);
            if (teacherCheck) _teacherChecks++;
            bool courseLock = command.CommandText.Contains("FROM Courses WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal);
            if (!Applied && (_beforeSecondTeacherCheck ? teacherCheck && _teacherChecks == 2 : courseLock))
            {
                // Thay đổi dữ liệu test từ connection khác ở đúng checkpoint.
                // Không bỏ khóa, không sửa service, không sửa schema để ép test pass.
                Applied = true;
                var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_connection).Options;
                await using var mutationDb = new AppDbContext(options);
                await _mutation(mutationDb);
            }
            return result;
        }
    }

    private sealed class ThrowAfterPracticeSaveInterceptor : SaveChangesInterceptor
    {
        public bool Applied { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Practice>().Any())
            {
                Applied = true;
                throw new InvalidOperationException("TEST_FAILURE_AFTER_PRACTICE_SAVE");
            }
            return new ValueTask<int>(result);
        }
    }
}
