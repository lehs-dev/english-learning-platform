using System.Net;
using System.Text.Json;
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

// Dùng SQL Server test riêng của factory, không ghi vào database demo.
public sealed class TeacherAssessmentPreviewTests : IClassFixture<IntegrationTestFactory>
{
    private const string Passage = "TASK3_PASSAGE <b>plain text</b>";
    private const string AudioUrl = "https://media.example.test/task3.mp3";
    private const string PrivateExplanation = "TASK3_PRIVATE_EXPLANATION";
    private readonly IntegrationTestFactory _factory;

    public TeacherAssessmentPreviewTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(AssessmentType.PlacementTest, false)]
    [InlineData(AssessmentType.SkillAssessment, true)]
    [InlineData(AssessmentType.PracticeExam, true)]
    public async Task Preview_ThreeTypes_ShowSavedConfigurationOrderingAndMedia(
        AssessmentType type, bool courseLinked)
    {
        var data = await SeedAsync(type: type, courseLinked: courseLinked);
        var result = await ReadAsync(data);
        Assert.Equal(LearningAccessResult.Allowed, result.Access);
        var preview = Assert.IsType<TeacherAssessmentPreview>(result.Value);
        Assert.Equal(data.Assessment.Title, preview.Title);
        Assert.Equal(data.Assessment.Description, preview.Description);
        Assert.Equal(type, preview.AssessmentType);
        Assert.Equal(AssessmentStatus.Draft, preview.Status);
        Assert.Equal(data.Assessment.TargetSkill, preview.TargetSkill);
        Assert.Equal(data.Assessment.CourseId, preview.CourseId);
        Assert.Equal(data.Assessment.Course?.Title, preview.CourseTitle);
        Assert.Equal(37, preview.DurationMinutes);
        Assert.Equal(2, preview.MaxAttempts);
        Assert.Equal(data.Assessment.PassingScore, preview.PassingScore);
        Assert.Empty(preview.Warnings);

        var expected = data.Assessment.Questions.OrderBy(q => q.OrderIndex).ToList();
        Assert.Equal(expected.Select(q => q.Id), preview.Questions.Select(q => q.AssessmentQuestionId));
        Assert.Equal(expected.Select(q => q.QuestionId), preview.Questions.Select(q => q.QuestionId));
        Assert.Equal(expected.Select(q => q.Points), preview.Questions.Select(q => q.Points));
        Assert.All(preview.Questions, q =>
        {
            Assert.Null(q.SelectedOptionId);
            Assert.Equal(new[] { 0, 1 }, q.Options.Select(o => o.OrderIndex));
            Assert.Equal(new[] { "CHOICE_FIRST", "CHOICE_SECOND" }, q.Options.Select(o => o.Content));
        });
        Assert.Equal(Passage, preview.Questions[0].StimulusContent);
        Assert.Equal(AudioUrl, preview.Questions[1].AudioUrl);

        using var client = await LoginAsync(data.Teacher);
        using var response = await client.GetAsync(Path(data));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains(data.Assessment.Title, WebUtility.HtmlDecode(html));
        Assert.Contains(type.ToString(), html);
        Assert.Contains(courseLinked ? "TASK3_COURSE" : "Bài độc lập", WebUtility.HtmlDecode(html));
        Assert.Contains(Passage, WebUtility.HtmlDecode(html));
        Assert.Contains($"src=\"{AudioUrl}\"", html);
        Assert.Contains("preload=\"none\"", html);
        Assert.Contains("name=\"PreviewAnswers[0]\"", html);
        Assert.Contains("<dl class=\"assessment-details\">", html);
        Assert.Contains("/css/assessment.css", html);
        Assert.True(html.IndexOf("TASK3_Q0", StringComparison.Ordinal)
            < html.IndexOf("TASK3_Q1", StringComparison.Ordinal));
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>task3_probe()</script>", html);
        Assert.DoesNotContain(PrivateExplanation, html);
        // Preview không được nạp script tự lưu/nộp bài của Student.
        Assert.DoesNotContain("/js/assessment.js", html);
        Assert.DoesNotContain("data-assessment-form", html);
        Assert.DoesNotContain("/Assessment/Start", html);
        Assert.DoesNotContain("/Assessment/Save", html);
        Assert.DoesNotContain("/Assessment/Submit", html);
    }

    [Theory]
    [InlineData(AssessmentStatus.Draft)]
    [InlineData(AssessmentStatus.Published)]
    public async Task RepeatedPreview_DoesNotCreateOrModifyAttemptsAnswersResultsOrContent(
        AssessmentStatus status)
    {
        var data = await SeedAsync(status: status, history: status == AssessmentStatus.Published);
        using var client = await LoginAsync(data.Teacher);
        string before = await SnapshotAsync(data);

        for (int index = 0; index < 3; index++)
        {
            Assert.Equal(LearningAccessResult.Allowed, (await ReadAsync(data)).Access);
            using var response = await client.GetAsync(Path(data));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // Kiểm tra ID/nội dung/điểm/thời gian, không chỉ đếm số dòng.
        // Published đã hết MaxAttempts và có Attempt quá hạn vẫn được Preview.
        Assert.Equal(before, await SnapshotAsync(data));
        using var list = await client.GetAsync("/TeacherAssessments");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        string listHtml = await list.Content.ReadAsStringAsync();
        Assert.Contains(Path(data), listHtml);
        Assert.Contains("class=\"button small secondary\"", listHtml);
        if (status == AssessmentStatus.Draft)
        {
            Assert.Contains("class=\"button small\"", listHtml);
            foreach (string route in new[] { "TeacherAssessments/Edit", "TeacherAssessmentQuestions/Index" })
            {
                using var editor = await client.GetAsync($"/{route}/{data.Assessment.Id}");
                Assert.Equal(HttpStatusCode.OK, editor.StatusCode);
                Assert.Contains(Path(data), await editor.Content.ReadAsStringAsync());
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task IncompleteDraft_ShowsWarningsWithoutBlockingOrPublishing(int questionCount)
    {
        var data = await SeedAsync(type: AssessmentType.SkillAssessment, questionCount: questionCount);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Assessments.Where(a => a.Id == data.Assessment.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.Title, "   "));
            if (questionCount > 0)
            {
                var first = data.Assessment.Questions.Single(q => q.OrderIndex == 0).Question;
                var second = data.Assessment.Questions.Single(q => q.OrderIndex == 1).Question;
                await db.Stimuli.Where(s => s.Id == first.StimulusId)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.ContentText, "   "));
                await db.QuestionOptions.Where(o => o.QuestionId == first.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(o => o.IsCorrect, false));
                await db.Questions.Where(q => q.Id == second.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(q => q.PrimarySkill, EnglishSkill.Listening));
            }
        }
        string before = await SnapshotAsync(data);
        var result = await ReadAsync(data);
        Assert.Equal(LearningAccessResult.Allowed, result.Access);
        var preview = Assert.IsType<TeacherAssessmentPreview>(result.Value);
        Assert.Equal(questionCount, preview.Questions.Count);
        Assert.Contains(preview.Warnings, w => w.Contains($"{questionCount}/5", StringComparison.Ordinal));
        Assert.Contains(preview.Warnings, w => w.StartsWith("Cấu hình:", StringComparison.Ordinal));
        if (questionCount > 0)
        {
            Assert.Contains(preview.Warnings, w => w.Contains("passage", StringComparison.Ordinal));
            Assert.Contains(preview.Warnings, w => w.Contains("đúng một đáp án", StringComparison.Ordinal));
            Assert.Contains(preview.Warnings, w => w.Contains("TargetSkill", StringComparison.Ordinal));
        }
        using var client = await LoginAsync(data.Teacher);
        using var response = await client.GetAsync(Path(data));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Các phần cần kiểm tra", html);
        Assert.Contains($"{questionCount}/5", html);
        if (questionCount == 0) Assert.Contains("Đề chưa có câu hỏi.", html);
        Assert.Equal(before, await SnapshotAsync(data));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/private.wav")]
    public async Task UnsafeAudio_IsWarnedAndNeverRenderedAsPlayerSource(string url)
    {
        var data = await SeedAsync();
        Guid stimulusId = data.Assessment.Questions.Single(q => q.OrderIndex == 1).Question.StimulusId!.Value;
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Stimuli
                .Where(s => s.Id == stimulusId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.ResourceUrl, url));
        }
        var preview = Assert.IsType<TeacherAssessmentPreview>((await ReadAsync(data)).Value);
        Assert.Null(preview.Questions[1].AudioUrl);
        Assert.Contains(preview.Warnings, w => w.Contains("URL audio", StringComparison.Ordinal));
        using var client = await LoginAsync(data.Teacher);
        using var response = await client.GetAsync(Path(data));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(url, html);
        Assert.DoesNotContain("<audio", html);
    }

    [Theory]
    [InlineData("OtherTeacher", LearningAccessResult.NotFound)]
    [InlineData("Student", LearningAccessResult.Forbidden)]
    [InlineData("LockedTeacher", LearningAccessResult.Forbidden)]
    public async Task Service_RejectsForeignOrUnauthorizedActor(string actorKind, LearningAccessResult expected)
    {
        var data = await SeedAsync();
        Guid actorId = data.Teacher.Id;
        if (actorKind != "LockedTeacher")
        {
            var actor = await IdentityTestHelpers.CreateUserAsync(_factory,
                actorKind == "Student" ? AppRoles.Student : AppRoles.Teacher);
            actorId = actor.Id;
        }
        else
        {
            using var scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
                .Where(u => u.Id == actorId)
                .ExecuteUpdateAsync(set => set.SetProperty(u => u.AccountStatus, AccountStatus.Locked));
        }
        var result = await ReadAsync(data, actorId);
        Assert.Equal(expected, result.Access);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Http_RequiresOwnerTeacherAndGetOnly()
    {
        var data = await SeedAsync();
        using var guest = IdentityTestHelpers.CreateClient(_factory);
        using var anonymous = await guest.GetAsync(Path(data));
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("/Account/Login", anonymous.Headers.Location!.ToString());

        var student = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student);
        using var studentClient = await LoginAsync(student);
        using var denied = await studentClient.GetAsync(Path(data));
        AssertForbidden(denied);
        var otherTeacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var foreignClient = await LoginAsync(otherTeacher);
        using var foreign = await foreignClient.GetAsync(Path(data));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.DoesNotContain("TASK3_Q0", await foreign.Content.ReadAsStringAsync());

        using var ownerClient = await LoginAsync(data.Teacher);
        using var missing = await ownerClient.GetAsync($"/TeacherAssessmentPreview/Index/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        string before = await SnapshotAsync(data);
        using var post = await ownerClient.PostAsync(Path(data), new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        Assert.Equal(before, await SnapshotAsync(data));
    }

    [Theory]
    [InlineData("Question")]
    [InlineData("Stimulus")]
    [InlineData("Course")]
    public async Task ForeignNestedContent_IsNotExposed(string resource)
    {
        var data = await SeedAsync();
        var foreign = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var first = data.Assessment.Questions.Single(q => q.OrderIndex == 0).Question;
            if (resource == "Question")
                await db.Questions.Where(q => q.Id == first.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(q => q.OwnerTeacherUserId, foreign.Id));
            if (resource == "Stimulus")
                await db.Stimuli.Where(s => s.Id == first.StimulusId)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.OwnerTeacherUserId, foreign.Id));
            if (resource == "Course")
                await db.Courses.Where(c => c.Id == data.Assessment.CourseId)
                    .ExecuteUpdateAsync(set => set.SetProperty(c => c.OwnerTeacherUserId, foreign.Id));
        }
        var result = await ReadAsync(data);
        Assert.Equal(LearningAccessResult.NotFound, result.Access);
        Assert.Null(result.Value);
        using var client = await LoginAsync(data.Teacher);
        using var response = await client.GetAsync(Path(data));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("TASK3_Q0", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(AssessmentStatus.Unpublished)]
    [InlineData(AssessmentStatus.Archived)]
    public async Task UnsupportedStatus_IsDenied(AssessmentStatus status)
    {
        var data = await SeedAsync(status: status);
        var result = await ReadAsync(data);
        Assert.Equal(LearningAccessResult.Forbidden, result.Access);
        Assert.Null(result.Value);
        using var client = await LoginAsync(data.Teacher);
        using var response = await client.GetAsync(Path(data));
        AssertForbidden(response);
    }

    private async Task<LearningOutcome<TeacherAssessmentPreview>> ReadAsync(SeedData data, Guid? actorId = null)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentPreviewService>()
            .GetAsync(actorId ?? data.Teacher.Id, data.Assessment.Id);
    }

    private async Task<HttpClient> LoginAsync(ApplicationUser user)
    {
        var client = IdentityTestHelpers.CreateClient(_factory);
        using var response = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static string Path(SeedData data) => $"/TeacherAssessmentPreview/Index/{data.Assessment.Id}";

    private static void AssertForbidden(HttpResponseMessage response)
    {
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.Redirect });
        if (response.StatusCode == HttpStatusCode.Redirect)
            Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
    }

    // Snapshot đọc bằng context mới để phát hiện thay đổi DB, không dựa vào cache entity.
    private async Task<string> SnapshotAsync(SeedData data)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assessment = await db.Assessments.AsNoTracking().AsSplitQuery()
            .Include(a => a.Questions).ThenInclude(q => q.Question).ThenInclude(q => q.Options)
            .Include(a => a.Questions).ThenInclude(q => q.Question).ThenInclude(q => q.Stimulus)
            .Include(a => a.Attempts).ThenInclude(a => a.Answers)
            .Include(a => a.Attempts).ThenInclude(a => a.SkillResults)
            .SingleAsync(a => a.Id == data.Assessment.Id);
        return JsonSerializer.Serialize(new
        {
            assessment.Id, assessment.Title, assessment.Description, assessment.Status,
            assessment.AssessmentType, assessment.TargetSkill, assessment.CourseId,
            assessment.DurationMinutes, assessment.MaxAttempts, assessment.PassingScore,
            assessment.CreatedAtUtc, assessment.UpdatedAtUtc,
            Questions = assessment.Questions.OrderBy(q => q.Id).Select(q => new
            {
                q.Id, q.QuestionId, q.OrderIndex, q.Points, q.CreatedAtUtc, q.UpdatedAtUtc,
                Question = new
                {
                    q.Question.Content, q.Question.PrimarySkill, q.Question.Status,
                    q.Question.Explanation, q.Question.StimulusId, q.Question.ContentLockedAtUtc,
                    q.Question.UpdatedAtUtc,
                    Options = q.Question.Options.OrderBy(o => o.Id).Select(o => new
                    {
                        o.Id, o.Content, o.OrderIndex, o.IsCorrect, o.UpdatedAtUtc
                    }),
                    Stimulus = q.Question.Stimulus == null ? null : new
                    {
                        q.Question.Stimulus.Id, q.Question.Stimulus.StimulusType,
                        q.Question.Stimulus.ContentText, q.Question.Stimulus.ResourceUrl,
                        q.Question.Stimulus.ContentLockedAtUtc, q.Question.Stimulus.UpdatedAtUtc
                    }
                }
            }),
            Attempts = assessment.Attempts.OrderBy(a => a.Id).Select(a => new
            {
                a.Id, a.StudentUserId, a.Status, a.StartedAtUtc, a.DeadlineUtc,
                a.FinalizedAtUtc, a.FinalizationReason, a.OverallScore, a.Passed,
                a.CreatedAtUtc, a.UpdatedAtUtc,
                Answers = a.Answers.OrderBy(answer => answer.Id).Select(answer => new
                {
                    answer.Id, answer.AssessmentQuestionId, answer.SelectedOptionId, answer.SavedAtUtc
                }),
                Results = a.SkillResults.OrderBy(r => r.Id).Select(r => new
                {
                    r.Id, r.Skill, r.Score, r.QuestionCount
                })
            })
        });
    }

    private async Task<SeedData> SeedAsync(AssessmentType type = AssessmentType.PracticeExam,
        AssessmentStatus status = AssessmentStatus.Draft, int questionCount = 5,
        bool courseLinked = true, bool history = false)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assessment = new AssessmentEntity
        {
            OwnerTeacherUserId = teacher.Id, Title = "TASK3_PREVIEW",
            Description = "TASK3_INSTRUCTIONS", AssessmentType = type, Status = status,
            TargetSkill = type == AssessmentType.SkillAssessment ? EnglishSkill.Reading : null,
            DurationMinutes = 37, MaxAttempts = 2,
            PassingScore = type == AssessmentType.PlacementTest ? null : 72.5m,
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddDays(-3),
            Course = courseLinked ? new Course { OwnerTeacherUserId = teacher.Id, Title = "TASK3_COURSE" } : null
        };
        // Cố tình thêm ngược thứ tự để test không vô tình dựa vào thứ tự ICollection.
        for (int i = questionCount - 1; i >= 0; i--)
        {
            var question = new Question
            {
                OwnerTeacherUserId = teacher.Id,
                Content = $"TASK3_Q{i}" + (i == 0 ? " <script>task3_probe()</script>" : ""),
                PrimarySkill = i == 1 && type != AssessmentType.SkillAssessment
                    ? EnglishSkill.Listening : EnglishSkill.Reading,
                Explanation = PrivateExplanation,
                Options = new List<QuestionOption>
                {
                    new QuestionOption { Content = "CHOICE_SECOND", OrderIndex = 1, IsCorrect = true },
                    new QuestionOption { Content = "CHOICE_FIRST", OrderIndex = 0 }
                }
            };
            if (i < 2)
                question.Stimulus = new Stimulus
                {
                    OwnerTeacherUserId = teacher.Id,
                    StimulusType = i == 0 ? StimulusType.Passage : StimulusType.Audio,
                    ContentText = i == 0 ? Passage : null,
                    ResourceUrl = i == 1 ? AudioUrl : null
                };
            if (status == AssessmentStatus.Published)
            {
                question.LockContent(DateTimeOffset.UtcNow.AddDays(-2));
                question.Stimulus?.LockContent(DateTimeOffset.UtcNow.AddDays(-2));
            }
            assessment.Questions.Add(new AssessmentQuestion
            {
                Question = question, OrderIndex = i, Points = i + 1.25m
            });
        }
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();
        if (history)
        {
            var student = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student);
            var first = assessment.Questions.Single(q => q.OrderIndex == 0);
            var started = DateTimeOffset.UtcNow.AddDays(-1);
            foreach (bool graded in new[] { true, false })
            {
                var attempt = new Attempt
                {
                    AssessmentId = assessment.Id, StudentUserId = student.Id,
                    Status = graded ? AttemptStatus.Graded : AttemptStatus.InProgress,
                    StartedAtUtc = started, DeadlineUtc = started.AddMinutes(37),
                    FinalizedAtUtc = graded ? started.AddMinutes(20) : null,
                    FinalizationReason = graded ? AttemptFinalizationReason.ManualSubmit : null,
                    OverallScore = graded ? 81.25m : null, Passed = graded ? true : null,
                    Answers = new List<AttemptAnswer>
                    {
                        new AttemptAnswer
                        {
                            AssessmentQuestionId = first.Id,
                            SelectedOptionId = first.Question.Options.First().Id
                        }
                    }
                };
                if (graded) attempt.SkillResults.Add(new AttemptSkillResult
                {
                    Skill = EnglishSkill.Reading, Score = 81.25m, QuestionCount = 4
                });
                db.Attempts.Add(attempt);
            }
            await db.SaveChangesAsync();
        }
        return new SeedData(teacher, assessment);
    }

    private sealed record SeedData(ApplicationUser Teacher, AssessmentEntity Assessment);
}
