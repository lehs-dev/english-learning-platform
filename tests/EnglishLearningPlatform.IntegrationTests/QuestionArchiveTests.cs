using System.Net;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Dùng lại fixture có sẵn.
// Database test được tách riêng, không dùng database ứng dụng.
public sealed class QuestionArchiveTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public QuestionArchiveTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    // Owner lưu trữ câu hỏi đã có kết quả Practice.
    // Kiểm tra nội dung, đáp án và kết quả cũ không bị mất.
    [Fact]
    public async Task Archive_Owner_PreservesQuestionAndPracticeResult()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(
            _factory, AppRoles.Teacher);

        var student = await IdentityTestHelpers.CreateUserAsync(
            _factory, AppRoles.Student);

        var question = await CreateQuestionAsync(
            teacher.Id, QuestionStatus.Published);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Tạo dữ liệu lịch sử tối thiểu cho một Practice gắn Module.
        var course = new Course
        {
            OwnerTeacherUserId = teacher.Id,
            Title = "Archive test course",
            Status = CourseStatus.Published
        };

        var module = new Module
        {
            Course = course,
            Title = "Archive test module"
        };

        var practice = new Practice
        {
            OwnerTeacherUserId = teacher.Id,
            Module = module,
            Title = "Archive test practice",
            Status = PracticeStatus.Published
        };

        var practiceQuestion = new PracticeQuestion
        {
            Practice = practice,
            QuestionId = question.Id,
            OrderIndex = 0
        };

        var submission = new PracticeSubmission
        {
            Practice = practice,
            StudentUserId = student.Id,
            Score = 100m
        };

        var answer = new PracticeAnswer
        {
            PracticeSubmission = submission,
            PracticeQuestion = practiceQuestion,
            SelectedOptionId = question.Options.Single(
                option => option.IsCorrect).Id
        };

        db.AddRange(
            course, module, practice,
            practiceQuestion, submission, answer);

        await db.SaveChangesAsync();

        var practiceService =
            scope.ServiceProvider.GetRequiredService<IPracticeService>();

        var before = await practiceService.GetResultAsync(
            student.Id, practice.Id);

        Assert.Equal(LearningAccessResult.Allowed, before.Access);
        Assert.NotNull(before.Value);

        var authoringService =
            scope.ServiceProvider
                .GetRequiredService<IQuestionAuthoringService>();

        var result = await authoringService.ArchiveAsync(
            teacher.Id, question.Id);

        Assert.True(result.Succeeded);

        // AsNoTracking buộc đọc dữ liệu từ database,
        // không dựa vào entity đang được service theo dõi.
        var saved = await db.Questions
            .AsNoTracking()
            .Include(item => item.Options)
            .Include(item => item.Stimulus)
            .SingleAsync(item => item.Id == question.Id);

        Assert.Equal(QuestionStatus.Archived, saved.Status);
        Assert.NotNull(saved.UpdatedAtUtc);

        Assert.Equal(question.Content, saved.Content);
        Assert.Equal(question.Explanation, saved.Explanation);
        Assert.Equal(question.PrimarySkill, saved.PrimarySkill);
        Assert.Equal(question.StimulusId, saved.StimulusId);
        Assert.Equal(question.ContentLockedAtUtc, saved.ContentLockedAtUtc);

        // ID Option không đổi thì các đáp án cũ vẫn tham chiếu đúng.
        Assert.Equal(
            question.Options.OrderBy(item => item.OrderIndex)
                .Select(item => item.Id),
            saved.Options.OrderBy(item => item.OrderIndex)
                .Select(item => item.Id));

        Assert.Equal(
            new[] { "A", "B" },
            saved.Options.OrderBy(item => item.OrderIndex)
                .Select(item => item.Content));

        Assert.Equal(
            new[] { true, false },
            saved.Options.OrderBy(item => item.OrderIndex)
                .Select(item => item.IsCorrect));

        Assert.NotNull(saved.Stimulus);
        Assert.Equal("Original passage.", saved.Stimulus.ContentText);

        // Dùng lại validator hiện có:
        // Archived bị chặn cho nội dung mới, nhưng bài cũ vẫn hợp lệ.
        var validator =
            scope.ServiceProvider
                .GetRequiredService<IQuestionUsageValidator>();

        Assert.Contains(
            validator.ValidateForNewUse(teacher.Id, saved),
            error => error.Code == "archived");

        Assert.Empty(
            validator.ValidateForExistingUse(teacher.Id, saved));

        var after = await practiceService.GetResultAsync(
            student.Id, practice.Id);

        Assert.Equal(LearningAccessResult.Allowed, after.Access);
        Assert.NotNull(after.Value);
        Assert.Equal(100m, after.Value.Score);
        Assert.Equal(before.Value.SubmittedAtUtc, after.Value.SubmittedAtUtc);
        Assert.Equal(
            before.Value.Questions.ToArray(),
            after.Value.Questions.ToArray());

        // Gọi lại không được cập nhật thời gian một lần nữa.
        var repeated = await authoringService.ArchiveAsync(
            teacher.Id, question.Id);

        Assert.True(repeated.Succeeded);

        var updatedAt = await db.Questions
            .AsNoTracking()
            .Where(item => item.Id == question.Id)
            .Select(item => item.UpdatedAtUtc)
            .SingleAsync();

        Assert.Equal(saved.UpdatedAtUtc, updatedAt);
    }

    // Teacher khác không được lưu trữ câu hỏi của owner.
    [Fact]
    public async Task Archive_ForeignTeacher_ReturnsNotFoundWithoutChanges()
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(
            _factory, AppRoles.Teacher);

        var otherTeacher = await IdentityTestHelpers.CreateUserAsync(
            _factory, AppRoles.Teacher);

        var question = await CreateQuestionAsync(owner.Id);

        using var client = IdentityTestHelpers.CreateClient(_factory);
        using var login = await IdentityTestHelpers.LoginAsync(
            client, otherTeacher);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        // Lấy token của chính Teacher đang đăng nhập.
        var token = await IdentityTestHelpers.GetTokenAsync(
            client, "/TeacherQuestionBank/Create");

        using var response = await client.PostAsync(
            $"/TeacherQuestionBank/Archive/{question.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var saved = await db.Questions
            .AsNoTracking()
            .SingleAsync(item => item.Id == question.Id);

        Assert.Equal(QuestionStatus.Draft, saved.Status);
        Assert.Null(saved.UpdatedAtUtc);
    }

    // Kiểm tra GET không thay đổi dữ liệu, POST cần token,
    // và nút Archive biến mất sau khi lưu trữ thành công.
    [Fact]
    public async Task Archive_Http_RequiresPostAndToken_UpdatesDetailPage()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(
            _factory, AppRoles.Teacher);

        var question = await CreateQuestionAsync(teacher.Id);

        using var client = IdentityTestHelpers.CreateClient(_factory);
        using var login = await IdentityTestHelpers.LoginAsync(
            client, teacher);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        string archiveUrl =
            $"/TeacherQuestionBank/Archive/{question.Id}";

        string detailUrl =
            $"/TeacherQuestionBank/Details/{question.Id}";

        using var getResponse = await client.GetAsync(archiveUrl);

        Assert.Equal(
            HttpStatusCode.MethodNotAllowed,
            getResponse.StatusCode);

        // Cố gửi POST không có token phải bị từ chối.
        using var invalidPost = await client.PostAsync(
            archiveUrl,
            new FormUrlEncodedContent(
                new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, invalidPost.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(
            QuestionStatus.Draft,
            await db.Questions
                .Where(item => item.Id == question.Id)
                .Select(item => item.Status)
                .SingleAsync());

        // Form trên trang chi tiết phải có token hợp lệ.
        var token = await IdentityTestHelpers.GetTokenAsync(
            client, detailUrl);

        using var validPost = await client.PostAsync(
            archiveUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, validPost.StatusCode);
        Assert.NotNull(validPost.Headers.Location);
        Assert.Equal(detailUrl, validPost.Headers.Location.ToString());

        using var details = await client.GetAsync(detailUrl);

        Assert.Equal(HttpStatusCode.OK, details.StatusCode);

        string html = await details.Content.ReadAsStringAsync();

        // Giải mã HTML để kiểm tra chữ tiếng Việt được Razor mã hóa.
        string decodedHtml = WebUtility.HtmlDecode(html);

        Assert.Contains("Câu hỏi đã được lưu trữ.", decodedHtml);
        Assert.DoesNotContain($"action=\"{archiveUrl}\"", html);
    }

    // Mỗi test nhận câu hỏi riêng, không dùng chung dữ liệu có thể bị sửa.
    private async Task<Question> CreateQuestionAsync(
        Guid teacherId,
        QuestionStatus status = QuestionStatus.Draft)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stimulus = new Stimulus
        {
            OwnerTeacherUserId = teacherId,
            StimulusType = StimulusType.Passage,
            ContentText = "Original passage."
        };

        var question = new Question
        {
            OwnerTeacherUserId = teacherId,
            Content = "Original question.",
            Explanation = "A is correct.",
            PrimarySkill = EnglishSkill.Reading,
            Status = status,
            Stimulus = stimulus
        };

        question.Options.Add(new QuestionOption
        {
            Content = "A",
            IsCorrect = true,
            OrderIndex = 0
        });

        question.Options.Add(new QuestionOption
        {
            Content = "B",
            IsCorrect = false,
            OrderIndex = 1
        });

        // Mô phỏng dữ liệu Published đã được ghi dấu khóa.
        if (status == QuestionStatus.Published)
        {
            var lockedAt = DateTimeOffset.UtcNow.AddDays(-1);
            question.LockContent(lockedAt);
            stimulus.LockContent(lockedAt);
        }

        db.Questions.Add(question);
        await db.SaveChangesAsync();

        return question;
    }
}
