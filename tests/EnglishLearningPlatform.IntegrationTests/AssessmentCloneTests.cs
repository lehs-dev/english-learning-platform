using System.Net;
using System.Text.Json;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class AssessmentCloneTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData(AssessmentType.PlacementTest, AssessmentStatus.Draft)]
    [InlineData(AssessmentType.SkillAssessment, AssessmentStatus.Published)]
    [InlineData(AssessmentType.PracticeExam, AssessmentStatus.Archived)]
    public async Task Clone_CopiesConfigurationAndOrderedPointsToNewDraft_WithoutEditingSource(AssessmentType type, AssessmentStatus status)
    {
        var data = await SeedAsync(type, status);
        string before = await SnapshotAsync(data.Id);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var result = await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentCloneService>()
            .CloneAsync(data.Teacher.Id, data.Id, data.Version);
        Assert.True(result.Success);
        Assert.NotEqual(data.Id, result.Id);
        var copy = await db.Assessments.AsNoTracking().Include(a => a.Questions).SingleAsync(a => a.Id == result.Id);
        var source = await db.Assessments.AsNoTracking().Include(a => a.Questions).SingleAsync(a => a.Id == data.Id);
        Assert.Equal(AssessmentStatus.Draft, copy.Status);
        Assert.Equal(source.AssessmentType, copy.AssessmentType);
        Assert.Equal(source.TargetSkill, copy.TargetSkill);
        Assert.Equal(source.Description, copy.Description);
        Assert.Equal(source.DurationMinutes, copy.DurationMinutes);
        Assert.Equal(source.MaxAttempts, copy.MaxAttempts);
        Assert.Equal(source.PassingScore, copy.PassingScore);
        Assert.Equal(source.CourseId, copy.CourseId);
        Assert.Equal(source.OwnerTeacherUserId, copy.OwnerTeacherUserId);
        Assert.InRange(copy.Title.Length, 1, 200);
        Assert.Equal(source.Questions.OrderBy(q => q.OrderIndex).Select(q => (q.QuestionId, q.OrderIndex, q.Points)),
            copy.Questions.OrderBy(q => q.OrderIndex).Select(q => (q.QuestionId, q.OrderIndex, q.Points)));
        Assert.Empty(source.Questions.Select(q => q.Id).Intersect(copy.Questions.Select(q => q.Id)));
        Assert.Equal(before, await SnapshotAsync(data.Id));
        Assert.False(await db.Attempts.AnyAsync(a => a.AssessmentId == copy.Id));

        // Draft clone có thể chỉnh độc lập, cả khi nguồn Archived.
        var drafts = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentDraftService>();
        var editor = await drafts.GetConfigurationEditorAsync(data.Teacher.Id, copy.Id);
        Assert.NotNull(editor.Value);
        editor.Value.Input.DurationMinutes = 50;
        Assert.True((await drafts.SaveConfigurationAsync(data.Teacher.Id, copy.Id, editor.Value.Input)).Success);
        var questions = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentQuestionService>();
        var selection = await questions.GetEditorAsync(data.Teacher.Id, copy.Id);
        Assert.NotNull(selection.Value);
        var changed = await questions.SaveAsync(data.Teacher.Id, copy.Id, new UpdateAssessmentQuestionsRequest
        {
            ExpectedUpdatedAtUtc = selection.Value.UpdatedAtUtc,
            Items = copy.Questions.OrderByDescending(q => q.OrderIndex)
                .Select(q => new AssessmentQuestionInput { QuestionId = q.QuestionId, Points = 9m }).ToList()
        });
        Assert.True(changed.Success);
        Assert.Equal(before, await SnapshotAsync(data.Id));
    }

    [Fact]
    public async Task UsedFinalClone_DoesNotCopyHistoryOrUnlockContent_AndCannotReplaceOriginalFinal()
    {
        var data = await SeedAsync(AssessmentType.PracticeExam, AssessmentStatus.Published);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var finals = scope.ServiceProvider.GetRequiredService<ITeacherCourseFinalService>();
        var course = await db.Courses.AsNoTracking().SingleAsync(c => c.Id == data.CourseId);
        Assert.True((await finals.SaveAsync(data.Teacher.Id, course.Id, new CourseFinalRequest
        { FinalAssessmentId = data.Id, ExpectedFinalAssessmentId = null, ExpectedUpdatedAtUtc = course.UpdatedAtUtc })).Success);
        var attempts = scope.ServiceProvider.GetRequiredService<IAttemptService>();
        var start = await attempts.StartAsync(data.Student.Id, data.Id);
        Assert.Equal(AttemptStartStatus.Started, start.Status);
        var first = await db.AssessmentQuestions.AsNoTracking().Include(q => q.Question).ThenInclude(q => q.Options)
            .Where(q => q.AssessmentId == data.Id).OrderBy(q => q.OrderIndex).FirstAsync();
        Assert.Equal(AttemptSaveStatus.Saved, await attempts.SaveAnswerAsync(data.Student.Id,
            start.AttemptId!.Value, first.Id, first.Question.Options.Single(o => o.IsCorrect).Id));
        Assert.Equal(AttemptFinalizeStatus.Graded, (await attempts.FinalizeAsync(data.Student.Id, start.AttemptId.Value)).Status);
        string before = await SnapshotAsync(data.Id);
        int attemptCount = await db.Attempts.CountAsync();
        int answerCount = await db.AttemptAnswers.CountAsync();
        int resultCount = await db.AttemptSkillResults.CountAsync();
        var cloned = await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentCloneService>()
            .CloneAsync(data.Teacher.Id, data.Id, data.Version);
        Assert.True(cloned.Success);
        Assert.Equal(attemptCount, await db.Attempts.CountAsync());
        Assert.Equal(answerCount, await db.AttemptAnswers.CountAsync());
        Assert.Equal(resultCount, await db.AttemptSkillResults.CountAsync());
        Assert.False(await db.Attempts.AnyAsync(a => a.AssessmentId == cloned.Id));
        var copy = await db.Assessments.AsNoTracking().SingleAsync(a => a.Id == cloned.Id);
        Assert.True((await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentPublishService>()
            .PublishAsync(data.Teacher.Id, copy.Id, copy.UpdatedAtUtc)).Success);
        course = await db.Courses.AsNoTracking().SingleAsync(c => c.Id == data.CourseId);
        Assert.False((await finals.SaveAsync(data.Teacher.Id, course.Id, new CourseFinalRequest
        { FinalAssessmentId = cloned.Id, ExpectedFinalAssessmentId = data.Id, ExpectedUpdatedAtUtc = course.UpdatedAtUtc })).Success);

        var q = first.Question;
        var edit = await scope.ServiceProvider.GetRequiredService<IQuestionAuthoringService>().UpdateAsync(data.Teacher.Id, q.Id,
            new CreateQuestionRequest { Content = "Changed via clone", PrimarySkill = q.PrimarySkill,
                Explanation = q.Explanation, StimulusId = q.StimulusId,
                Options = q.Options.OrderBy(o => o.OrderIndex).Select(o => new QuestionOptionRequest(o.Content, o.IsCorrect)).ToArray() });
        Assert.False(edit.Succeeded);
        Assert.Contains(edit.Errors, e => e.Code == "locked");
        Assert.Equal(before, await SnapshotAsync(data.Id));
        Assert.Equal(data.Id, await db.Courses.Where(c => c.Id == course.Id).Select(c => c.FinalAssessmentId).SingleAsync());
    }

    [Fact]
    public async Task Clone_DeniesStaleVersionForeignOwnerStudentAndMissingCsrf_HttpRedirectsToNewDraft()
    {
        var data = await SeedAsync(AssessmentType.PracticeExam, AssessmentStatus.Published);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentCloneService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        string before = await SnapshotAsync(data.Id);
        int assessmentCount = await db.Assessments.CountAsync();
        Assert.Equal(LearningAccessResult.NotFound, (await service.CloneAsync(other.Id, data.Id, data.Version)).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await service.CloneAsync(data.Student.Id, data.Id, data.Version)).Access);
        Assert.False((await service.CloneAsync(data.Teacher.Id, data.Id, data.Version!.Value.AddSeconds(-1))).Success);
        // Request trực tiếp có CSRF hợp lệ vẫn không được Clone đề của Teacher khác.
        using var foreignClient = IdentityTestHelpers.CreateClient(factory);
        using var foreignLogin = await IdentityTestHelpers.LoginAsync(foreignClient, other);
        string foreignToken = await IdentityTestHelpers.GetTokenAsync(foreignClient, "/TeacherAssessments/Edit");
        using var foreignClone = await foreignClient.PostAsync("/TeacherAssessmentClone/Create/" + data.Id,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = foreignToken,
                ["expectedUpdatedAtUtc"] = data.Version.Value.ToString("O"),
                ["OwnerTeacherUserId"] = other.Id.ToString()
            }));
        Assert.Equal(HttpStatusCode.NotFound, foreignClone.StatusCode);
        Assert.Equal(assessmentCount, await db.Assessments.CountAsync());
        Assert.Equal(before, await SnapshotAsync(data.Id));
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, data.Teacher);
        string path = "/TeacherAssessmentClone/Create/" + data.Id;
        using var get = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        var fields = new Dictionary<string, string> { ["expectedUpdatedAtUtc"] = data.Version.Value.ToString("O"),
            ["OwnerTeacherUserId"] = other.Id.ToString(), ["Status"] = "Published" };
        using var noToken = await client.PostAsync(path, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        fields["__RequestVerificationToken"] = await IdentityTestHelpers.GetTokenAsync(client, "/TeacherAssessments/Edit");
        using var cloned = await client.PostAsync(path, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, cloned.StatusCode);
        Assert.StartsWith("/TeacherAssessments/Edit/", cloned.Headers.Location!.ToString());
        Guid id = Guid.Parse(cloned.Headers.Location.ToString().Split('/').Last());
        var copy = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Assessments.AsNoTracking().SingleAsync(a => a.Id == id);
        Assert.Equal(data.Teacher.Id, copy.OwnerTeacherUserId);
        Assert.Equal(AssessmentStatus.Draft, copy.Status);
    }

    private async Task<string> SnapshotAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var a = await db.Assessments.AsNoTracking().Include(a => a.Questions).ThenInclude(q => q.Question).ThenInclude(q => q.Options)
            .Include(a => a.Attempts).ThenInclude(a => a.Answers).Include(a => a.Attempts).ThenInclude(a => a.SkillResults)
            .SingleAsync(a => a.Id == id);
        return JsonSerializer.Serialize(new { a.Id, a.Title, a.Status, a.CourseId, a.DurationMinutes, a.MaxAttempts, a.PassingScore, a.UpdatedAtUtc,
            Questions = a.Questions.OrderBy(q => q.OrderIndex).Select(q => new { q.Id, q.QuestionId, q.Points, q.OrderIndex,
                q.Question.Content, q.Question.Explanation, q.Question.ContentLockedAtUtc,
                Options = q.Question.Options.OrderBy(o => o.OrderIndex).Select(o => new { o.Id, o.Content, o.IsCorrect, o.OrderIndex }) }),
            Attempts = a.Attempts.OrderBy(a => a.Id).Select(a => new { a.Id, a.Status, a.OverallScore, a.Passed, a.DeadlineUtc,
                Answers = a.Answers.OrderBy(v => v.Id).Select(v => new { v.Id, v.SelectedOptionId }),
                Results = a.SkillResults.OrderBy(v => v.Skill).Select(v => new { v.Skill, v.Score }) }) });
    }

    private async Task<Data> SeedAsync(AssessmentType type, AssessmentStatus status)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Clone course", Status = CourseStatus.Published };
        var a = new Assessment { OwnerTeacherUserId = teacher.Id, Course = course, Title = new string('a', 200),
            Description = "Clone config", AssessmentType = type, Status = status, DurationMinutes = 20, MaxAttempts = 3,
            TargetSkill = type == AssessmentType.SkillAssessment ? EnglishSkill.Grammar : null,
            PassingScore = type == AssessmentType.PlacementTest ? null : 70m, UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5) };
        foreach (int i in Enumerable.Range(0, 5))
        {
            var q = new Question { OwnerTeacherUserId = teacher.Id, Content = "Clone question " + i,
                PrimarySkill = EnglishSkill.Grammar, Explanation = "Original explanation",
                Options = new List<QuestionOption> { new() { Content = "Correct", IsCorrect = true }, new() { Content = "Wrong", OrderIndex = 1 } } };
            if (status != AssessmentStatus.Draft) q.LockContent(DateTimeOffset.UtcNow.AddDays(-1));
            a.Questions.Add(new AssessmentQuestion { Question = q, OrderIndex = i, Points = i + 1m });
        }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Assessments.Add(a); db.Enrollments.Add(new Enrollment { Course = course, StudentUserId = student.Id });
        await db.SaveChangesAsync();
        return new Data(teacher, student, a.Id, course.Id, a.UpdatedAtUtc);
    }
    private sealed record Data(ApplicationUser Teacher, ApplicationUser Student, Guid Id, Guid CourseId, DateTimeOffset? Version);
}
