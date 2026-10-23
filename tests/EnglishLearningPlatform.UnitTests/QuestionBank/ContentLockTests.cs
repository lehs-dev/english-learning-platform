using EnglishLearningPlatform.Domain.Entities;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.QuestionBank;

public sealed class ContentLockTests
{
    // Question và Stimulus mới chưa được ghi nhận khóa nội dung.
    [Fact]
    public void NewEntities_HaveNoContentLock()
    {
        var question = new Question();
        var stimulus = new Stimulus();

        Assert.Null(question.ContentLockedAtUtc);
        Assert.Null(stimulus.ContentLockedAtUtc);
    }

    // Đầu vào UTC+7 phải được lưu thành UTC, nhưng vẫn giữ đúng thời điểm.
    [Fact]
    public void LockContent_FirstCall_StoresTimeInUtc()
    {
        var question = new Question();
        var stimulus = new Stimulus();
        var localTime = new DateTimeOffset(2026, 10, 16, 9, 30, 0, TimeSpan.FromHours(7));
        var expectedUtc = new DateTimeOffset(2026, 10, 16, 2, 30, 0, TimeSpan.Zero);

        question.LockContent(localTime);
        stimulus.LockContent(localTime);

        Assert.Equal(expectedUtc, question.ContentLockedAtUtc);
        Assert.Equal(expectedUtc, stimulus.ContentLockedAtUtc);
        Assert.Equal(TimeSpan.Zero, question.ContentLockedAtUtc!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, stimulus.ContentLockedAtUtc!.Value.Offset);
    }

    // Gọi lại với thời gian sớm hơn hoặc muộn hơn đều không được ghi đè dấu khóa.
    [Fact]
    public void LockContent_RepeatedCalls_KeepFirstTime()
    {
        var question = new Question();
        var stimulus = new Stimulus();
        var firstTime = new DateTimeOffset(2026, 10, 16, 2, 30, 0, TimeSpan.Zero);

        question.LockContent(firstTime);
        stimulus.LockContent(firstTime);
        question.LockContent(firstTime.AddDays(1));
        stimulus.LockContent(firstTime.AddDays(1));
        question.LockContent(firstTime.AddDays(-1));
        stimulus.LockContent(firstTime.AddDays(-1));

        Assert.Equal(firstTime, question.ContentLockedAtUtc);
        Assert.Equal(firstTime, stimulus.ContentLockedAtUtc);
    }
}
