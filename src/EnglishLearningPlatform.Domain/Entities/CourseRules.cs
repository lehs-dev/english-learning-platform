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
        if (string.IsNullOrWhiteSpace(course.Title) || course.Title.Trim().Length > 200) errors.Add("Title: tên khóa học không hợp lệ.");
        if (!Enum.IsDefined(course.Level)) errors.Add("Level: cấp độ không hợp lệ.");
        if (!ValidPrice(course.IsPaid, course.Price)) errors.Add("Price: Free phải có giá 0; Paid phải có giá dương, tối đa 2 chữ số thập phân.");
        if (course.Description?.Length > 4000 || course.Objectives?.Length > 4000) errors.Add("Description/Objectives: tối đa 4000 ký tự.");
        if (course.Skills.Any(s => !Enum.IsDefined(s.Skill)) || course.Topics.Any(t => string.IsNullOrWhiteSpace(t.Topic) || t.Topic.Length > 120)) errors.Add("Skills/Topics: dữ liệu không hợp lệ.");
        foreach (var module in course.Modules)
        {
            if (!Enum.IsDefined(module.Visibility) || string.IsNullOrWhiteSpace(module.Title) || module.Title.Length > 200) errors.Add($"Module '{module.Title}': metadata không hợp lệ.");
            foreach (var lesson in module.Lessons)
            {
                if (!Enum.IsDefined(lesson.Status) || string.IsNullOrWhiteSpace(lesson.Title) || lesson.Title.Length > 200) errors.Add($"Lesson '{lesson.Title}': metadata không hợp lệ.");
                if (lesson.Status == LessonStatus.Published &&
                    (lesson.Resources.Count == 0 || lesson.Resources.Any(r => !ValidResource(r.ResourceType, r.ContentText, r.ResourceUrl))))
                    errors.Add($"Module '{module.Title}' / Lesson '{lesson.Title}': cần ít nhất một resource và mọi resource phải hợp lệ.");
            }
        }
        if (!course.Modules.Any(m => m.Visibility == ModuleVisibility.Visible && m.Lessons.Any(l =>
            l.Status == LessonStatus.Published && l.Resources.Count > 0 && l.Resources.All(r => ValidResource(r.ResourceType, r.ContentText, r.ResourceUrl)))))
            errors.Add("Cần ít nhất một Module Visible chứa Lesson Published có nội dung hợp lệ.");
        if (course.FinalAssessmentId.HasValue && (course.FinalAssessment is null ||
            course.FinalAssessment.CourseId != course.Id || course.FinalAssessment.OwnerTeacherUserId != course.OwnerTeacherUserId ||
            course.FinalAssessment.Status != AssessmentStatus.Published ||
            course.FinalAssessment.AssessmentType is not (AssessmentType.SkillAssessment or AssessmentType.PracticeExam) ||
            course.FinalAssessment.PassingScore is null or < 0 or > 100))
            errors.Add("FinalAssessment: phải Published, cùng khóa học/owner, thuộc SkillAssessment/PracticeExam và có PassingScore hợp lệ.");
        return errors;
    }
}
