namespace EnglishLearningPlatform.Application.Identity;

public interface IActiveUserRoleReader
{
    Task<string?> GetActiveRoleAsync(Guid userId, CancellationToken ct = default);
}
