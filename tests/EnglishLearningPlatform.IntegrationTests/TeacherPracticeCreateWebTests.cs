using System.Net;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Test gửi HTTP qua MVC thật, dùng cookie và antiforgery thật.
// Factory dùng SQL container/database test riêng, không sửa database demo.
public sealed class TeacherPracticeCreateWebTests : IClassFixture<IntegrationTestFactory>
{
    private const string CreatePath = "/TeacherPractices/Create";
    private readonly IntegrationTestFactory _factory;

    public TeacherPracticeCreateWebTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(AppRoles.Teacher)]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Admin)]
    public async Task Layout_QuestionBankLink_IsVisibleOnlyToTeacherAndRouteStillRequiresTeacher(string role)
    {
        var actor = await IdentityTestHelpers.CreateUserAsync(_factory, role);
        using var client = await LoginAsync(actor);
        using var profile = await client.GetAsync("/Profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var html = await profile.Content.ReadAsStringAsync();
        var navigationClass = role switch
        {
            AppRoles.Teacher => "studio-nav",
            AppRoles.Student => "studio-nav",
            AppRoles.Admin => "admin-nav",
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
        var navigation = Regex.Match(html, $"""<nav\b[^>]*\bclass="[^"]*\b{navigationClass}\b[^"]*"[^>]*>.*?</nav>""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        Assert.True(navigation.Success);
        var questionBankLinks = Regex.Matches(navigation.Value, "<a\\b[^>]*>.*?</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(match => match.Value)
            .Where(link => Attribute(link[..(link.IndexOf('>') + 1)], "href") == "/TeacherQuestionBank")
            .ToList();

        using var bank = await client.GetAsync("/TeacherQuestionBank");
        if (role == AppRoles.Teacher)
        {
            var link = Assert.Single(questionBankLinks);
            Assert.Contains("Ngân hàng câu hỏi", WebUtility.HtmlDecode(link));
            Assert.Equal(HttpStatusCode.OK, bank.StatusCode);
            Assert.True(bank.Headers.CacheControl!.NoStore);
        }
        else
        {
            Assert.Empty(questionBankLinks);
            Assert.Equal(HttpStatusCode.Redirect, bank.StatusCode);
            Assert.Contains("/Account/AccessDenied", bank.Headers.Location!.ToString());
        }
        await AssertNoPracticeAsync(actor.Id);
    }

    [Fact]
    public async Task Guest_LayoutDoesNotShowQuestionBankLinkAndDirectRouteRequiresLogin()
    {
        using var client = IdentityTestHelpers.CreateClient(_factory);
        using var loginPage = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        var html = await loginPage.Content.ReadAsStringAsync();
        Assert.DoesNotContain("href=\"/TeacherQuestionBank\"", html);

        using var bank = await client.GetAsync("/TeacherQuestionBank");

        Assert.Equal(HttpStatusCode.Redirect, bank.StatusCode);
        Assert.Contains("/Account/Login", bank.Headers.Location!.ToString());
    }

    [Fact]
    public async Task CreatePracticePage_QuestionBankMenuLink_OpensBankWithoutCreatingDraft()
    {
        var own = await SeedAsync();
        using var client = await LoginAsync(own.Teacher);
        using var form = await client.GetAsync(Path(own.CourseId));
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        var html = await form.Content.ReadAsStringAsync();
        var link = Assert.Single(Regex.Matches(html, "<a\\b[^>]*>", RegexOptions.IgnoreCase)
            .Select(match => Attribute(match.Value, "href")), href => href == "/TeacherQuestionBank");

        using var bank = await client.GetAsync(link);

        Assert.Equal(HttpStatusCode.OK, bank.StatusCode);
        Assert.Matches("<h1\\b[^>]*>\\s*Ngân hàng câu hỏi\\s*</h1>",
            WebUtility.HtmlDecode(await bank.Content.ReadAsStringAsync()));
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Guest_GetOrPost_RedirectsToLogin(bool post)
    {
        using var client = IdentityTestHelpers.CreateClient(_factory);

        using var response = post
            ? await client.PostAsync(CreatePath, new FormUrlEncodedContent(new Dictionary<string, string>()))
            : await client.GetAsync(CreatePath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData(AppRoles.Student, false)]
    [InlineData(AppRoles.Student, true)]
    [InlineData(AppRoles.Admin, false)]
    [InlineData(AppRoles.Admin, true)]
    public async Task NonTeacher_GetOrPost_IsDenied(string role, bool post)
    {
        var actor = await IdentityTestHelpers.CreateUserAsync(_factory, role);
        using var client = await LoginAsync(actor);

        using var response = post
            ? await client.PostAsync(CreatePath, new FormUrlEncodedContent(new Dictionary<string, string>()))
            : await client.GetAsync(CreatePath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
        await AssertNoPracticeAsync(actor.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CourseIndex_CreateDraftLink_OpensFormEvenWithoutCoursesAndDoesNotWrite(bool hasCourse)
    {
        var teacher = hasCourse
            ? (await SeedAsync()).Teacher
            : await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var client = await LoginAsync(teacher);

        using var index = await client.GetAsync("/TeacherCourses");

        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        var href = Assert.Single(CreateLinks(await index.Content.ReadAsStringAsync()));
        Assert.Equal(CreatePath, href);
        using var form = await client.GetAsync(href);
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        var html = await form.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(TokenFromCreateForm(html)));
        Assert.DoesNotContain("name=\"Input.ModuleId\"", CreateForm(html, "post"));
        Assert.DoesNotContain("name=\"Input.LessonId\"", CreateForm(html, "post"));
        await AssertNoPracticeAsync(teacher.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CourseManage_ParentLinks_AppearOncePerParentAndPrefillCorrectForm(int lessonCount)
    {
        var own = await SeedAsync();
        var lessonIds = new List<Guid>();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (lessonCount == 0)
            {
                await db.Lessons.Where(l => l.Id == own.LessonId).ExecuteDeleteAsync();
            }
            else
            {
                lessonIds.Add(own.LessonId);
            }
            if (lessonCount == 2)
            {
                var secondLesson = new Lesson
                {
                    ModuleId = own.ModuleId, Title = "Second lesson", OrderIndex = 1,
                    Status = LessonStatus.Draft
                };
                db.Lessons.Add(secondLesson);
                lessonIds.Add(secondLesson.Id);
            }
            // Luôn thêm một Module rỗng để phát hiện nút Module bị đặt
            // nhầm bên trong vòng lặp Lesson và biến mất khi không có Lesson.
            var emptyModule = new Module
            {
                CourseId = own.CourseId, Title = "Empty second module", OrderIndex = 1,
                Visibility = ModuleVisibility.Hidden
            };
            db.Modules.Add(emptyModule);
            await db.SaveChangesAsync();
        }

        using var client = await LoginAsync(own.Teacher);
        using var response = await client.GetAsync("/TeacherCourses/Manage/" + own.CourseId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var links = CreateLinks(await response.Content.ReadAsStringAsync()).ToList();
        var moduleLinks = links.Where(href => LinkQuery(href).ContainsKey("moduleId")).ToList();
        var lessonLinks = links.Where(href => LinkQuery(href).ContainsKey("lessonId")).ToList();
        Assert.Equal(2, moduleLinks.Count);
        Assert.Equal(lessonCount, lessonLinks.Count);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var moduleIds = await db.Modules.Where(m => m.CourseId == own.CourseId).Select(m => m.Id).ToListAsync();
            foreach (var id in moduleIds)
            {
                Assert.Single(moduleLinks, href => LinkQuery(href)["moduleId"].ToString() == id.ToString());
            }
            foreach (var id in lessonIds)
            {
                Assert.Single(lessonLinks, href => LinkQuery(href)["lessonId"].ToString() == id.ToString());
            }
        }

        foreach (var href in links)
        {
            var query = LinkQuery(href);
            Assert.Equal(2, query.Count);
            Assert.Equal(own.CourseId.ToString(), query["courseId"].ToString());
            Assert.NotEqual(query.ContainsKey("moduleId"), query.ContainsKey("lessonId"));
            using var form = await client.GetAsync(href);
            Assert.Equal(HttpStatusCode.OK, form.StatusCode);
            var html = await form.Content.ReadAsStringAsync();
            if (query.ContainsKey("moduleId"))
                AssertSelected(html, "Input.ModuleId", Guid.Parse(query["moduleId"].ToString()));
            else
                AssertSelected(html, "Input.LessonId", Guid.Parse(query["lessonId"].ToString()));
        }
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Fact]
    public async Task Get_DefaultPage_ShowsOnlyOwnedCoursesAndSeparateGetPostFormsWithoutWriting()
    {
        var own = await SeedAsync();
        var otherOwn = await SeedAsync(owner: own.Teacher);
        var foreign = await SeedAsync();
        using var client = await LoginAsync(own.Teacher);

        using var response = await client.GetAsync(CreatePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var html = await response.Content.ReadAsStringAsync();
        var getForm = CreateForm(html, "get");
        var postForm = CreateForm(html, "post");
        Assert.Contains(own.CourseId.ToString(), SelectMarkup(getForm, "CourseId"));
        Assert.Contains(otherOwn.CourseId.ToString(), SelectMarkup(getForm, "CourseId"));
        Assert.DoesNotContain(foreign.CourseId.ToString(), html);
        Assert.DoesNotContain(foreign.CourseTitle, WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("__RequestVerificationToken", getForm);
        Assert.Single(InputTags(postForm), tag => Attribute(tag, "name") == "__RequestVerificationToken");
        Assert.Contains("name=\"Input.Title\"", postForm);
        Assert.Contains("name=\"Input.Description\"", postForm);
        var hiddenCourse = Assert.Single(InputTags(postForm), tag => Attribute(tag, "name") == "CourseId");
        Assert.Equal("hidden", Attribute(hiddenCourse, "type"));
        Assert.Equal("PracticeCourseId", Attribute(hiddenCourse, "id"));
        Assert.DoesNotContain("name=\"Input.ModuleId\"", postForm);
        Assert.DoesNotContain("name=\"Input.LessonId\"", postForm);
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Fact]
    public async Task Get_TeacherWithoutCourses_CanOpenUnattachedDraftForm()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var client = await LoginAsync(teacher);

        using var response = await client.GetAsync(CreatePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Bạn chưa có khóa học", WebUtility.HtmlDecode(html));
        Assert.Single(Regex.Matches(SelectMarkup(html, "CourseId"), "<option\\b", RegexOptions.IgnoreCase));
        Assert.False(string.IsNullOrEmpty(TokenFromCreateForm(html)));
        await AssertNoPracticeAsync(teacher.Id);
    }

    [Theory]
    [InlineData(CourseStatus.Draft)]
    [InlineData(CourseStatus.Published)]
    [InlineData(CourseStatus.Unpublished)]
    [InlineData(CourseStatus.Archived)]
    public async Task Get_SelectedCourse_ShowsItsParentsIncludingHiddenDraftContent(CourseStatus status)
    {
        var own = await SeedAsync(status);
        var otherOwn = await SeedAsync(owner: own.Teacher);
        using var client = await LoginAsync(own.Teacher);

        using var response = await client.GetAsync(Path(own.CourseId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var modules = SelectMarkup(html, "Input.ModuleId");
        var lessons = SelectMarkup(html, "Input.LessonId");
        Assert.Contains(own.ModuleId.ToString(), modules);
        Assert.Contains(own.LessonId.ToString(), lessons);
        Assert.DoesNotContain(otherOwn.ModuleId.ToString(), modules);
        Assert.DoesNotContain(otherOwn.LessonId.ToString(), lessons);
        Assert.Contains(own.ModuleTitle + " — " + own.LessonTitle, WebUtility.HtmlDecode(lessons));
        Assert.Contains(own.CourseTitle, WebUtility.HtmlDecode(html));
        Assert.Contains("value=\"" + own.CourseId + "\"", CreateForm(html, "post"));
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Theory]
    [InlineData("Lesson")]
    [InlineData("Module")]
    public async Task Get_ParentFromOwnedCourse_IsPreselectedWithoutCreatingPractice(string parent)
    {
        var own = await SeedAsync();
        using var client = await LoginAsync(own.Teacher);
        var id = parent == "Lesson" ? own.LessonId : own.ModuleId;
        var field = parent == "Lesson" ? "Input.LessonId" : "Input.ModuleId";

        using var response = await client.GetAsync(Path(own.CourseId) + "&" + parent.ToLowerInvariant() + "Id=" + id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSelected(await response.Content.ReadAsStringAsync(), field, id);
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Theory]
    [InlineData("courseId=abc")]
    [InlineData("courseId=00000000-0000-0000-0000-000000000000")]
    [InlineData("lessonId=abc")]
    [InlineData("moduleId=abc")]
    [InlineData("lessonId=11111111-1111-1111-1111-111111111111")]
    [InlineData("moduleId=11111111-1111-1111-1111-111111111111")]
    public async Task Get_InvalidQueryOrParentWithoutCourse_ReturnsBadRequest(string query)
    {
        var own = await SeedAsync();
        using var client = await LoginAsync(own.Teacher);

        using var response = await client.GetAsync(CreatePath + "?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetOrPost_ForeignCourse_IsDeniedWithoutRevealingMetadata(bool post)
    {
        var own = await SeedAsync();
        var foreign = await SeedAsync();
        using var client = await LoginAsync(own.Teacher);
        using var response = post
            ? await PostAsync(client, null, Form(foreign.CourseId, "None", foreign))
            : await client.GetAsync(Path(foreign.CourseId));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
        Assert.DoesNotContain(foreign.CourseTitle, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        await AssertNoPracticeAsync(own.Teacher.Id);
        await AssertNoPracticeAsync(foreign.Teacher.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetOrPost_NonexistentCourse_ReturnsNotFound(bool post)
    {
        var own = await SeedAsync();
        using var client = await LoginAsync(own.Teacher);
        var missing = Guid.NewGuid();
        using var response = post
            ? await PostAsync(client, null, Form(missing, "None", own))
            : await client.GetAsync(Path(missing));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("UnattachedInSelectedCourse")]
    [InlineData("Lesson")]
    [InlineData("Module")]
    public async Task Post_ValidForm_SavesOneOwnedDraftAndRedirectsWithoutCreatingResult(string parent)
    {
        var own = await SeedAsync();
        using var client = await LoginAsync(own.Teacher);
        Guid? courseId = parent == "None" ? null : own.CourseId;
        var form = Form(courseId, parent, own);
        form["Input.Title"] = "  My new draft  ";
        form["Input.Description"] = "  Description  ";

        using var response = await PostAsync(client, courseId, form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        if (parent is "Lesson" or "Module")
            Assert.Equal("/TeacherCourses/Manage/" + own.CourseId, response.Headers.Location!.ToString());
        else
        {
            Assert.StartsWith("/TeacherCourses", response.Headers.Location!.ToString());
            Assert.DoesNotContain("/Manage", response.Headers.Location.ToString());
        }
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = Assert.Single(await db.Practices.AsNoTracking().Where(p => p.OwnerTeacherUserId == own.Teacher.Id).ToListAsync());
        Assert.Equal(PracticeStatus.Draft, saved.Status);
        Assert.Equal("My new draft", saved.Title);
        Assert.Equal("Description", saved.Description);
        Assert.Equal(parent == "Lesson" ? own.LessonId : (Guid?)null, saved.LessonId);
        Assert.Equal(parent == "Module" ? own.ModuleId : (Guid?)null, saved.ModuleId);
        Assert.False(await db.PracticeQuestions.AnyAsync(q => q.PracticeId == saved.Id));
        Assert.False(await db.PracticeSubmissions.AnyAsync(s => s.PracticeId == saved.Id));
        Assert.False(await db.Attempts.AnyAsync(a => a.StudentUserId == own.Teacher.Id));
    }

    [Theory]
    [InlineData("BlankTitle", "Input.Title")]
    [InlineData("LongTitle", "Input.Title")]
    [InlineData("LongDescription", "Input.Description")]
    [InlineData("MalformedLesson", "Input.LessonId")]
    [InlineData("MalformedModule", "Input.ModuleId")]
    [InlineData("EmptyLesson", "Input.LessonId")]
    [InlineData("EmptyModule", "Input.ModuleId")]
    [InlineData("BothParents", "Input.LessonId")]
    [InlineData("MalformedCourse", "CourseId")]
    public async Task Post_InvalidForm_ShowsFieldErrorAndDoesNotSave(string fault, string field)
    {
        var own = await SeedAsync();
        using var client = await LoginAsync(own.Teacher);
        var form = Form(own.CourseId, "None", own);
        switch (fault)
        {
            case "BlankTitle": form["Input.Title"] = "   "; break;
            case "LongTitle": form["Input.Title"] = new string('a', 201); break;
            case "LongDescription": form["Input.Description"] = new string('a', 4001); break;
            case "MalformedLesson": form["Input.LessonId"] = "abc"; break;
            case "MalformedModule": form["Input.ModuleId"] = "abc"; break;
            case "EmptyLesson": form["Input.LessonId"] = Guid.Empty.ToString(); break;
            case "EmptyModule": form["Input.ModuleId"] = Guid.Empty.ToString(); break;
            case "BothParents": form["Input.LessonId"] = own.LessonId.ToString(); form["Input.ModuleId"] = own.ModuleId.ToString(); break;
            case "MalformedCourse": form["CourseId"] = "abc"; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }

        using var response = await PostAsync(client, own.CourseId, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertFieldError(await response.Content.ReadAsStringAsync(), field);
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Fact]
    public async Task Post_EmptyCourseId_IsBadRequestAndNotSaved()
    {
        var own = await SeedAsync();
        using var client = await LoginAsync(own.Teacher);

        using var response = await PostAsync(client, own.CourseId, Form(Guid.Empty, "None", own));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Theory]
    [InlineData("Lesson", false)]
    [InlineData("Module", false)]
    [InlineData("Lesson", true)]
    [InlineData("Module", true)]
    public async Task Post_ParentFromAnotherCourse_IsRejectedEvenWhenSameTeacherOwnsBoth(string parent, bool sameOwner)
    {
        var own = await SeedAsync();
        var other = await SeedAsync(owner: sameOwner ? own.Teacher : null);
        using var client = await LoginAsync(own.Teacher);
        var form = Form(own.CourseId, parent, other);

        using var response = await PostAsync(client, own.CourseId, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        AssertFieldError(html, parent == "Lesson" ? "Input.LessonId" : "Input.ModuleId");
        Assert.DoesNotContain(other.ModuleId.ToString(), SelectMarkup(html, "Input.ModuleId"));
        Assert.DoesNotContain(other.LessonId.ToString(), SelectMarkup(html, "Input.LessonId"));
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Theory]
    [InlineData("Lesson")]
    [InlineData("Module")]
    public async Task Get_ParentFromAnotherCourse_IsBadRequest(string parent)
    {
        var own = await SeedAsync();
        var other = await SeedAsync(owner: own.Teacher);
        using var client = await LoginAsync(own.Teacher);
        var id = parent == "Lesson" ? other.LessonId : other.ModuleId;

        using var response = await client.GetAsync(Path(own.CourseId) + "&" + parent.ToLowerInvariant() + "Id=" + id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Theory]
    [InlineData("Lesson")]
    [InlineData("Module")]
    public async Task Post_ParentWithoutCourseContext_ReturnsValidationErrorAndDoesNotSave(string parent)
    {
        var own = await SeedAsync();
        using var client = await LoginAsync(own.Teacher);

        using var response = await PostAsync(client, null, Form(null, parent, own));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertFieldError(await response.Content.ReadAsStringAsync(), "CourseId");
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Fact]
    public async Task Post_BothParents_PreservesInputDropdownsSelectionAndBothBackendErrors()
    {
        var own = await SeedAsync();
        var foreign = await SeedAsync();
        using var client = await LoginAsync(own.Teacher);
        var form = Form(own.CourseId, "Lesson", own);
        form["Input.ModuleId"] = own.ModuleId.ToString();
        form["Input.Title"] = "Keep this title";
        form["Input.Description"] = "Keep this description";

        using var response = await PostAsync(client, own.CourseId, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        AssertFieldError(html, "Input.LessonId");
        AssertFieldError(html, "Input.ModuleId");
        AssertSelected(html, "Input.LessonId", own.LessonId);
        AssertSelected(html, "Input.ModuleId", own.ModuleId);
        var title = Assert.Single(InputTags(CreateForm(html, "post")), tag => Attribute(tag, "name") == "Input.Title");
        Assert.Equal("Keep this title", Attribute(title, "value"));
        Assert.Contains("Keep this description", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain(foreign.CourseId.ToString(), html);
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_MissingOrInvalidAntiforgeryToken_IsBadRequestAndDoesNotSave(bool invalid)
    {
        var own = await SeedAsync();
        using var client = await LoginAsync(own.Teacher);
        // Tạo cookie antiforgery nhưng không gửi token hợp lệ trong POST.
        await TokenAsync(client, Path(own.CourseId));
        var form = Form(own.CourseId, "Lesson", own);
        if (invalid) form["__RequestVerificationToken"] = "INVALID_TEST_TOKEN";

        using var response = await client.PostAsync(CreatePath, new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Fact]
    public async Task Post_AntiforgeryTokenFromAnotherAccount_IsRejected()
    {
        var own = await SeedAsync();
        var foreign = await SeedAsync();
        using var client = await LoginAsync(own.Teacher);
        using var foreignClient = await LoginAsync(foreign.Teacher);
        await TokenAsync(client, CreatePath);
        var form = Form(own.CourseId, "Lesson", own);
        form["__RequestVerificationToken"] = await TokenAsync(foreignClient, CreatePath);

        using var response = await client.PostAsync(CreatePath, new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Theory]
    [InlineData(AccountStatus.Locked, false)]
    [InlineData(AccountStatus.Disabled, false)]
    [InlineData(AccountStatus.Locked, true)]
    [InlineData(AccountStatus.Disabled, true)]
    public async Task AccountDisabledAfterLogin_GetOrPostRejectsStaleCookie(AccountStatus status, bool post)
    {
        var own = await SeedAsync();
        using var client = await LoginAsync(own.Teacher);
        var form = Form(own.CourseId, "Lesson", own);
        form["__RequestVerificationToken"] = await TokenAsync(client, Path(own.CourseId));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users.Where(u => u.Id == own.Teacher.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.AccountStatus, status));

        using var response = post
            ? await client.PostAsync(CreatePath, new FormUrlEncodedContent(form))
            : await client.GetAsync(Path(own.CourseId));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoleChangedAfterLogin_GetOrPostRejectsStaleTeacherCookie(bool post)
    {
        var own = await SeedAsync();
        using var client = await LoginAsync(own.Teacher);
        var form = Form(own.CourseId, "Lesson", own);
        form["__RequestVerificationToken"] = await TokenAsync(client, Path(own.CourseId));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.UserRoles.Where(r => r.UserId == own.Teacher.Id).ExecuteDeleteAsync();
        var studentRoleId = await db.Roles.Where(r => r.Name == AppRoles.Student).Select(r => r.Id).SingleAsync();
        db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>
        {
            UserId = own.Teacher.Id, RoleId = studentRoleId
        });
        await db.SaveChangesAsync();

        using var response = post
            ? await client.PostAsync(CreatePath, new FormUrlEncodedContent(form))
            : await client.GetAsync(Path(own.CourseId));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Fact]
    public async Task Post_ForgedOwnerStatusIdAndDisplayOptions_AreIgnoredWithoutModifyingExistingDrafts()
    {
        var own = await SeedAsync();
        var foreign = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownExisting = new Practice { OwnerTeacherUserId = own.Teacher.Id, Title = "EXISTING_SELF" };
        var foreignExisting = new Practice { OwnerTeacherUserId = foreign.Teacher.Id, Title = "EXISTING_FOREIGN" };
        db.Practices.AddRange(ownExisting, foreignExisting);
        await db.SaveChangesAsync();
        using var client = await LoginAsync(own.Teacher);
        var form = Form(null, "None", own);
        form["Input.Title"] = "NEW_DRAFT";
        form["actorUserId"] = foreign.Teacher.Id.ToString();
        form["OwnerTeacherUserId"] = foreign.Teacher.Id.ToString();
        form["Input.OwnerTeacherUserId"] = foreign.Teacher.Id.ToString();
        form["Input.Status"] = "Published";
        form["Input.Id"] = foreignExisting.Id.ToString();
        form["CourseTitle"] = "FORGED_DISPLAY_TITLE";
        form["CourseOptions[0].Value"] = foreign.CourseId.ToString();
        form["ModuleOptions[0].Value"] = foreign.ModuleId.ToString();
        form["LessonOptions[0].Value"] = foreign.LessonId.ToString();

        using var response = await PostAsync(client, null, form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        db.ChangeTracker.Clear();
        var created = await db.Practices.AsNoTracking().SingleAsync(p => p.Title == "NEW_DRAFT");
        Assert.Equal(own.Teacher.Id, created.OwnerTeacherUserId);
        Assert.Equal(PracticeStatus.Draft, created.Status);
        Assert.Null(created.LessonId);
        Assert.Null(created.ModuleId);
        Assert.NotEqual(foreignExisting.Id, created.Id);
        Assert.Equal("EXISTING_SELF", (await db.Practices.AsNoTracking().SingleAsync(p => p.Id == ownExisting.Id)).Title);
        Assert.Equal("EXISTING_FOREIGN", (await db.Practices.AsNoTracking().SingleAsync(p => p.Id == foreignExisting.Id)).Title);
        Assert.Equal(2, await db.Practices.CountAsync(p => p.OwnerTeacherUserId == own.Teacher.Id));
        Assert.Equal(1, await db.Practices.CountAsync(p => p.OwnerTeacherUserId == foreign.Teacher.Id));
    }

    [Fact]
    public async Task Get_MetadataWithHtml_IsEncodedInHeadingAndDropdowns()
    {
        var own = await SeedAsync();
        const string courseText = "<script>alert('course')</script>";
        const string moduleText = "<img src=x onerror=alert('module')>";
        const string lessonText = "<svg onload=alert('lesson')>";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Courses.Where(c => c.Id == own.CourseId).ExecuteUpdateAsync(update => update.SetProperty(c => c.Title, courseText));
        await db.Modules.Where(m => m.Id == own.ModuleId).ExecuteUpdateAsync(update => update.SetProperty(m => m.Title, moduleText));
        await db.Lessons.Where(l => l.Id == own.LessonId).ExecuteUpdateAsync(update => update.SetProperty(l => l.Title, lessonText));
        using var client = await LoginAsync(own.Teacher);

        using var response = await client.GetAsync(Path(own.CourseId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        foreach (var text in new[] { courseText, moduleText, lessonText })
        {
            Assert.DoesNotContain(text, html);
            Assert.Contains(text, WebUtility.HtmlDecode(html));
        }
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Fact]
    public async Task Post_ValidationFailure_EncodesPostedTextAndIgnoresForgedDropdowns()
    {
        var own = await SeedAsync();
        var foreign = await SeedAsync();
        using var client = await LoginAsync(own.Teacher);
        const string title = "<script>alert('title')</script>";
        const string description = "<img src=x onerror=alert('description')>";
        var form = Form(own.CourseId, "Lesson", own);
        form["Input.ModuleId"] = own.ModuleId.ToString();
        form["Input.Title"] = title;
        form["Input.Description"] = description;
        form["CourseTitle"] = "FORGED_DISPLAY_TITLE";
        form["ModuleOptions[0].Value"] = foreign.ModuleId.ToString();
        form["LessonOptions[0].Value"] = foreign.LessonId.ToString();

        using var response = await PostAsync(client, own.CourseId, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(title, html);
        Assert.DoesNotContain(description, html);
        Assert.Contains(title, WebUtility.HtmlDecode(html));
        Assert.Contains(description, WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("FORGED_DISPLAY_TITLE", html);
        Assert.DoesNotContain(foreign.ModuleId.ToString(), SelectMarkup(html, "Input.ModuleId"));
        Assert.DoesNotContain(foreign.LessonId.ToString(), SelectMarkup(html, "Input.LessonId"));
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Theory]
    [InlineData(LearningAccessResult.Forbidden)]
    [InlineData(LearningAccessResult.NotFound)]
    public async Task Post_BackendDenial_IsMappedToHttpWithoutReportingSuccess(LearningAccessResult access)
    {
        var own = await SeedAsync();
        var backend = new FixedResultPracticeService(WriteOutcome.Denied(access));
        using var app = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITeacherPracticeService>();
            services.AddSingleton<ITeacherPracticeService>(backend);
        }));
        using var client = IdentityTestHelpers.CreateClient(app);
        using var login = await IdentityTestHelpers.LoginAsync(client, own.Teacher);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await PostAsync(client, own.CourseId, Form(own.CourseId, "Lesson", own));

        if (access == LearningAccessResult.NotFound)
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        else
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
        }
        Assert.Equal(1, backend.Calls);
        Assert.Equal(own.Teacher.Id, backend.ActorUserId);
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    [Fact]
    public async Task Post_BackendFieldAndGlobalErrors_AreDisplayedWithoutSaving()
    {
        var own = await SeedAsync();
        var backend = new FixedResultPracticeService(new WriteOutcome(LearningAccessResult.Allowed, Guid.Empty,
            new Dictionary<string, string[]>
            {
                ["Title"] = new[] { "TEST_FIELD_ERROR" },
                [string.Empty] = new[] { "TEST_GLOBAL_ERROR" }
            }));
        using var app = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITeacherPracticeService>();
            services.AddSingleton<ITeacherPracticeService>(backend);
        }));
        using var client = IdentityTestHelpers.CreateClient(app);
        using var login = await IdentityTestHelpers.LoginAsync(client, own.Teacher);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await PostAsync(client, own.CourseId, Form(own.CourseId, "Lesson", own));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        AssertFieldError(html, "Input.Title");
        Assert.Contains("TEST_FIELD_ERROR", html);
        Assert.Contains("TEST_GLOBAL_ERROR", html);
        AssertSelected(html, "Input.LessonId", own.LessonId);
        Assert.Equal(1, backend.Calls);
        await AssertNoPracticeAsync(own.Teacher.Id);
    }

    private async Task<Data> SeedAsync(CourseStatus status = CourseStatus.Draft, ApplicationUser? owner = null)
    {
        var teacher = owner ?? await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        string suffix = Guid.NewGuid().ToString("N");
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "COURSE_" + suffix, Status = status };
        var module = new Module { Course = course, Title = "MODULE_" + suffix, Visibility = ModuleVisibility.Hidden };
        var lesson = new Lesson { Module = module, Title = "LESSON_" + suffix, Status = LessonStatus.Draft };
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();
        return new Data(teacher, course.Id, module.Id, lesson.Id, course.Title, module.Title, lesson.Title);
    }

    private async Task<HttpClient> LoginAsync(ApplicationUser user)
    {
        var client = IdentityTestHelpers.CreateClient(_factory);
        using var response = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static string Path(Guid? courseId) => courseId.HasValue
        ? CreatePath + "?courseId=" + courseId.Value
        : CreatePath;

    private static Dictionary<string, string> Form(Guid? courseId, string parent, Data data) => new()
    {
        ["CourseId"] = courseId?.ToString() ?? string.Empty,
        ["Input.Title"] = "My new draft",
        ["Input.Description"] = "Description",
        ["Input.LessonId"] = parent == "Lesson" ? data.LessonId.ToString() : string.Empty,
        ["Input.ModuleId"] = parent == "Module" ? data.ModuleId.ToString() : string.Empty
    };

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid? tokenCourseId, Dictionary<string, string> form)
    {
        form["__RequestVerificationToken"] = await TokenAsync(client, Path(tokenCourseId));
        return await client.PostAsync(CreatePath, new FormUrlEncodedContent(form));
    }

    private static async Task<string> TokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return TokenFromCreateForm(await response.Content.ReadAsStringAsync());
    }

    private static string TokenFromCreateForm(string html)
    {
        // Layout còn có form Logout: chỉ lấy token của form tạo Draft.
        var input = Assert.Single(InputTags(CreateForm(html, "post")),
            tag => Attribute(tag, "name") == "__RequestVerificationToken");
        string token = Attribute(input, "value");
        Assert.False(string.IsNullOrWhiteSpace(token));
        return token;
    }

    private static string CreateForm(string html, string method)
    {
        var forms = Regex.Matches(html, "<form\\b[^>]*>.*?</form>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(match => match.Value)
            .Where(form => Attribute(form[..(form.IndexOf('>') + 1)], "method") == method
                && Attribute(form[..(form.IndexOf('>') + 1)], "action").StartsWith(CreatePath, StringComparison.Ordinal))
            .ToList();
        return Assert.Single(forms);
    }

    private static IEnumerable<string> InputTags(string html) =>
        Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase).Select(match => match.Value);

    private static IEnumerable<string> CreateLinks(string html) =>
        Regex.Matches(html, "<a\\b[^>]*>", RegexOptions.IgnoreCase)
            .Select(match => Attribute(match.Value, "href"))
            .Where(href => href.StartsWith(CreatePath, StringComparison.Ordinal));

    private static Dictionary<string, Microsoft.Extensions.Primitives.StringValues> LinkQuery(string href)
    {
        var uri = new Uri(new Uri("http://localhost"), href);
        return QueryHelpers.ParseQuery(uri.Query);
    }

    private static string Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, "\\s" + Regex.Escape(name) + "=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : string.Empty;
    }

    private static string SelectMarkup(string html, string field)
    {
        var match = Regex.Match(html, "<select\\b[^>]*\\bname=\"" + Regex.Escape(field) + "\"[^>]*>.*?</select>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        Assert.True(match.Success, "Missing select for " + field);
        return match.Value;
    }

    private static void AssertSelected(string html, string field, Guid id)
    {
        var option = Assert.Single(Regex.Matches(SelectMarkup(html, field), "<option\\b[^>]*>", RegexOptions.IgnoreCase)
            .Select(match => match.Value), tag => Attribute(tag, "value") == id.ToString());
        Assert.Matches("\\sselected(?:=|\\s|>)", option);
    }

    private static void AssertFieldError(string html, string field)
    {
        var span = Regex.Match(html, "<span\\b[^>]*\\bdata-valmsg-for=\"" + Regex.Escape(field) + "\"[^>]*>.*?</span>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        Assert.True(span.Success, "Missing error span for " + field);
        Assert.Contains("field-validation-error", span.Value);
    }

    private async Task AssertNoPracticeAsync(Guid teacherId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Practices.AnyAsync(p => p.OwnerTeacherUserId == teacherId));
        Assert.False(await db.PracticeSubmissions.AnyAsync(s => s.Practice.OwnerTeacherUserId == teacherId));
    }

    private sealed record Data(ApplicationUser Teacher, Guid CourseId, Guid ModuleId, Guid LessonId,
        string CourseTitle, string ModuleTitle, string LessonTitle);

    private sealed class FixedResultPracticeService : ITeacherPracticeService
    {
        private readonly WriteOutcome _result;
        public int Calls { get; private set; }
        public Guid ActorUserId { get; private set; }
        public FixedResultPracticeService(WriteOutcome result) { _result = result; }
        public Task<WriteOutcome> CreateDraftAsync(Guid actorUserId, CreatePracticeRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            ActorUserId = actorUserId;
            return Task.FromResult(_result);
        }
    }
}
