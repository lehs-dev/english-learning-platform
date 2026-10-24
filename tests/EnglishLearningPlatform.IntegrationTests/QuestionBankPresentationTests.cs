using System.Net;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class QuestionBankPresentationTests(IntegrationTestFactory factory)
    : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData("A1")]
    [InlineData("A2")]
    [InlineData("B1")]
    [InlineData("B2")]
    [InlineData("C1")]
    [InlineData("C2")]
    public async Task Create_SupportsEachCefrLevel_AndIgnoresRemovedDifficulty(string level)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var client = IdentityTestHelpers.CreateClient(factory);
        await IdentityTestHelpers.LoginAsync(client, teacher);
        var content = "Câu hỏi trình độ " + Guid.NewGuid().ToString("N");
        var token = await IdentityTestHelpers.GetTokenAsync(client, "/TeacherQuestionBank/Create");

        using var response = await client.PostAsync("/TeacherQuestionBank/Create",
            new FormUrlEncodedContent(Form(token, content, level)));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var question = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Questions
            .SingleAsync(q => q.Content == content);
        Assert.Equal(level, question.Level);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Beginner")]
    [InlineData("B3")]
    [InlineData("A0")]
    [InlineData("C3")]
    public async Task Create_RejectsMissingOrUnsupportedLevel_WithoutLosingFormInput(string level)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var client = IdentityTestHelpers.CreateClient(factory);
        await IdentityTestHelpers.LoginAsync(client, teacher);
        var content = "Giữ nội dung khi nhập sai " + Guid.NewGuid().ToString("N");
        var token = await IdentityTestHelpers.GetTokenAsync(client, "/TeacherQuestionBank/Create");

        using var response = await client.PostAsync("/TeacherQuestionBank/Create",
            new FormUrlEncodedContent(Form(token, content, level)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains(content, html);
        Assert.Contains("trình độ", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("name=\"Difficulty\"", html);
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Questions
            .AnyAsync(q => q.Content == content));
    }

    [Fact]
    public async Task List_UsesVietnameseAttributesAndCefrFilters_AndKeepsLegacyQuestionsVisible()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var otherTeacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var prefix = "Ngân hàng " + Guid.NewGuid().ToString("N");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Questions.AddRange(
                Question(teacher.Id, prefix + " một", "A1"),
                Question(teacher.Id, prefix + " hai", "A1"),
                Question(teacher.Id, prefix + " cũ", "Beginner"),
                Question(otherTeacher.Id, prefix + " riêng tư", "A1"));
            await db.SaveChangesAsync();
        }
        using var client = IdentityTestHelpers.CreateClient(factory);
        await IdentityTestHelpers.LoginAsync(client, teacher);
        var url = "/TeacherQuestionBank?Search=" + Uri.EscapeDataString(prefix);
        using var filteredResponse = await client.GetAsync(url + "&Level=A1&PageSize=1&Difficulty=unused");
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        var filtered = WebUtility.HtmlDecode(await filteredResponse.Content.ReadAsStringAsync());
        Assert.Contains("Ngữ pháp", filtered);
        Assert.Contains("Bản nháp", filtered);
        Assert.Contains("Hiển thị 1–1 / 2", filtered);
        Assert.Contains("Level=A1", filtered);
        Assert.DoesNotContain("riêng tư", filtered);
        Assert.DoesNotContain("name=\"Difficulty\"", filtered);
        Assert.DoesNotContain("Độ khó", filtered);
        var levelOptions = Regex.Match(filtered, "<select id=\"Level\"[^>]*>(.*?)</select>", RegexOptions.Singleline).Groups[1].Value;
        foreach (var level in new[] { "A1", "A2", "B1", "B2", "C1", "C2" })
            Assert.Contains("value=\"" + level + "\"", levelOptions);

        using var allResponse = await client.GetAsync(url);
        var all = WebUtility.HtmlDecode(await allResponse.Content.ReadAsStringAsync());
        Assert.Contains(prefix + " cũ", all);
        Assert.Contains("Chưa phân loại", all);
        Assert.DoesNotContain(">Beginner<", all);
        Assert.DoesNotContain("riêng tư", all);
    }

    private static Dictionary<string, string> Form(string token, string content, string level) => new()
    {
        ["__RequestVerificationToken"] = token,
        ["Content"] = content,
        ["PrimarySkill"] = ((int)EnglishSkill.Grammar).ToString(),
        ["Level"] = level,
        ["Difficulty"] = "Tampered legacy value",
        ["Options[0].Content"] = "Lựa chọn thứ nhất",
        ["Options[1].Content"] = "Lựa chọn thứ hai",
        ["CorrectOptionIndex"] = "0"
    };

    private static Question Question(Guid owner, string content, string level) => new()
    {
        OwnerTeacherUserId = owner,
        Content = content,
        PrimarySkill = EnglishSkill.Grammar,
        Level = level,
        Options = [
            new QuestionOption { Content = "Đúng", IsCorrect = true, OrderIndex = 0 },
            new QuestionOption { Content = "Sai", OrderIndex = 1 }
        ]
    };
}
