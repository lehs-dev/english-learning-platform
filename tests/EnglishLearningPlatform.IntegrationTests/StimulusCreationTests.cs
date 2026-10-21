using System.Net;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Một fixture SQL riêng; kiểm tra tạo ngữ liệu và sử dụng qua các màn hình thật.
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

    // Chỉ hai ca cho toàn luồng: Passage/Reading và Audio/Listening.
    // Tạo qua form thật rồi dùng chung một Stimulus, không seed sẵn bản sao cho từng câu.
    [Theory]
    [InlineData(StimulusType.Passage)]
    [InlineData(StimulusType.Audio)]
    public async Task SharedStimulus_CreatedThroughForms_IsConsistentInDetailsPreviewAndStudentSheets(
        StimulusType type)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var teacherClient = await LoginAsync(teacher);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        string title = "T1 shared stimulus " + Guid.NewGuid().ToString("N");
        string media = type == StimulusType.Passage
            ? "T1 shared passage <b>plain text</b>\nSecond line."
            : "https://media.example.test/t1-shared.mp3?version=1&lang=en";
        EnglishSkill skill = type == StimulusType.Passage
            ? EnglishSkill.Reading : EnglishSkill.Listening;

        // Teacher tạo ngữ liệu qua HTTP; server quyết định owner và Id.
        string token = await IdentityTestHelpers.GetTokenAsync(teacherClient, CreatePath);
        var stimulusFields = new Dictionary<string, string>
            {
                ["StimulusType"] = ((int)type).ToString(), ["Title"] = title,
                ["ContentText"] = type == StimulusType.Passage ? media : "",
                ["__RequestVerificationToken"] = token
            };
        using var multipart = new MultipartFormDataContent();
        foreach (var field in stimulusFields) multipart.Add(new StringContent(field.Value), field.Key);
        if (type == StimulusType.Audio)
            multipart.Add(new ByteArrayContent(StimulusAudioTestData.Wav()), "audioFile", "test.wav");
        using var create = await teacherClient.PostAsync(CreatePath, multipart);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.Equal("/TeacherQuestionBank/Create", create.Headers.Location!.ToString());
        Guid stimulusId = await db.Stimuli.Where(s => s.Title == title).Select(s => s.Id).SingleAsync();
        if (type == StimulusType.Audio)
            media = (await db.Stimuli.Where(s => s.Id == stimulusId).Select(s => s.ResourceUrl).SingleAsync())!;

        // Đủ 5 câu khác nhau để Publish Assessment; tất cả trỏ tới cùng một Stimulus.
        var questionIds = new List<Guid>();
        for (int index = 0; index < 5; index++)
        {
            string content = title + " question " + index;
            token = await IdentityTestHelpers.GetTokenAsync(teacherClient, "/TeacherQuestionBank/Create");
            using var questionPost = await teacherClient.PostAsync("/TeacherQuestionBank/Create",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Content"] = content, ["PrimarySkill"] = ((int)skill).ToString(),
                    ["StimulusId"] = stimulusId.ToString(), ["Options[0].Content"] = "Choice A",
                    ["Options[1].Content"] = "Choice B", ["CorrectOptionIndex"] = "0",
                    ["__RequestVerificationToken"] = token
                }));
            Assert.Equal(HttpStatusCode.Redirect, questionPost.StatusCode);
            Assert.Equal("/TeacherQuestionBank", questionPost.Headers.Location!.ToString());
            Guid questionId = await db.Questions.Where(q => q.Content == content).Select(q => q.Id).SingleAsync();
            questionIds.Add(questionId);
            await AssertSharedMediaAsync(teacherClient, $"/TeacherQuestionBank/Details/{questionId}", type, media, 1);
        }

        // Tái sử dụng service soạn đề, không tự chèn AssessmentQuestion để bỏ qua validation.
        var draft = await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentDraftService>()
            .SaveDraftAsync(teacher.Id, null, new AssessmentDraftRequest
            {
                Title = title, AssessmentType = AssessmentType.PracticeExam,
                DurationMinutes = 30, MaxAttempts = 2
            });
        Assert.True(draft.Success);
        var questionService = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentQuestionService>();
        var editor = await questionService.GetEditorAsync(teacher.Id, draft.Id);
        Assert.NotNull(editor.Value);
        var saved = await questionService.SaveAsync(teacher.Id, draft.Id, new UpdateAssessmentQuestionsRequest
        {
            ExpectedUpdatedAtUtc = editor.Value.UpdatedAtUtc,
            Items = questionIds.Select(id => new AssessmentQuestionInput { QuestionId = id, Points = 1m }).ToList()
        });
        Assert.True(saved.Success);
        string previewPath = $"/TeacherAssessmentPreview/Index/{draft.Id}";
        await AssertSharedMediaAsync(teacherClient, previewPath, type, media, 5);
        await AssertSharedMediaAsync(teacherClient, previewPath, type, media, 5);
        Assert.False(await db.Attempts.AnyAsync(a => a.AssessmentId == draft.Id));

        var version = await db.Assessments.Where(a => a.Id == draft.Id).Select(a => a.UpdatedAtUtc).SingleAsync();
        var published = await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentPublishService>()
            .PublishAsync(teacher.Id, draft.Id, version);
        Assert.True(published.Success);
        await AssertSharedMediaAsync(teacherClient, previewPath, type, media, 5);
        Assert.False(await db.Attempts.AnyAsync(a => a.AssessmentId == draft.Id));

        // Seed tối thiểu Course/Lesson/Enrollment vì T1 không thay đổi chức năng soạn Course.
        // Practice Draft vẫn chọn câu và Publish bằng service thật.
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = title, Status = CourseStatus.Published };
        var module = new Module { Course = course, Title = "T1 module", Visibility = ModuleVisibility.Visible };
        var lesson = new Lesson { Module = module, Title = "T1 lesson", Status = LessonStatus.Published };
        var practice = new Practice { OwnerTeacherUserId = teacher.Id, Title = title };
        db.Lessons.Add(lesson);
        db.Practices.Add(practice);
        db.Enrollments.Add(new Enrollment { Course = course, StudentUserId = student.Id });
        await db.SaveChangesAsync();
        var practiceService = scope.ServiceProvider.GetRequiredService<ITeacherPracticeQuestionService>();
        var practiceSaved = await practiceService.UpdateQuestionsAsync(teacher.Id, practice.Id,
            new UpdatePracticeQuestionsRequest
            {
                ExpectedUpdatedAtUtc = practice.UpdatedAtUtc,
                QuestionIds = questionIds.Take(2).ToList()
            });
        Assert.True(practiceSaved.Success);
        version = await db.Practices.Where(p => p.Id == practice.Id).Select(p => p.UpdatedAtUtc).SingleAsync();
        Assert.True((await practiceService.PublishAsync(teacher.Id, practice.Id, lesson.Id, null, version)).Success);

        // Student mở bài thật; GET/F5 không tạo thêm Attempt hoặc sao chép Stimulus.
        using var studentClient = await LoginAsync(student);
        string overviewPath = $"/Assessment/Take/{draft.Id}";
        token = await IdentityTestHelpers.GetTokenAsync(studentClient, overviewPath);
        using var start = await studentClient.PostAsync($"/Assessment/Start/{draft.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        string takingPath = start.Headers.Location!.ToString();
        for (int refresh = 0; refresh < 2; refresh++)
        {
            await AssertSharedMediaAsync(studentClient, takingPath, type, media, 5);
            await AssertSharedMediaAsync(studentClient, $"/Practice/Take/{practice.Id}", type, media, 2);
        }
        Assert.Equal(1, await db.Attempts.CountAsync(a => a.AssessmentId == draft.Id));
        Assert.False(await db.AttemptAnswers.AnyAsync(a => a.Attempt.AssessmentId == draft.Id));
        Assert.False(await db.AttemptSkillResults.AnyAsync(r => r.Attempt.AssessmentId == draft.Id));
        var storedQuestions = await db.Questions.AsNoTracking().Where(q => questionIds.Contains(q.Id)).ToListAsync();
        Assert.Equal(5, storedQuestions.Count);
        Assert.All(storedQuestions, q => Assert.Equal(stimulusId, q.StimulusId));
        var stimulus = await db.Stimuli.AsNoTracking().SingleAsync(s => s.Id == stimulusId);
        Assert.Equal(teacher.Id, stimulus.OwnerTeacherUserId);
        Assert.Equal(media, type == StimulusType.Passage ? stimulus.ContentText : stimulus.ResourceUrl);
        Assert.Equal(1, await db.Stimuli.CountAsync(s => s.Title == title));
        if (type == StimulusType.Audio)
        {
            using var audio = await studentClient.GetAsync(media);
            Assert.Equal(HttpStatusCode.OK, audio.StatusCode);
            Assert.Equal(StimulusAudioTestData.Wav(), await audio.Content.ReadAsByteArrayAsync());
            using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, media);
            rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 15);
            using var range = await studentClient.SendAsync(rangeRequest);
            Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
            Assert.Equal(16, (await range.Content.ReadAsByteArrayAsync()).Length);
        }
    }

    [Theory]
    [InlineData("None", false)]
    [InlineData("Pending", false)]
    [InlineData("Succeeded", true)]
    public async Task ManagedAudio_CourseScopeHonorsPaymentAndOwnership(string enrollment, bool allowed)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var otherTeacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var created = await scope.ServiceProvider.GetRequiredService<IStimulusCreationService>().CreateAsync(teacher.Id,
            new CreateStimulusRequest { StimulusType = StimulusType.Audio, AudioContent = StimulusAudioTestData.Wav() });
        Assert.True(created.Success);
        var stimulus = await db.Stimuli.SingleAsync(s => s.Id == created.Id);
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Private audio course", Status = CourseStatus.Published,
            IsPaid = true, Price = 1000m };
        var assessment = new Assessment { OwnerTeacherUserId = teacher.Id, Course = course, Title = "Private listening", Status = AssessmentStatus.Published,
            AssessmentType = AssessmentType.PracticeExam, DurationMinutes = 15, MaxAttempts = 2 };
        foreach (int i in Enumerable.Range(0, 5))
            assessment.Questions.Add(new AssessmentQuestion { Points = 1m, OrderIndex = i, Question = new Question
            { OwnerTeacherUserId = teacher.Id, Content = "Listen " + i, PrimarySkill = EnglishSkill.Listening, Stimulus = stimulus,
                Options = new List<QuestionOption> { new() { Content = "A", IsCorrect = true }, new() { Content = "B", OrderIndex = 1 } } } });
        db.Assessments.Add(assessment);
        if (enrollment != "None")
        {
            var payment = new Payment { Amount = 1000m, Provider = "TestOnly_Audio", ProviderTransactionId = Guid.NewGuid().ToString("N"),
                Status = enrollment == "Pending" ? PaymentStatus.Pending : PaymentStatus.Succeeded,
                Order = new Order { Course = course, StudentUserId = student.Id, Amount = 1000m, Currency = "VND", Provider = "TestOnly_Audio" } };
            db.Enrollments.Add(new Enrollment { Course = course, StudentUserId = student.Id, Payment = payment });
        }
        await db.SaveChangesAsync();
        using var studentClient = await LoginAsync(student);
        using var audio = await studentClient.GetAsync(stimulus.ResourceUrl);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.NotFound, audio.StatusCode);
        using var foreign = await LoginAsync(otherTeacher);
        using var foreignAudio = await foreign.GetAsync(stimulus.ResourceUrl);
        Assert.Equal(HttpStatusCode.NotFound, foreignAudio.StatusCode);
        using var owner = await LoginAsync(teacher);
        using var ownerAudio = await owner.GetAsync(stimulus.ResourceUrl);
        Assert.Equal(HttpStatusCode.OK, ownerAudio.StatusCode);
        Assert.False(await db.Attempts.AnyAsync(a => a.AssessmentId == assessment.Id));
    }

    [Fact]
    public async Task ExternalAudio_NewPracticeAndAssessmentCannotPublishOrPartiallyLockContent()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stimulus = new Stimulus { OwnerTeacherUserId = teacher.Id, StimulusType = StimulusType.Audio,
            ResourceUrl = "https://external.test/mutable.mp3" };
        var assessment = new Assessment { OwnerTeacherUserId = teacher.Id, Title = "External audio draft",
            AssessmentType = AssessmentType.PracticeExam, DurationMinutes = 20, MaxAttempts = 2 };
        foreach (int i in Enumerable.Range(0, 5))
            assessment.Questions.Add(new AssessmentQuestion { Points = 1m, OrderIndex = i, Question = new Question
            { OwnerTeacherUserId = teacher.Id, Content = "Listen " + i, PrimarySkill = EnglishSkill.Listening, Stimulus = stimulus,
                Options = new List<QuestionOption> { new() { Content = "A", IsCorrect = true }, new() { Content = "B", OrderIndex = 1 } } } });
        var lesson = new Lesson { Title = "Audio test lesson", Status = LessonStatus.Published,
            Module = new Module { Title = "Module", Course = new Course { Title = "Audio test course",
                OwnerTeacherUserId = teacher.Id, Status = CourseStatus.Published } } };
        var practice = new Practice { OwnerTeacherUserId = teacher.Id, Title = "External audio practice", Lesson = lesson };
        practice.Questions.Add(new PracticeQuestion { Question = assessment.Questions.First().Question });
        db.Assessments.Add(assessment); db.Practices.Add(practice);
        await db.SaveChangesAsync();
        Assert.False((await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentPublishService>()
            .PublishAsync(teacher.Id, assessment.Id, assessment.UpdatedAtUtc)).Success);
        Assert.False((await scope.ServiceProvider.GetRequiredService<ITeacherPracticeQuestionService>()
            .PublishAsync(teacher.Id, practice.Id, lesson.Id, null, practice.UpdatedAtUtc)).Success);
        db.ChangeTracker.Clear();
        Assert.Equal(AssessmentStatus.Draft, (await db.Assessments.SingleAsync(a => a.Id == assessment.Id)).Status);
        Assert.Equal(PracticeStatus.Draft, (await db.Practices.SingleAsync(p => p.Id == practice.Id)).Status);
        Assert.All(await db.Questions.Where(q => q.OwnerTeacherUserId == teacher.Id).ToListAsync(), q => Assert.Null(q.ContentLockedAtUtc));
        Assert.Null((await db.Stimuli.SingleAsync(s => s.Id == stimulus.Id)).ContentLockedAtUtc);
    }

    // Kiểm tra HTML Razor thật và số câu dùng chung ngữ liệu.
    // Không tải audio từ Internet; khả năng phát của URL thực tế được kiểm tra trên web.
    private static async Task AssertSharedMediaAsync(
        HttpClient client, string path, StimulusType type, string media, int expectedCount)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        var players = Regex.Matches(html, "<audio\\b[^>]*\\bsrc=\"([^\"]*)\"");
        if (type == StimulusType.Audio)
        {
            Assert.Equal(expectedCount, players.Count);
            foreach (Match player in players)
            {
                Assert.Equal(media, WebUtility.HtmlDecode(player.Groups[1].Value));
                Assert.Contains("controls", player.Value);
                Assert.Contains("preload=\"none\"", player.Value);
            }
        }
        else
        {
            Assert.Empty(players);
            Assert.DoesNotContain("<b>plain text</b>", html);
            // Razor có thể mã hóa cả ký tự xuống dòng thành HTML entity.
            // So sánh nội dung sau giải mã, nhưng vẫn kiểm tra HTML không được thực thi ở trên.
            Assert.Equal(expectedCount, Regex.Matches(WebUtility.HtmlDecode(html), Regex.Escape(media)).Count);
        }
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
