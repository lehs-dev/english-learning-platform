using System.Net;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using AssessmentEntity = EnglishLearningPlatform.Domain.Entities.Assessment;

namespace EnglishLearningPlatform.IntegrationTests;

// Factory tạo SQL Server test riêng. Không sử dụng database demo của người dùng.
public sealed class TeacherAssessmentDraftTests : IClassFixture<IntegrationTestFactory>
{
    private const string IndexPath = "/TeacherAssessments";
    private const string EditPath = "/TeacherAssessments/Edit";
    private readonly IntegrationTestFactory _factory;

    public TeacherAssessmentDraftTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(AssessmentType.PlacementTest)]
    [InlineData(AssessmentType.SkillAssessment)]
    [InlineData(AssessmentType.PracticeExam)]
    public async Task Create_AllThreeTypes_SavesOwnedEmptyDraftAndReopensConfiguration(AssessmentType type)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        var input = Request(type);
        input.Title = "  Assessment title  ";
        input.Description = "  Instructions  ";

        var result = await SaveAsync(teacher.Id, null, input);

        Assert.True(result.Success);
        var saved = await ReadAsync(result.Id);
        Assert.Equal(teacher.Id, saved.OwnerTeacherUserId);
        Assert.Equal(AssessmentStatus.Draft, saved.Status);
        Assert.Null(saved.CourseId);
        Assert.Equal("Assessment title", saved.Title);
        Assert.Equal("Instructions", saved.Description);
        Assert.Equal(type, saved.AssessmentType);
        Assert.Equal(input.TargetSkill, saved.TargetSkill);
        Assert.Equal(input.DurationMinutes, saved.DurationMinutes);
        Assert.Equal(input.MaxAttempts, saved.MaxAttempts);
        Assert.Equal(input.PassingScore, saved.PassingScore);
        Assert.Empty(saved.Questions);
        Assert.Empty(saved.Attempts);

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentDraftService>();
        var editor = await service.GetDraftAsync(teacher.Id, result.Id);
        Assert.Equal(LearningAccessResult.Allowed, editor.Access);
        Assert.NotNull(editor.Value);
        Assert.Equal(saved.Title, editor.Value.Title);
        Assert.Equal(saved.Description, editor.Value.Description);
        Assert.Equal(type, editor.Value.AssessmentType);
        Assert.Equal(saved.TargetSkill, editor.Value.TargetSkill);
        Assert.Equal(saved.DurationMinutes, editor.Value.DurationMinutes);
        Assert.Equal(saved.MaxAttempts, editor.Value.MaxAttempts);
        Assert.Equal(saved.PassingScore, editor.Value.PassingScore);
        Assert.Equal(saved.UpdatedAtUtc, editor.Value.ExpectedUpdatedAtUtc);
        var list = await service.GetListAsync(teacher.Id);
        var item = Assert.Single(list.Value!);
        Assert.Equal(result.Id, item.Id);
        Assert.True(item.CanEditDraft);
        Assert.Equal(0, item.QuestionCount);
    }

    [Fact]
    public async Task Edit_UpdatesConfigurationPreservesCourseAndQuestions_AndRejectsStaleVersion()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        Guid id = await CreateAsync(teacher);
        Guid courseId;
        Guid linkId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Owned course" };
            var link = new AssessmentQuestion
            {
                AssessmentId = id, Points = 2.5m, OrderIndex = 0,
                Question = new Question
                {
                    OwnerTeacherUserId = teacher.Id, Content = "Existing question",
                    PrimarySkill = EnglishSkill.Grammar,
                    Options = new List<QuestionOption>
                    {
                        new QuestionOption { Content = "Correct", IsCorrect = true, OrderIndex = 0 },
                        new QuestionOption { Content = "Wrong", OrderIndex = 1 }
                    }
                }
            };
            db.Courses.Add(course);
            db.AssessmentQuestions.Add(link);
            (await db.Assessments.SingleAsync(a => a.Id == id)).CourseId = course.Id;
            await db.SaveChangesAsync();
            courseId = course.Id;
            linkId = link.Id;
        }

        var input = Request(AssessmentType.SkillAssessment);
        input.Title = "Updated draft";
        input.DurationMinutes = 35;
        input.MaxAttempts = 4;
        input.PassingScore = 72.25m;
        Assert.True((await SaveAsync(teacher.Id, id, input)).Success);
        var saved = await ReadAsync(id);
        Assert.Equal("Updated draft", saved.Title);
        Assert.Equal(35, saved.DurationMinutes);
        Assert.Equal(4, saved.MaxAttempts);
        Assert.Equal(72.25m, saved.PassingScore);
        Assert.Equal(AssessmentType.SkillAssessment, saved.AssessmentType);
        Assert.Equal(EnglishSkill.Grammar, saved.TargetSkill);
        Assert.NotNull(saved.UpdatedAtUtc);
        Assert.Equal(courseId, saved.CourseId);
        var item = Assert.Single(saved.Questions);
        Assert.Equal(linkId, item.Id);
        Assert.Equal(2.5m, item.Points);
        Assert.Equal(0, item.OrderIndex);

        // Form cũ vẫn mang version null: phải bị từ chối, không ghi đè.
        input.Title = "Stale overwrite";
        var rejected = await SaveAsync(teacher.Id, id, input);
        AssertValidation(rejected, "tải lại trang");
        var after = await ReadAsync(id);
        Assert.Equal(saved.Title, after.Title);
        Assert.Equal(saved.UpdatedAtUtc, after.UpdatedAtUtc);
        Assert.Equal(courseId, after.CourseId);
        Assert.Equal(linkId, Assert.Single(after.Questions).Id);
        Assert.Empty(after.Attempts);
    }

    [Fact]
    public async Task InvalidInput_IsRejectedByServiceWithoutCreatingOrChangingDraft()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        var input = Request(AssessmentType.SkillAssessment);
        input.TargetSkill = null;

        var create = await SaveAsync(teacher.Id, null, input);
        Assert.False(create.Success);
        Assert.Contains(nameof(AssessmentDraftRequest.TargetSkill), create.Errors.Keys);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Assessments.AnyAsync(a => a.OwnerTeacherUserId == teacher.Id));
        }

        Guid id = await CreateAsync(teacher);
        input = Request(AssessmentType.PlacementTest);
        input.PassingScore = 70m;
        var update = await SaveAsync(teacher.Id, id, input);
        Assert.False(update.Success);
        Assert.Contains(nameof(AssessmentDraftRequest.PassingScore), update.Errors.Keys);
        var saved = await ReadAsync(id);
        Assert.Equal(AssessmentType.PracticeExam, saved.AssessmentType);
        Assert.Null(saved.UpdatedAtUtc);
    }

    [Fact]
    public async Task OtherTeacher_CannotListReadOrUpdateOwnersDraft()
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        var other = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        Guid id = await CreateAsync(owner);
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentDraftService>();

        Assert.Empty((await service.GetListAsync(other.Id)).Value!);
        Assert.Equal(LearningAccessResult.NotFound, (await service.GetDraftAsync(other.Id, id)).Access);
        Assert.Equal(LearningAccessResult.NotFound,
            (await service.SaveDraftAsync(other.Id, id, Request())).Access);
        Assert.Equal(owner.Id, (await ReadAsync(id)).OwnerTeacherUserId);
        Assert.Null((await ReadAsync(id)).UpdatedAtUtc);
    }

    [Theory]
    [InlineData(AppRoles.Student, AccountStatus.Active)]
    [InlineData(AppRoles.Admin, AccountStatus.Active)]
    [InlineData(AppRoles.Teacher, AccountStatus.Locked)]
    [InlineData(AppRoles.Teacher, AccountStatus.Disabled)]
    public async Task NonTeacherOrInactiveAccount_IsDeniedByService(string role, AccountStatus status)
    {
        var actor = await IdentityTestHelpers.CreateUserAsync(_factory, role);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users.Where(u => u.Id == actor.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.AccountStatus, status));
        var service = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentDraftService>();

        Assert.Equal(LearningAccessResult.Forbidden, (await service.GetListAsync(actor.Id)).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await service.GetDraftAsync(actor.Id, Guid.NewGuid())).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await service.SaveDraftAsync(actor.Id, null, Request())).Access);
        Assert.False(await db.Assessments.AnyAsync(a => a.OwnerTeacherUserId == actor.Id));
    }

    [Theory]
    [InlineData(AssessmentStatus.Published, false)]
    [InlineData(AssessmentStatus.Unpublished, false)]
    [InlineData(AssessmentStatus.Archived, false)]
    [InlineData(AssessmentStatus.Draft, true)]
    public async Task NonDraftOrExistingAttempt_CannotBeEdited(AssessmentStatus status, bool hasAttempt)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        Guid id = await CreateAsync(teacher);
        var student = hasAttempt
            ? await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student) : null;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Assessments.Where(a => a.Id == id)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.Status, status));
            if (student != null)
            {
                // Dữ liệu phòng vệ: kể cả Draft bất thường đã có Attempt cũng phải bị khóa.
                db.Attempts.Add(new Attempt
                {
                    AssessmentId = id, StudentUserId = student.Id,
                    StartedAtUtc = DateTimeOffset.UtcNow, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(15)
                });
                await db.SaveChangesAsync();
            }
        }

        using var checkScope = _factory.Services.CreateScope();
        var service = checkScope.ServiceProvider.GetRequiredService<ITeacherAssessmentDraftService>();
        Assert.Equal(LearningAccessResult.Forbidden, (await service.GetDraftAsync(teacher.Id, id)).Access);
        var listItem = Assert.Single((await service.GetListAsync(teacher.Id)).Value!);
        Assert.False(listItem.CanEditDraft);
        var input = Request();
        input.Title = "Must not be saved";
        AssertValidation(await service.SaveDraftAsync(teacher.Id, id, input), "Draft chưa có Attempt");
        var after = await ReadAsync(id);
        Assert.Equal("Test draft", after.Title);
        Assert.Equal(status, after.Status);
        Assert.Null(after.UpdatedAtUtc);
        Assert.Equal(hasAttempt ? 1 : 0, after.Attempts.Count);
    }

    [Fact]
    public async Task Http_CreateEditAndList_UseRealFormsAndIgnoreOwnerStatusAndCourseFromClient()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var client = await LoginAsync(teacher);
        using var emptyList = await client.GetAsync(IndexPath);
        Assert.Equal(HttpStatusCode.OK, emptyList.StatusCode);
        Assert.True(emptyList.Headers.CacheControl!.NoStore);
        Assert.Contains("href=\"/TeacherAssessments\"", await emptyList.Content.ReadAsStringAsync());
        var fields = await FormFieldsAsync(client, EditPath);

        // MVC phải trả lỗi đúng trường, không tạo Draft với SkillAssessment thiếu TargetSkill.
        fields["Input.AssessmentType"] = "SkillAssessment";
        using var invalid = await client.PostAsync(EditPath, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Skill Assessment phải chọn một kỹ năng", WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync()));

        fields["Input.AssessmentType"] = "PracticeExam";
        fields["Input.PassingScore"] = "72.25";
        fields["Input.OwnerTeacherUserId"] = Guid.NewGuid().ToString();
        fields["Input.Status"] = "Published";
        fields["Input.CourseId"] = Guid.NewGuid().ToString();
        using var create = await client.PostAsync(EditPath, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        string editUrl = create.Headers.Location!.ToString();
        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var saved = Assert.Single(await db.Assessments.AsNoTracking()
                .Where(a => a.OwnerTeacherUserId == teacher.Id).ToListAsync());
            id = saved.Id;
            Assert.Equal(AssessmentStatus.Draft, saved.Status);
            Assert.Null(saved.CourseId);
            Assert.Equal(72.25m, saved.PassingScore);
        }

        // Lấy version từ HTML thật, không tự đưa version vào thay cho form.
        for (int index = 0; index < 2; index++)
        {
            fields = await FormFieldsAsync(client, editUrl);
            fields["Input.Title"] = "HTTP updated " + index;
            fields["Input.DurationMinutes"] = "25";
            fields["Input.MaxAttempts"] = "4";
            using var update = await client.PostAsync(editUrl, new FormUrlEncodedContent(fields));
            Assert.Equal(HttpStatusCode.Redirect, update.StatusCode);
        }
        var after = await ReadAsync(id);
        Assert.Equal("HTTP updated 1", after.Title);
        Assert.Equal(25, after.DurationMinutes);
        Assert.Equal(4, after.MaxAttempts);
        Assert.Empty(after.Questions);
        Assert.Empty(after.Attempts);
        using var list = await client.GetAsync(IndexPath);
        Assert.Contains("HTTP updated 1", await list.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Http_GuestStudentForeignOwnerAndMissingCsrf_AreRejectedWithoutWrites()
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        var other = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student);
        Guid id = await CreateAsync(owner);
        string editUrl = EditPath + "/" + id;

        using var guest = IdentityTestHelpers.CreateClient(_factory);
        using var guestPage = await guest.GetAsync(IndexPath);
        Assert.Equal(HttpStatusCode.Redirect, guestPage.StatusCode);
        Assert.Contains("/Account/Login", guestPage.Headers.Location!.ToString());
        using var studentClient = await LoginAsync(student);
        using var denied = await studentClient.GetAsync(IndexPath);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("/Account/AccessDenied", denied.Headers.Location!.ToString());

        using var otherClient = await LoginAsync(other);
        using var foreignPage = await otherClient.GetAsync(editUrl);
        Assert.Equal(HttpStatusCode.NotFound, foreignPage.StatusCode);
        var fields = await FormFieldsAsync(otherClient, EditPath);
        using var foreignPost = await otherClient.PostAsync(editUrl, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.NotFound, foreignPost.StatusCode);

        using var ownerClient = await LoginAsync(owner);
        fields = await FormFieldsAsync(ownerClient, editUrl);
        fields.Remove("__RequestVerificationToken");
        using var csrf = await ownerClient.PostAsync(editUrl, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        Assert.Null((await ReadAsync(id)).UpdatedAtUtc);
    }

    [Fact]
    public async Task Http_ListFilters_CombineSearchTypeAndStatusWithoutExposingAnotherTeacher()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        var other = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Assessments.AddRange(
            new AssessmentEntity { OwnerTeacherUserId = teacher.Id, DurationMinutes = 20, MaxAttempts = 2, Title = "Grammar match", AssessmentType = AssessmentType.PracticeExam, Status = AssessmentStatus.Draft },
            new AssessmentEntity { OwnerTeacherUserId = teacher.Id, DurationMinutes = 20, MaxAttempts = 2, Title = "Grammar published", AssessmentType = AssessmentType.PracticeExam, Status = AssessmentStatus.Published },
            new AssessmentEntity { OwnerTeacherUserId = teacher.Id, DurationMinutes = 20, MaxAttempts = 2, Title = "Grammar placement", AssessmentType = AssessmentType.PlacementTest, Status = AssessmentStatus.Draft },
            new AssessmentEntity { OwnerTeacherUserId = other.Id, DurationMinutes = 20, MaxAttempts = 2, Title = "Grammar foreign private", AssessmentType = AssessmentType.PracticeExam, Status = AssessmentStatus.Draft });
        await db.SaveChangesAsync();
        using var client = await LoginAsync(teacher);
        using var response = await client.GetAsync(IndexPath + "?search=gRaMmAr&status=Draft&type=PracticeExam");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Grammar match", html);
        Assert.DoesNotContain("Grammar published", html);
        Assert.DoesNotContain("Grammar placement", html);
        Assert.DoesNotContain("Grammar foreign private", html);
        Assert.Single(Regex.Matches(html, "<article\\b[^>]*data-assessment-card"));
        using var invalid = await client.GetAsync(IndexPath + "?status=999");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    // Mỗi thao tác dùng scope mới như một request thật, tránh entity cache làm sai test.
    private async Task<WriteOutcome> SaveAsync(Guid actorId, Guid? id, AssessmentDraftRequest input)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentDraftService>()
            .SaveDraftAsync(actorId, id, input);
    }

    private async Task<Guid> CreateAsync(ApplicationUser teacher)
    {
        var result = await SaveAsync(teacher.Id, null, Request());
        Assert.True(result.Success);
        return result.Id;
    }

    private async Task<AssessmentEntity> ReadAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Assessments.AsNoTracking().AsSplitQuery()
            .Include(a => a.Questions).Include(a => a.Attempts).SingleAsync(a => a.Id == id);
    }

    private static AssessmentDraftRequest Request(AssessmentType type = AssessmentType.PracticeExam)
    {
        return new AssessmentDraftRequest
        {
            Title = "Test draft", AssessmentType = type,
            TargetSkill = type == AssessmentType.SkillAssessment ? EnglishSkill.Grammar : null,
            PassingScore = type == AssessmentType.SkillAssessment ? 70m : null,
            DurationMinutes = 20, MaxAttempts = 3
        };
    }

    private static void AssertValidation(WriteOutcome result, string text)
    {
        Assert.Equal(LearningAccessResult.Allowed, result.Access);
        Assert.False(result.Success);
        Assert.Contains(result.Errors.SelectMany(error => error.Value), message => message.Contains(text));
    }

    private async Task<HttpClient> LoginAsync(ApplicationUser user)
    {
        var client = IdentityTestHelpers.CreateClient(_factory);
        using var response = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static async Task<Dictionary<string, string>> FormFieldsAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        string html = await response.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        var versionInput = Regex.Match(html, "<input\\b[^>]*name=\"Input.ExpectedUpdatedAtUtc\"[^>]*>");
        Assert.True(token.Success);
        Assert.True(versionInput.Success);
        // Razor có thể bỏ thuộc tính value khi version null; input vẫn hợp lệ.
        var version = Regex.Match(versionInput.Value, "\\bvalue=\"([^\"]*)\"");
        return new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["Input.ExpectedUpdatedAtUtc"] = WebUtility.HtmlDecode(version.Groups[1].Value),
            ["Input.Title"] = "HTTP draft", ["Input.AssessmentType"] = "PracticeExam",
            ["Input.DurationMinutes"] = "20", ["Input.MaxAttempts"] = "3"
        };
    }
}
