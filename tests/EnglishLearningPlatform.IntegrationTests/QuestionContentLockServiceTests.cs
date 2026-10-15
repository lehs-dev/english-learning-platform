using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Dùng database riêng của fixture; không tạo thêm test HTTP hoặc test phân quyền.
public sealed class QuestionContentLockServiceTests(IntegrationTestFactory factory)
    : IClassFixture<IntegrationTestFactory>
{
    private static readonly DateTimeOffset LockedAt =
        new(2026, 10, 16, 2, 30, 0, TimeSpan.Zero);

    // Draft không còn liên kết vẫn phải bị khóa chỉ nhờ dấu khóa đã lưu.
    [Fact]
    public async Task PersistedLock_WithoutParentLinks_LocksEditorAndRejectsContentChange()
    {
        var data = await SeedLockedQuestionAsync();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IQuestionAuthoringService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var editor = await service.GetEditorAsync(data.TeacherId, data.QuestionId);
        Assert.NotNull(editor);
        Assert.True(editor.ScoringContentLocked);
        Assert.False(string.IsNullOrWhiteSpace(editor.LockReason));

        var request = OriginalRequest() with { Content = "Changed scoring content" };
        var result = await service.UpdateAsync(data.TeacherId, data.QuestionId, request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "locked");
        db.ChangeTracker.Clear();
        var saved = await db.Questions.Include(q => q.Options).SingleAsync(q => q.Id == data.QuestionId);
        Assert.Equal(QuestionStatus.Draft, saved.Status);
        Assert.Equal(OriginalRequest().Content, saved.Content);
        Assert.Equal(LockedAt, saved.ContentLockedAtUtc);
        Assert.Null(saved.UpdatedAtUtc); // GET và lần sửa bị từ chối không được ghi dữ liệu.
        Assert.Equal(new[] { "First", "Second" }, saved.Options.OrderBy(o => o.OrderIndex).Select(o => o.Content));
        Assert.False(await db.AssessmentQuestions.AnyAsync(q => q.QuestionId == data.QuestionId));
        Assert.False(await db.PracticeQuestions.AnyAsync(q => q.QuestionId == data.QuestionId));
    }

    // Giữ nguyên chính sách metadata của service hiện có, không khóa mọi trường.
    [Fact]
    public async Task PersistedLock_AllowsMetadataUpdateWithoutChangingScoringOrLockTime()
    {
        var data = await SeedLockedQuestionAsync();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IQuestionAuthoringService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = OriginalRequest() with { Level = "A2", Difficulty = "Medium",
            Explanation = "Updated explanation" };

        var result = await service.UpdateAsync(data.TeacherId, data.QuestionId, request);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
        db.ChangeTracker.Clear();
        var saved = await db.Questions.Include(q => q.Options).SingleAsync(q => q.Id == data.QuestionId);
        Assert.Equal(request.Level, saved.Level);
        Assert.Equal(request.Difficulty, saved.Difficulty);
        Assert.Equal(request.Explanation, saved.Explanation);
        Assert.Equal(OriginalRequest().Content, saved.Content);
        Assert.Equal(EnglishSkill.Reading, saved.PrimarySkill);
        Assert.Equal(LockedAt, saved.ContentLockedAtUtc);
        var options = saved.Options.OrderBy(o => o.OrderIndex).ToArray();
        Assert.Equal(new[] { "First", "Second" }, options.Select(o => o.Content));
        Assert.Equal(new[] { true, false }, options.Select(o => o.IsCorrect));
        var editor = await service.GetEditorAsync(data.TeacherId, data.QuestionId);
        Assert.NotNull(editor);
        Assert.True(editor.ScoringContentLocked);
    }

    // Không tạo Practice/Assessment: tránh để kiểm tra cũ vô tình làm test pass.
    private async Task<(Guid TeacherId, Guid QuestionId)> SeedLockedQuestionAsync()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = OriginalRequest();
        var question = new Question { OwnerTeacherUserId = teacher.Id, Content = request.Content,
            PrimarySkill = EnglishSkill.Reading, Level = request.Level, Difficulty = request.Difficulty,
            Explanation = request.Explanation, Status = QuestionStatus.Draft };
        question.Options.Add(new QuestionOption { Content = "First", IsCorrect = true, OrderIndex = 0 });
        question.Options.Add(new QuestionOption { Content = "Second", IsCorrect = false, OrderIndex = 1 });
        question.LockContent(LockedAt);
        db.Questions.Add(question);
        await db.SaveChangesAsync();
        return (teacher.Id, question.Id);
    }

    private static CreateQuestionRequest OriginalRequest() => new()
    {
        Content = "Choose the correct answer.", PrimarySkill = EnglishSkill.Reading,
        Level = "Beginner", Difficulty = "Easy", Explanation = "Original explanation",
        Options = new[] { new QuestionOptionRequest("First", true), new QuestionOptionRequest("Second", false) }
    };
}
