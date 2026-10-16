using System.Net;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Một fixture SQL riêng; chỉ kiểm tra chức năng tạo và tích hợp dropdown hiện có.
public sealed class StimulusCreationTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    private const string CreatePath = "/TeacherStimuli/Create";

    // Kiểm tra lưu dữ liệu và dùng được ngay trong dropdown riêng của owner.
    [Theory]
    [InlineData(StimulusType.Passage)]
    [InlineData(StimulusType.Audio)]
    public async Task Create_ValidTeacher_OwnsNormalizedStimulusAndSeesItInOwnDropdown(StimulusType type)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = new CreateStimulusRequest { StimulusType = type, Title = "  New resource  ",
            ContentText = type == StimulusType.Passage ? "  Reading passage  " : null,
            ResourceUrl = type == StimulusType.Audio ? "  https://example.test/audio.mp3  " : null };

        var result = await scope.ServiceProvider.GetRequiredService<IStimulusCreationService>()
            .CreateAsync(teacher.Id, request);

        Assert.True(result.Success);
        db.ChangeTracker.Clear();
        var saved = await db.Stimuli.SingleAsync(s => s.Id == result.Id);
        Assert.Equal(teacher.Id, saved.OwnerTeacherUserId);
        Assert.Equal(type, saved.StimulusType);
        Assert.Equal("New resource", saved.Title);
        Assert.Equal(type == StimulusType.Passage ? "Reading passage" : null, saved.ContentText);
        Assert.Equal(type == StimulusType.Audio ? "https://example.test/audio.mp3" : null, saved.ResourceUrl);
        Assert.Null(saved.ContentLockedAtUtc);
        var queries = scope.ServiceProvider.GetRequiredService<IQuestionAuthoringService>();
        Assert.Contains(await queries.GetStimuliAsync(teacher.Id), s => s.Id == result.Id);
        Assert.DoesNotContain(await queries.GetStimuliAsync(other.Id), s => s.Id == result.Id);
    }

    // Có tài khoản không đồng nghĩa có quyền tạo ngữ liệu của Teacher.
    [Theory]
    [InlineData(AppRoles.Student, AccountStatus.Active)]
    [InlineData(AppRoles.Admin, AccountStatus.Active)]
    [InlineData(AppRoles.Teacher, AccountStatus.Locked)]
    public async Task Create_NotActiveTeacher_IsForbiddenWithoutInsert(string role, AccountStatus status)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.SingleAsync(u => u.Id == user.Id)).AccountStatus = status;
        await db.SaveChangesAsync();
        int before = await db.Stimuli.CountAsync();

        var result = await scope.ServiceProvider.GetRequiredService<IStimulusCreationService>()
            .CreateAsync(user.Id, ValidPassage());

        Assert.Equal(LearningAccessResult.Forbidden, result.Access);
        Assert.Equal(before, await db.Stimuli.CountAsync());
    }

    // Validation thất bại không được tạo bản ghi dở dang.
    [Fact]
    public async Task Create_InvalidRequest_ReturnsErrorsWithoutInsert()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        int before = await db.Stimuli.CountAsync();

        var result = await scope.ServiceProvider.GetRequiredService<IStimulusCreationService>()
            .CreateAsync(teacher.Id, new CreateStimulusRequest { StimulusType = StimulusType.Passage });

        Assert.False(result.Success);
        Assert.Contains(nameof(CreateStimulusRequest.ContentText), result.Errors.Keys);
        Assert.Equal(before, await db.Stimuli.CountAsync());
    }

    // Kiểm tra ranh giới HTTP, không chỉ quyền trong service.
    [Fact]
    public async Task Http_GuestAndStudent_CannotOpenTeacherForm()
    {
        using var guest = IdentityTestHelpers.CreateClient(factory);
        using var guestResponse = await guest.GetAsync(CreatePath);
        Assert.Equal(HttpStatusCode.Redirect, guestResponse.StatusCode);
        Assert.Contains("/Account/Login", guestResponse.Headers.Location!.ToString());
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = await LoginAsync(student);
        using var response = await client.GetAsync(CreatePath);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
    }

    // Đi qua form thật, chống overposting và kiểm tra điều hướng sau khi lưu.
    [Fact]
    public async Task Http_FormAndValidPost_IgnoreForgedOwnerAndLockAndExposeNewDropdownOption()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var client = await LoginAsync(teacher);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        int before = await db.Stimuli.CountAsync();
        using var get = await client.GetAsync(CreatePath);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.True(get.Headers.CacheControl!.NoStore);
        Assert.Contains("teacher-studio", await get.Content.ReadAsStringAsync());
        string token = await IdentityTestHelpers.GetTokenAsync(client, CreatePath);
        Assert.Equal(before, await db.Stimuli.CountAsync()); // Hai GET không được tạo dữ liệu.
        string title = "HTTP passage " + Guid.NewGuid().ToString("N");
        Guid forgedId = Guid.NewGuid();
        var fields = new Dictionary<string, string> {
            ["StimulusType"] = "1", ["Title"] = title, ["ContentText"] = "A reading passage.",
            ["OwnerTeacherUserId"] = other.Id.ToString(), ["actorUserId"] = other.Id.ToString(),
            ["Id"] = forgedId.ToString(), ["ContentLockedAtUtc"] = "2020-01-01T00:00:00Z",
            ["__RequestVerificationToken"] = token };

        using var post = await client.PostAsync(CreatePath, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Equal("/TeacherQuestionBank/Create", post.Headers.Location!.ToString());
        var saved = await db.Stimuli.AsNoTracking().SingleAsync(s => s.Title == title);
        Assert.Equal(teacher.Id, saved.OwnerTeacherUserId);
        Assert.NotEqual(forgedId, saved.Id);
        Assert.Null(saved.ContentLockedAtUtc);
        Assert.Equal(before + 1, await db.Stimuli.CountAsync());
        using var questionForm = await client.GetAsync(post.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, questionForm.StatusCode);
        Assert.Contains($"value=\"{saved.Id}\"", await questionForm.Content.ReadAsStringAsync());
        using var bank = await client.GetAsync("/TeacherQuestionBank");
        Assert.Contains("href=\"/TeacherStimuli/Create\"", await bank.Content.ReadAsStringAsync());
    }

    // Cả lỗi chuyển kiểu MVC và lỗi validation đều giữ nội dung được HTML-encode.
    [Theory]
    [InlineData("abc", "")]
    [InlineData("2", "javascript:alert(1)")]
    public async Task Http_InvalidInput_KeepsEncodedValuesWithoutInsert(string type, string url)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var client = await LoginAsync(teacher);
        string token = await IdentityTestHelpers.GetTokenAsync(client, CreatePath);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        int before = await db.Stimuli.CountAsync();
        const string title = "<script>alert('title')</script>";
        using var response = await client.PostAsync(CreatePath, new FormUrlEncodedContent(
            new Dictionary<string, string> { ["StimulusType"] = type, ["Title"] = title,
                ["ResourceUrl"] = url, ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains(title, WebUtility.HtmlDecode(html));
        Assert.DoesNotContain(title, html);
        Assert.Contains("field-validation-error", html);
        Assert.Equal(before, await db.Stimuli.CountAsync());
    }

    // Cookie đăng nhập không thay thế được token chống giả mạo form.
    [Fact]
    public async Task Http_PostWithoutAntiforgeryToken_IsRejectedWithoutInsert()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var client = await LoginAsync(teacher);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        int before = await db.Stimuli.CountAsync();

        using var response = await client.PostAsync(CreatePath, new FormUrlEncodedContent(
            new Dictionary<string, string> { ["StimulusType"] = "1", ["ContentText"] = "Passage" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await db.Stimuli.CountAsync());
    }

    // Dùng luồng đăng nhập có sẵn, không bỏ qua authentication trong test.
    private async Task<HttpClient> LoginAsync(ApplicationUser user)
    {
        var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    // Mẫu hợp lệ dùng cho các test chỉ tập trung vào kiểm tra quyền.
    private static CreateStimulusRequest ValidPassage() => new() {
        StimulusType = StimulusType.Passage, Title = "Passage", ContentText = "Reading passage" };
}
