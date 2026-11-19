using System.Text;
using System.Text.RegularExpressions;

namespace EnglishLearningPlatform.Application.Practice;

public static partial class AnswerNormalizer
{
    public static string Normalize(string? input) => string.IsNullOrWhiteSpace(input)
        ? string.Empty : Whitespace().Replace(input.Normalize(NormalizationForm.FormC).Trim(), " ");

    public static bool Matches(string? studentInput, IEnumerable<string> acceptedAnswers)
    {
        var input = Normalize(studentInput);
        return input.Length > 0 && acceptedAnswers.Any(answer => string.Equals(input, Normalize(answer), StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
