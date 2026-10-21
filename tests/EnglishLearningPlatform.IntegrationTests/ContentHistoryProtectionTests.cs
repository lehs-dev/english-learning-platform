using System.Net;
using System.Text.Json;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class ContentHistoryProtectionTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData("QuestionContent")]
    [InlineData("Skill")]
    [InlineData("StimulusId")]
    [InlineData("Explanation")]
    [InlineData("OptionText")]
    [InlineData("OptionOrder")]
    [InlineData("CorrectChoice")]
    [InlineData("DeleteOption")]
    [InlineData("Passage")]
    [InlineData("DeleteStimulus")]
    [InlineData("ClearQuestionLock")]
    [InlineData("ClearStimulusLock")]
    public async Task EfWrite_RejectsProtectedChanges_EvenWithoutParentLinksOrTrustedOriginalValues(string change)
    {
        var data = await SeedAsync();
        string before = await SnapshotAsync(data.Teacher.Id);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var q = await db.Questions.Include(q => q.Stimulus).Include(q => q.Options).SingleAsync(q => q.Id == data.Id);
        var options = q.Options.OrderBy(o => o.OrderIndex).ToArray();
        switch (change)
        {
            case "QuestionContent": q.Content = "New content"; break;
            case "Skill": q.PrimarySkill = EnglishSkill.Listening; break;
            case "StimulusId": q.StimulusId = null; break;
            case "Explanation": q.Explanation = "New explanation"; break;
            case "OptionText": options[0].Content = "New option"; break;
            case "OptionOrder": options[0].OrderIndex = 5; break;
            case "CorrectChoice": options[0].IsCorrect = false; options[1].IsCorrect = true; break;
            case "DeleteOption": db.QuestionOptions.Remove(options[0]); break;
            case "Passage": q.Stimulus!.ContentText = "New passage"; break;
            case "DeleteStimulus": db.Stimuli.Remove(q.Stimulus!); break;
            case "ClearQuestionLock": db.Entry(q).Property(nameof(q.ContentLockedAtUtc)).CurrentValue = null; break;
            case "ClearStimulusLock": db.Entry(q.Stimulus!).Property(nameof(Stimulus.ContentLockedAtUtc)).CurrentValue = null; break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(before, await SnapshotAsync(data.Teacher.Id));
    }

    [Fact]
    public async Task SharedStimulus_UnlockedDraftSiblingCannotChangeAudioOrPassageUsedByLockedQuestion_SyncPathAlsoProtected()
    {
        var data = await SeedAsync();
        string before = await SnapshotAsync(data.Teacher.Id);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sibling = await db.Questions.Include(q => q.Stimulus).SingleAsync(q => q.OwnerTeacherUserId == data.Teacher.Id && q.Id != data.Id);
        Assert.Null(sibling.ContentLockedAtUtc);
        sibling.Stimulus!.ResourceUrl = "https://external.test/replaced.mp3";
        sibling.Stimulus.StimulusType = StimulusType.Audio;
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Equal(before, await SnapshotAsync(data.Teacher.Id));
    }

    [Fact]
    public async Task ForgedQuestionPosts_CannotChangeLockedFieldsOrExplanation()
    {
        var data = await SeedAsync();
        string before = await SnapshotAsync(data.Teacher.Id);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, data.Teacher);
        string path = "/TeacherQuestionBank/Edit/" + data.Id;
        string token = await IdentityTestHelpers.GetTokenAsync(client, path);
        var original = new Dictionary<string, string>
        {
            ["Content"] = "Original", ["PrimarySkill"] = ((int)EnglishSkill.Reading).ToString(),
            ["StimulusId"] = data.StimulusId.ToString(), ["Explanation"] = "Original explanation",
            ["Options[0].Content"] = "Correct", ["Options[1].Content"] = "Wrong", ["CorrectOptionIndex"] = "0",
            ["__RequestVerificationToken"] = token
        };
        foreach (var forged in new Dictionary<string, string>
        { ["Content"] = "Tampered", ["PrimarySkill"] = ((int)EnglishSkill.Grammar).ToString(),
            ["StimulusId"] = "", ["Explanation"] = "Tampered explanation", ["Options[0].Content"] = "Tampered option", ["CorrectOptionIndex"] = "1" })
        {
            var fields = new Dictionary<string, string>(original) { [forged.Key] = forged.Value };
            using var rejected = await client.PostAsync(path, new FormUrlEncodedContent(fields));
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode); // Form lỗi, không redirect như lưu thành công.
            Assert.Contains("bị khóa", WebUtility.HtmlDecode(await rejected.Content.ReadAsStringAsync()));
            Assert.Equal(before, await SnapshotAsync(data.Teacher.Id));
        }
    }

    private async Task<string> SnapshotAsync(Guid teacherId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return JsonSerializer.Serialize(await db.Questions.AsNoTracking().Where(q => q.OwnerTeacherUserId == teacherId).OrderBy(q => q.Id)
            .Select(q => new { q.Id, q.Content, q.PrimarySkill, q.Explanation, q.StimulusId, q.ContentLockedAtUtc,
                Passage = q.Stimulus!.ContentText, Audio = q.Stimulus.ResourceUrl, Lock = q.Stimulus.ContentLockedAtUtc,
                Options = q.Options.OrderBy(o => o.OrderIndex).Select(o => new { o.Id, o.Content, o.IsCorrect, o.OrderIndex }).ToList() }).ToListAsync());
    }

    private async Task<Data> SeedAsync()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var stimulus = new Stimulus { OwnerTeacherUserId = teacher.Id, StimulusType = StimulusType.Passage, ContentText = "Original passage" };
        stimulus.LockContent(DateTimeOffset.UtcNow.AddDays(-1));
        Question NewQuestion() => new() { OwnerTeacherUserId = teacher.Id, Content = "Original", PrimarySkill = EnglishSkill.Reading,
            Explanation = "Original explanation", Stimulus = stimulus,
            Options = new List<QuestionOption> { new() { Content = "Correct", IsCorrect = true }, new() { Content = "Wrong", OrderIndex = 1 } } };
        var locked = NewQuestion(); locked.LockContent(DateTimeOffset.UtcNow.AddDays(-1));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Questions.AddRange(locked, NewQuestion());
        await db.SaveChangesAsync();
        return new Data(teacher, locked.Id, stimulus.Id);
    }
    private sealed record Data(ApplicationUser Teacher, Guid Id, Guid StimulusId);
}
