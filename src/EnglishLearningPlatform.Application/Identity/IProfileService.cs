namespace EnglishLearningPlatform.Application.Identity;

public interface IProfileService
{
    Task<UserProfile?> GetProfileAsync(Guid userId);
    Task<UpdateProfileResult> UpdateFullNameAsync(Guid userId, string fullName);
}
