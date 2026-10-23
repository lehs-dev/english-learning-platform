using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Web.Models.Assessment;

public sealed class TeacherAssessmentDraftViewModel
{
    // null: tạo mới; có giá trị: sửa Assessment tương ứng.
    public Guid? Id { get; set; }

    // Controller lấy từ server, không lấy từ form.
    public AssessmentStatus Status { get; set; } = AssessmentStatus.Draft;

    public AssessmentDraftRequest Input { get; set; }
        = new AssessmentDraftRequest();
}
