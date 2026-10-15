using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class Question : BaseEntity
{
    public Guid OwnerTeacherUserId { get; set; }
    public Guid? StimulusId { get; set; }
    public Stimulus? Stimulus { get; set; }
    public string Content { get; set; } = string.Empty;
    public EnglishSkill PrimarySkill { get; set; }
    public string? Level { get; set; }
    public string? Difficulty { get; set; }
    public string? Explanation { get; set; }
    public QuestionStatus Status { get; set; } = QuestionStatus.Draft;
    // Thời điểm hệ thống ghi nhận nội dung bị khóa.
    // Null nghĩa là chưa có dấu khóa được lưu.
    // private set: code bên ngoài không được tự gán hoặc xóa dấu khóa.
    public DateTimeOffset? ContentLockedAtUtc { get; private set; }

    /// <summary>
    /// Ghi nhận khóa nội dung một lần.
    /// Gọi lại không làm thay đổi thời điểm khóa đã lưu.
    /// Thời gian phải do server cung cấp, không lấy từ dữ liệu người dùng gửi lên.
    /// </summary>
    public void LockContent(DateTimeOffset lockedAtUtc)
    {
        // Đã khóa thì giữ nguyên, không ghi đè thời điểm khóa.
        if (ContentLockedAtUtc.HasValue)
        {
            return;
        }

        // Chuẩn hóa thời gian về UTC trước khi lưu.
        ContentLockedAtUtc = lockedAtUtc.ToUniversalTime();
    }

    public ICollection<QuestionOption> Options { get; set; } = new List<QuestionOption>();
    public ICollection<PracticeQuestion> PracticeQuestions { get; set; } = new List<PracticeQuestion>();
    public ICollection<AssessmentQuestion> AssessmentQuestions { get; set; } = new List<AssessmentQuestion>();
}
