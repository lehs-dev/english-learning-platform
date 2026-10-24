using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public static class CourseRules
{
    public static bool ValidPrice(bool paid, decimal price) =>
        price <= 9999999999999999.99m && decimal.Round(price, 2) == price && (paid ? price > 0 : price == 0);

    public static bool ValidResource(LessonResourceType type, string? text, string? url) =>
        Enum.IsDefined(type) && (type == LessonResourceType.Text ? !string.IsNullOrWhiteSpace(text) :
            url?.Length <= 2048 && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));

    public static IReadOnlyList<string> PublishErrors(Course course)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(course.Title) || course.Title.Trim().Length > 200) errors.Add("Tên khóa học không hợp lệ; cần từ 1 đến 200 ký tự.");
        if (!Enum.IsDefined(course.Level)) errors.Add("Cấp độ khóa học không hợp lệ.");
        if (!ValidPrice(course.IsPaid, course.Price)) errors.Add("Khóa học miễn phí phải có giá 0 đồng. Khóa học có phí phải có giá lớn hơn 0, tối đa 2 chữ số thập phân và trong giới hạn cho phép.");
        if (course.Description?.Length > 4000 || course.Objectives?.Length > 4000) errors.Add("Mô tả và mục tiêu học tập tối đa 4.000 ký tự mỗi phần.");
        if (course.Skills.Any(s => !Enum.IsDefined(s.Skill)) || course.Topics.Any(t => string.IsNullOrWhiteSpace(t.Topic) || t.Topic.Length > 120)) errors.Add("Kỹ năng hoặc chủ đề khóa học không hợp lệ.");
        foreach (var module in course.Modules)
        {
            if (!Enum.IsDefined(module.Visibility) || string.IsNullOrWhiteSpace(module.Title) || module.Title.Length > 200) errors.Add($"Chương học '{module.Title}': thông tin không hợp lệ.");
            foreach (var lesson in module.Lessons)
            {
                if (!Enum.IsDefined(lesson.Status) || string.IsNullOrWhiteSpace(lesson.Title) || lesson.Title.Length > 200) errors.Add($"Bài học '{lesson.Title}': thông tin không hợp lệ.");
                if (lesson.Status == LessonStatus.Published &&
                    (lesson.Resources.Count == 0 || lesson.Resources.Any(r => !ValidResource(r.ResourceType, r.ContentText, r.ResourceUrl))))
                    errors.Add($"Chương '{module.Title}' / Bài học '{lesson.Title}': cần ít nhất một tài nguyên và mọi tài nguyên phải hợp lệ.");
            }
        }
        if (!course.Modules.Any(m => m.Visibility == ModuleVisibility.Visible && m.Lessons.Any(l =>
            l.Status == LessonStatus.Published && l.Resources.Count > 0 && l.Resources.All(r => ValidResource(r.ResourceType, r.ContentText, r.ResourceUrl)))))
            errors.Add("Cần ít nhất một chương đang hiển thị, có bài học đã xuất bản và nội dung hợp lệ.");
        if (course.FinalAssessmentId.HasValue && (course.FinalAssessment is null ||
            course.FinalAssessment.CourseId != course.Id || course.FinalAssessment.OwnerTeacherUserId != course.OwnerTeacherUserId ||
            course.FinalAssessment.Status != AssessmentStatus.Published ||
            course.FinalAssessment.AssessmentType is not (AssessmentType.SkillAssessment or AssessmentType.PracticeExam) ||
            course.FinalAssessment.PassingScore is null or < 0 or > 100))
            errors.Add("Bài kiểm tra cuối khóa phải được xuất bản, thuộc cùng khóa học và giảng viên, là bài đánh giá kỹ năng hoặc đề luyện thi và có điểm đạt hợp lệ.");
        return errors;
    }
}
