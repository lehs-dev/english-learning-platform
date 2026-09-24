namespace EnglishLearningPlatform.Application.Identity;

public interface ILoginService
{
    Task<LoginResult> LoginAsync(LoginRequest request);
}
