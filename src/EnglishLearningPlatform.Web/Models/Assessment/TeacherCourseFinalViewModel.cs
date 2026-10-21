using EnglishLearningPlatform.Application.Assessment;

namespace EnglishLearningPlatform.Web.Models.Assessment;

// Editor là dữ liệu server; Input chỉ chứa lựa chọn Final và version của form.
public sealed class TeacherCourseFinalViewModel
{
    public TeacherCourseFinalEditor Editor { get; set; } = new();
    public CourseFinalRequest Input { get; set; } = new();
}
