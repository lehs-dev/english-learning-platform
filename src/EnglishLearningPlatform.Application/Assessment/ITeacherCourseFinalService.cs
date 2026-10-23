using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Application.Assessment;

public interface ITeacherCourseFinalService
{
    // Owner được xem trang chỉ đọc khi Course Archived hoặc Final đã có Attempt.
    Task<LearningOutcome<TeacherCourseFinalEditor>> GetEditorAsync(
        Guid actorUserId, Guid courseId, CancellationToken cancellationToken = default);

    // Thay Final và cập nhật completion trong cùng transaction.
    Task<WriteOutcome> SaveAsync(
        Guid actorUserId, Guid courseId, CourseFinalRequest request,
        CancellationToken cancellationToken = default);
}
