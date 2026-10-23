namespace EnglishLearningPlatform.Application.QuestionBank;

public interface IQuestionAuthoringService
{
    Task<QuestionEditor?> GetEditorAsync(
        Guid actorUserId,
        Guid questionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QuestionStimulusOption>> GetStimuliAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lưu trữ câu hỏi thuộc quyền sở hữu của Teacher.
    /// Không xóa câu hỏi, lựa chọn hoặc dữ liệu làm bài cũ.
    /// </summary>
    Task<QuestionWriteResult> ArchiveAsync(
        Guid actorUserId,
        Guid questionId,
        CancellationToken cancellationToken = default);

    Task<QuestionWriteResult> UpdateAsync(
        Guid actorUserId,
        Guid questionId,
        CreateQuestionRequest request,
        CancellationToken cancellationToken = default);
}
