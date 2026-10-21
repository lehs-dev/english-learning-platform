using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Assessment;

/// <summary>
/// Dữ liệu server trả về khi mở trang sửa cấu hình.
/// Tách trạng thái thật khỏi dữ liệu người dùng nhập.
/// </summary>
public sealed class TeacherAssessmentConfigurationEditor
{
    public AssessmentStatus Status { get; set; }

    public AssessmentDraftRequest Input { get; set; }
        = new AssessmentDraftRequest();
}
