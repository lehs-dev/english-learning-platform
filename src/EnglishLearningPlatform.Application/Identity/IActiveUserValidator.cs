namespace EnglishLearningPlatform.Application.Identity;

public interface IActiveUserValidator
{
    Task<bool> IsActiveRoleAsync(Guid userId, string role, CancellationToken ct = default);
    Task<bool> IsActiveStudentAsync(Guid userId, CancellationToken ct = default);
    Task<bool> IsActiveTeacherAsync(Guid userId, CancellationToken ct = default);
    Task<bool> IsActiveAdminAsync(Guid userId, CancellationToken ct = default);
}
