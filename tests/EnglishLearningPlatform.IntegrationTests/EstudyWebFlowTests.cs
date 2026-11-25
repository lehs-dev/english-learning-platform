using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Application.Flashcards;
using EnglishLearningPlatform.Application.Gamification;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class EstudyWebFlowTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task StudentPages_RenderNewFeatureLayoutsAndDisableResponseCaching()
    {
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var dashboard = await GetPrivatePageAsync(client, "/Learning/Dashboard");
        Assert.Contains("href=\"/LearningPath\"", dashboard);
        Assert.Contains("href=\"/Flashcards\"", dashboard);
        Assert.Contains("href=\"/Learning/Progress?tab=achievements\"", dashboard);
        var progress = await GetPrivatePageAsync(client, "/Learning/Progress?tab=achievements");
        Assert.Contains("href=\"/Achievements\"", progress);
        Assert.Contains("Hãy đánh giá năng lực trước", await GetPrivatePageAsync(client, "/LearningPath"));
        Assert.Contains("Chưa có bộ thẻ khả dụng", await GetPrivatePageAsync(client, "/Flashcards"));
        Assert.Contains("0 XP", await GetPrivatePageAsync(client, "/Achievements"));
    }

    [Fact]
    public async Task TeacherAuthoringAndAnalytics_RenderOwnedCourseDraftCardsAndFilters()
    {
        var data = await SeedDeckAsync(publish: false);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, data.Teacher);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var create = await GetPrivatePageAsync(client, "/TeacherFlashcards/Create");
        Assert.Contains("name=\"Input.CourseId\"", create);
        Assert.Contains(data.CourseTitle, create);
        Assert.Contains("__RequestVerificationToken", create);
        var index = await GetPrivatePageAsync(client, "/TeacherFlashcards");
        Assert.Contains(data.DeckTitle, index);
        var edit = await GetPrivatePageAsync(client, $"/TeacherFlashcards/Edit/{data.DeckId}");
        Assert.Contains("name=\"FrontText\"", edit);
        Assert.Contains("__RequestVerificationToken", edit);
        Assert.Contains("Web word 0", edit);
        Assert.Contains("Web meaning 4", edit);
        var preview = await GetPrivatePageAsync(client, $"/TeacherFlashcards/Preview/{data.DeckId}");
        Assert.Contains("Web word 0", preview);
        Assert.Contains("Web meaning 4", preview);
        var analytics = await GetPrivatePageAsync(client, $"/TeacherAnalytics?CourseId={data.CourseId}");
        Assert.Contains("Thống kê lớp học", analytics);
        Assert.Contains("name=\"From\"", analytics);
        Assert.Contains("name=\"To\"", analytics);
        Assert.Contains(data.CourseTitle, analytics);
        Assert.Contains(data.Student.FullName, analytics);
    }

    [Fact]
    public async Task TeacherFlashcardCsv_ImportsAllValidRowsOnceAndRejectsInvalidBatch()
    {
        var data = await SeedDeckAsync(publish: false);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, data.Teacher);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/TeacherFlashcards/Edit/{data.DeckId}");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deckCards = db.Set<Flashcard>().AsNoTracking().Where(card => card.DeckId == data.DeckId);
        using var rejected = await ImportAsync("FrontText,BackText\r\nnew word,từ mới\r\ninvalid,\r\n");
        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        Assert.Equal(5, await deckCards.CountAsync());

        const string valid = "FrontText,BackText,Example\r\nnew word,từ mới,\"First, learn the meaning.\"\r\nhello,xin chào,\r\n";
        using var imported = await ImportAsync(valid);
        using var replayed = await ImportAsync(valid);
        Assert.Equal(HttpStatusCode.Redirect, imported.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, replayed.StatusCode);
        var cards = await deckCards.OrderBy(card => card.OrderIndex).ToListAsync();
        Assert.Equal(7, cards.Count);
        Assert.Equal("First, learn the meaning.", cards[^2].Example);
        Assert.Equal("xin chào", cards[^1].BackText);

        async Task<HttpResponseMessage> ImportAsync(string csv)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(token), "__RequestVerificationToken");
            form.Add(new StringContent(csv, Encoding.UTF8, "text/csv"), "csvFile", "words.csv");
            return await client.PostAsync($"/TeacherFlashcards/ImportCards/{data.DeckId}", form);
        }
    }

    [Fact]
    public async Task PublishedDeck_StudyFormEnforcesCsrfAndReplayThenFiveDistinctReviewsAwardXp()
    {
        var data = await SeedDeckAsync(publish: true);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, data.Student);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var study = await GetPrivatePageAsync(client, $"/Flashcards/Study/{data.DeckId}");
        Assert.Contains(data.DeckTitle, study);
        Assert.Contains("data-flashcard-flip", study);
        Assert.Contains("aria-controls=\"card-back\"", study);
        Assert.Contains("Web word 0", study);
        Assert.Contains("Web meaning 0", study);
        Assert.Contains("value=\"Good\"", study);
        var cardId = Guid.Parse(InputValue(study, "flashcardId"));
        var reviewRequestId = Guid.Parse(InputValue(study, "reviewRequestId"));
        var fields = new Dictionary<string, string>
        {
            ["flashcardId"] = cardId.ToString(),
            ["reviewRequestId"] = reviewRequestId.ToString(),
            ["rating"] = "Good"
        };
        using var missingCsrf = await client.PostAsync($"/Flashcards/Review/{data.DeckId}", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        using (var verify = factory.Services.CreateScope())
        {
            var flashcards = verify.ServiceProvider.GetRequiredService<IFlashcardService>();
            Assert.Equal(5, (await flashcards.GetStudyAsync(data.Student.Id, data.DeckId)).Value!.DueCount);
        }
        fields["__RequestVerificationToken"] = InputValue(study, "__RequestVerificationToken");
        using var saved = await client.PostAsync($"/Flashcards/Review/{data.DeckId}", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains($"/Flashcards/Study/{data.DeckId}", saved.Headers.Location!.OriginalString);
        using var replay = await client.PostAsync($"/Flashcards/Review/{data.DeckId}", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
        using var scope = factory.Services.CreateScope();
        var flashcardService = scope.ServiceProvider.GetRequiredService<IFlashcardService>();
        var next = (await flashcardService.GetStudyAsync(data.Student.Id, data.DeckId)).Value!;
        Assert.Equal(4, next.DueCount);
        Assert.NotEqual(cardId, next.Card!.Id);
        Assert.Equal(0, (await scope.ServiceProvider.GetRequiredService<IGamificationService>().GetSummaryAsync(data.Student.Id)).Value!.TotalXp);
        foreach (var remaining in data.CardIds.Where(id => id != cardId))
            Assert.Equal(FlashcardWriteStatus.Saved, (await flashcardService.ReviewAsync(data.Student.Id, data.DeckId,
                remaining, Guid.NewGuid(), FlashcardRating.Good)).Status);
        var completed = (await flashcardService.GetStudyAsync(data.Student.Id, data.DeckId)).Value!;
        Assert.Equal(0, completed.DueCount);
        Assert.Null(completed.Card);
        var summary = (await scope.ServiceProvider.GetRequiredService<IGamificationService>().GetSummaryAsync(data.Student.Id)).Value!;
        Assert.Equal(5, summary.TotalXp);
        Assert.Equal(1, summary.CurrentStreak);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(5, await db.Set<FlashcardReview>().CountAsync(r => r.StudentUserId == data.Student.Id));
        Assert.Equal(1, await db.GamificationEvents.CountAsync(e => e.StudentUserId == data.Student.Id && e.EventType == GamificationEventType.FlashcardsReviewed));
        Assert.Contains("Bạn đã ôn hết các thẻ đến hạn", await GetPrivatePageAsync(client, $"/Flashcards/Study/{data.DeckId}"));
        Assert.Contains("5 XP", await GetPrivatePageAsync(client, "/Achievements"));
        Assert.Contains("5 XP", await GetPrivatePageAsync(client, "/Learning/Dashboard"));
    }

    private static async Task<string> GetPrivatePageAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true, $"{path} must send Cache-Control: no-store.");
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static string InputValue(string html, string name)
    {
        var match = Regex.Match(html, "<input\\b(?=[^>]*\\bname=\"" + Regex.Escape(name) + "\")[^>]*\\bvalue=\"([^\"]*)\"",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"Expected form field {name}.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private async Task<DeckData> SeedDeckAsync(bool publish)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new Course
        {
            OwnerTeacherUserId = teacher.Id, Title = "Web flashcard course " + Guid.NewGuid(), Status = CourseStatus.Published,
            Modules = [new Module { Title = "Web module", Lessons = [new Lesson
            {
                Title = "Web lesson", Status = LessonStatus.Published,
                Resources = [new LessonResource { ResourceType = LessonResourceType.Text, ContentText = "Learn words" }]
            }] }]
        };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        Assert.True((await scope.ServiceProvider.GetRequiredService<ICourseService>().EnrollFreeAsync(student.Id, course.Id)).Success);
        var teacherFlashcards = scope.ServiceProvider.GetRequiredService<ITeacherFlashcardService>();
        var deckTitle = "Web vocabulary " + Guid.NewGuid();
        var created = await teacherFlashcards.CreateAsync(teacher.Id,
            new(course.Id, course.Modules.Single().Lessons.Single().Id, deckTitle, "Five words for HTTP flow"));
        Assert.Equal(FlashcardWriteStatus.Saved, created.Status);
        var deckId = created.Id!.Value;
        for (int index = 0; index < 5; index++)
            Assert.Equal(FlashcardWriteStatus.Saved, (await teacherFlashcards.SaveCardAsync(teacher.Id, deckId, null,
                new($"Web word {index}", $"Web meaning {index}", $"Web example {index}"))).Status);
        if (publish) Assert.Equal(FlashcardWriteStatus.Saved, (await teacherFlashcards.PublishAsync(teacher.Id, deckId)).Status);
        var cards = (await teacherFlashcards.GetDeckAsync(teacher.Id, deckId)).Value!.Cards.Select(c => c.Id).ToArray();
        return new(teacher, student, course.Id, course.Title, deckId, deckTitle, cards);
    }

    private sealed record DeckData(ApplicationUser Teacher, ApplicationUser Student, Guid CourseId, string CourseTitle,
        Guid DeckId, string DeckTitle, IReadOnlyList<Guid> CardIds);
}
