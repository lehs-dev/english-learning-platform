namespace EnglishLearningPlatform.Application.Identity;

public sealed record UpdateProfileResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static UpdateProfileResult Success() => new(true, Array.Empty<string>());
    public static UpdateProfileResult Failure(params string[] errors) => new(false, errors);
}
