
namespace EnglishLearningPlatform.Application.Identity;

public sealed record RegisterStudentResult(
    bool Succeeded,
    IReadOnlyList<string> Errors)
{
    public static RegisterStudentResult Success() =>
        new(true, Array.Empty<string>());

    public static RegisterStudentResult Failure(IEnumerable<string> errors) =>
        new(false, errors.ToArray());
}
