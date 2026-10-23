using EnglishLearningPlatform.Domain.Entities;

using AssessmentEntity =
    EnglishLearningPlatform.Domain.Entities.Assessment;

namespace EnglishLearningPlatform.Application.Assessment;

public interface IAssessmentDefinitionValidator
{
    // Caller phải đọc dữ liệu thật từ database:
    // - Questions gồm Question, Options và Stimulus.
    // - linkedCourse là Course đang gắn với Assessment.
    // - finalCourses là các Course đang chọn Assessment này làm Final.
    //
    // isFirstPublish:
    // true: Publish lần đầu, từ chối câu Archived.
    // false: kiểm tra định nghĩa Published hiện có.
    //
    // Hàm chỉ trả lỗi, không đổi trạng thái hoặc khóa nội dung.
    IReadOnlyList<string> Validate(
        AssessmentEntity assessment,
        IReadOnlyList<AssessmentQuestion> questions,
        Course? linkedCourse,
        IReadOnlyList<Course> finalCourses,
        bool isFirstPublish);
}