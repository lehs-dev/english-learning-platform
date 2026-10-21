using EnglishLearningPlatform.Application.Assessment;

namespace EnglishLearningPlatform.Web.Models.Assessment;

/// <summary>
/// Tách thông tin đọc từ server khỏi hai trường Teacher được phép gửi lên.
/// Form phạm vi không được thay owner, cấu hình hoặc danh sách câu hỏi.
/// </summary>
public sealed class TeacherAssessmentScopeViewModel
{
    public TeacherAssessmentScopeEditor Editor { get; set; } = new();

    public AssessmentScopeRequest Input { get; set; } = new();
}
