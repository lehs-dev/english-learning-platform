using EnglishLearningPlatform.Application.Assessment;

namespace EnglishLearningPlatform.Web.Models.Assessment;

public sealed class TeacherAssessmentDraftViewModel
{
    // null: tạo mới; có giá trị: sửa Assessment tương ứng.
    public Guid? Id { get; set; }

    public AssessmentDraftRequest Input { get; set; }
        = new AssessmentDraftRequest();
}