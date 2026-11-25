using System.Net;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class PracticeMistakeReviewTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task Review_GradesOnlyOwnLatestMistakes_AndPreservesFullResult()
    {
        var data = await SeedAsync();
        await SubmitFullAsync(data);
        using var studentClient = IdentityTestHelpers.CreateClient(factory);
        using var studentLogin = await IdentityTestHelpers.LoginAsync(studentClient, data.Student);
        Assert.Equal(HttpStatusCode.Redirect, studentLogin.StatusCode);

        var reviewPath = $"/Practice/Review/{data.PracticeId}";
        var resultPath = $"/Practice/Result/{data.PracticeId}";
        Assert.Contains($"href=\"{reviewPath}\"", await studentClient.GetStringAsync(resultPath));
        var sheet = await studentClient.GetStringAsync(reviewPath);
        Assert.Contains(data.WrongItemId.ToString(), sheet);
        Assert.Contains(data.BlankItemId.ToString(), sheet);
        Assert.DoesNotContain(data.CorrectItemId.ToString(), sheet);
        Assert.DoesNotContain("UNPUBLISHED_REVIEW_EXPLANATION", sheet);
        Assert.DoesNotContain("private fill answer", sheet);
        var stamp = Regex.Match(sheet, "name=\"ReviewStamp\"[^>]*value=\"([0-9A-F]{64})\"");
        Assert.True(stamp.Success);
        var csrf = await IdentityTestHelpers.GetTokenAsync(studentClient, reviewPath);

        using var missingToken = await CheckAsync(studentClient, null, data.WrongItemId, null, null);
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        using var outsideSubset = await CheckAsync(studentClient, csrf, data.CorrectItemId, data.CorrectChoiceId, "private fill answer");
        Assert.Equal(HttpStatusCode.BadRequest, outsideSubset.StatusCode);

        using var blank = await CheckAsync(studentClient, csrf, data.WrongItemId, null, null);
        Assert.Equal(HttpStatusCode.OK, blank.StatusCode);
        var blankFeedback = WebUtility.HtmlDecode(await blank.Content.ReadAsStringAsync());
        Assert.Contains("Bạn trả lời đúng 0 / 2 câu hỏi.", blankFeedback);
        Assert.Contains("Bỏ trống", blankFeedback);

        using var checkedReview = await CheckAsync(studentClient, csrf, data.WrongItemId, data.WrongItemCorrectChoiceId, "private fill answer");
        Assert.Equal(HttpStatusCode.OK, checkedReview.StatusCode);
        var feedback = WebUtility.HtmlDecode(await checkedReview.Content.ReadAsStringAsync());
        Assert.Contains("Kết quả lượt ôn", feedback);
        Assert.Contains("Bạn trả lời đúng 2 / 2 câu hỏi.", feedback);
        Assert.Contains("UNPUBLISHED_REVIEW_EXPLANATION", feedback);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var result = await db.PracticeSubmissions.SingleAsync(s => s.PracticeId == data.PracticeId);
            Assert.Equal(33.33m, result.Score);
            Assert.False(await db.PracticeSessions.AnyAsync(session => session.PracticeId == data.PracticeId));
            Assert.False(await db.PracticeSubmissionHistories.AnyAsync(history => history.PracticeId == data.PracticeId));
        }
        var fullResult = WebUtility.HtmlDecode(await studentClient.GetStringAsync(resultPath));
        Assert.Contains("Bạn trả lời đúng 1 / 3 câu hỏi.", fullResult);

        using var otherClient = IdentityTestHelpers.CreateClient(factory);
        using var otherLogin = await IdentityTestHelpers.LoginAsync(otherClient, data.OtherStudent);
        Assert.Equal(HttpStatusCode.Redirect, otherLogin.StatusCode);
        using var unattempted = await otherClient.GetAsync(reviewPath);
        Assert.Equal(HttpStatusCode.NotFound, unattempted.StatusCode);

        // The new full result can have the same missed subset, but its saved answer rows are different.
        await SubmitFullAsync(data);
        using var stale = await CheckAsync(studentClient, csrf, data.WrongItemId, data.WrongItemCorrectChoiceId, "private fill answer");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains($"href=\"{resultPath}\"", await stale.Content.ReadAsStringAsync());

        async Task<HttpResponseMessage> CheckAsync(HttpClient client, string? token, Guid firstItem, Guid? choice, string? text)
        {
            var fields = new Dictionary<string, string>
            {
                ["ReviewStamp"] = stamp.Groups[1].Value,
                ["Answers[0].PracticeQuestionId"] = firstItem.ToString(),
                ["Answers[1].PracticeQuestionId"] = data.BlankItemId.ToString()
            };
            if (token is not null) fields["__RequestVerificationToken"] = token;
            if (choice.HasValue) fields["Answers[0].SelectedOptionId"] = choice.Value.ToString();
            if (text is not null) fields["Answers[1].TextAnswer"] = text;
            using var form = new FormUrlEncodedContent(fields);
            return await client.PostAsync($"/Practice/CheckReview/{data.PracticeId}", form);
        }
    }

    [Fact]
    public async Task Review_RejectsPreviousVersionWhileKeepingItsFullResultAvailable()
    {
        var data = await SeedAsync();
        await SubmitFullAsync(data);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var opened = await service.GetMistakeSheetAsync(data.Student.Id, data.PracticeId);
        var originalSheet = Assert.IsType<PracticeSheet>(opened.Value);
        var items = await db.PracticeQuestions.AsNoTracking().Where(question => question.PracticeId == data.PracticeId).ToListAsync();
        db.PracticeQuestions.AddRange(items.Select(item => new PracticeQuestion
        {
            PracticeId = item.PracticeId, QuestionId = item.QuestionId, OrderIndex = item.OrderIndex, Version = 2
        }));
        await db.SaveChangesAsync();
        await db.Practices.Where(practice => practice.Id == data.PracticeId)
            .ExecuteUpdateAsync(update => update.SetProperty(practice => practice.Version, 2));

        var review = await service.CheckMistakesAsync(data.Student.Id, data.PracticeId, originalSheet.ReviewStamp!,
            [new(data.WrongItemId, data.WrongItemCorrectChoiceId), new(data.BlankItemId, null, "private fill answer")]);
        Assert.Equal(PracticeReviewStatus.Stale, review.Status);
        Assert.Null(review.Feedback);
        var full = await service.GetResultAsync(data.Student.Id, data.PracticeId);
        Assert.Equal(1, full.Value!.Version);
        Assert.Equal(33.33m, full.Value.Score);
    }

    private async Task SubmitFullAsync(PracticeData data)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        var submission = await service.SubmitAsync(data.Student.Id, data.PracticeId,
            [new(data.CorrectItemId, data.CorrectChoiceId), new(data.WrongItemId, data.WrongChoiceId), new(data.BlankItemId, null)]);
        Assert.Equal(PracticeSubmitStatus.Saved, submission.Status);
    }

    private async Task<PracticeData> SeedAsync()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Review Course", Status = CourseStatus.Published };
        var module = new Module { Course = course, Title = "Review Module", Visibility = ModuleVisibility.Visible };
        var correctChoice = new QuestionOption { Content = "Often", IsCorrect = true };
        var wrongItemCorrectChoice = new QuestionOption { Content = "Yesterday", IsCorrect = true };
        var wrongChoice = new QuestionOption { Content = "Tomorrow", OrderIndex = 1 };
        var correct = new PracticeQuestion
        {
            Question = NewQuestion("Read the prompt.", QuestionType.MultipleChoice), OrderIndex = 0
        };
        correct.Question.Options = [correctChoice, new() { Content = "When", OrderIndex = 1 }];
        var wrong = new PracticeQuestion
        {
            Question = NewQuestion("Choose the correct time.", QuestionType.MultipleChoice), OrderIndex = 1
        };
        wrong.Question.Options = [wrongItemCorrectChoice, wrongChoice];
        var blank = new PracticeQuestion
        {
            Question = NewQuestion("Complete the phrase.", QuestionType.FillBlank), OrderIndex = 2
        };
        blank.Question.Explanation = "UNPUBLISHED_REVIEW_EXPLANATION";
        blank.Question.AcceptedAnswers = [new() { AnswerText = "private fill answer" }];
        var practice = new Practice
        {
            OwnerTeacherUserId = teacher.Id, Module = module, Title = "Review Practice",
            Status = PracticeStatus.Published, Questions = [correct, wrong, blank]
        };
        db.AddRange(practice, new Enrollment { Course = course, StudentUserId = student.Id },
            new Enrollment { Course = course, StudentUserId = other.Id });
        await db.SaveChangesAsync();
        return new(student, other, practice.Id, correct.Id, wrong.Id, blank.Id, correctChoice.Id, wrongItemCorrectChoice.Id, wrongChoice.Id);

        Question NewQuestion(string content, QuestionType type) => new()
        {
            OwnerTeacherUserId = teacher.Id, Content = content, PrimarySkill = EnglishSkill.Grammar,
            QuestionType = type, Status = QuestionStatus.Published
        };
    }

    private sealed record PracticeData(ApplicationUser Student, ApplicationUser OtherStudent, Guid PracticeId,
        Guid CorrectItemId, Guid WrongItemId, Guid BlankItemId, Guid CorrectChoiceId, Guid WrongItemCorrectChoiceId, Guid WrongChoiceId);
}
