using System.Text.Json;
using System.Text.Json.Serialization;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Practice;

public sealed record StructuredQuestionAuthorItem(string Text, string? MatchText = null);
public sealed record StructuredQuestionChoice(Guid Id, string Text);
public sealed record StructuredQuestionPrompt(
    IReadOnlyList<StructuredQuestionChoice> Items,
    IReadOnlyList<StructuredQuestionChoice> Matches);

public sealed record StructuredAnswerEvaluation(
    bool IsValid,
    bool IsCorrect,
    string? CanonicalJson,
    string? SubmittedAnswer,
    string CorrectAnswer);

public static class StructuredQuestionEvaluator
{
    public const int MinItems = 2;
    public const int MaxItems = 10;
    public const int MaxItemLength = 200;
    public const int MaxAnswerLength = 4096;
    private const int MaxDefinitionLength = 32768;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = 4
    };

    private sealed record DefinitionItem(Guid Id, string Text, Guid? MatchId, string? MatchText);
    private sealed record AnswerPair(Guid LeftId, Guid RightId);

    public static bool IsStructured(QuestionType type) => type is QuestionType.Matching or QuestionType.Ordering;

    public static bool IsValidAuthorItems(QuestionType type, IReadOnlyList<StructuredQuestionAuthorItem>? items)
    {
        if (!IsStructured(type) || items is null || items.Count is < MinItems or > MaxItems)
            return false;

        if (items.Any(item => item is null || !IsValidText(item.Text)))
            return false;

        if (items.Select(item => item.Text.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
            return false;

        if (type == QuestionType.Ordering)
            return items.All(item => string.IsNullOrWhiteSpace(item.MatchText));

        return items.All(item => IsValidText(item.MatchText)) &&
               items.Select(item => item.MatchText!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == items.Count;
    }

    public static string? CreateDefinition(QuestionType type, IReadOnlyList<StructuredQuestionAuthorItem>? items)
    {
        if (!IsValidAuthorItems(type, items))
            return null;

        var matching = type == QuestionType.Matching;
        var definition = items!.Select(item => new DefinitionItem(
            Guid.NewGuid(), item.Text.Trim(), matching ? Guid.NewGuid() : null,
            matching ? item.MatchText!.Trim() : null));
        return JsonSerializer.Serialize(definition, JsonOptions);
    }

    public static IReadOnlyList<StructuredQuestionAuthorItem>? GetAuthorItems(Question question) =>
        ReadDefinition(question)?.Select(item => new StructuredQuestionAuthorItem(item.Text, item.MatchText)).ToArray();

    public static bool HasSameDefinition(Question question, IReadOnlyList<StructuredQuestionAuthorItem> items)
    {
        if (!IsStructured(question.QuestionType))
            return question.StructuredDefinitionJson is null && items.Count == 0;

        var saved = GetAuthorItems(question);
        return saved is not null && saved.SequenceEqual(items.Select(item => new StructuredQuestionAuthorItem(
            item.Text.Trim(), question.QuestionType == QuestionType.Matching ? item.MatchText!.Trim() : null)));
    }

    public static StructuredQuestionPrompt? GetPrompt(Question question)
    {
        var definition = ReadDefinition(question);
        if (definition is null)
            return null;

        var items = definition.Select(item => new StructuredQuestionChoice(item.Id, item.Text));
        // Independently generated keys provide stable display order without revealing the solution.
        return question.QuestionType == QuestionType.Ordering
            ? new(items.OrderBy(item => item.Id).ToArray(), [])
            : new(items.ToArray(), definition.Select(item => new StructuredQuestionChoice(item.MatchId!.Value, item.MatchText!))
                .OrderBy(item => item.Id).ToArray());
    }

    public static StructuredAnswerEvaluation Evaluate(Question question, string? responseJson)
    {
        if (responseJson is { Length: > MaxAnswerLength })
            return Invalid();

        var definition = ReadDefinition(question);
        if (definition is null)
            return Invalid();

        var matching = question.QuestionType == QuestionType.Matching;
        var correctAnswer = matching
            ? FormatMatches(definition.Select(item => (item.Text, item.MatchText!)))
            : string.Join(" → ", definition.Select(item => item.Text));
        if (string.IsNullOrWhiteSpace(responseJson))
            return new(true, false, null, null, correctAnswer);

        try
        {
            using var document = JsonDocument.Parse(responseJson, new JsonDocumentOptions { MaxDepth = 3 });
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() != definition.Length)
                return Invalid();

            return matching
                ? EvaluateMatches(document.RootElement, definition, correctAnswer)
                : EvaluateOrder(document.RootElement, definition, correctAnswer);
        }
        catch (JsonException)
        {
            return Invalid();
        }
    }

    private static StructuredAnswerEvaluation EvaluateOrder(JsonElement answer, DefinitionItem[] definition, string correctAnswer)
    {
        var choices = definition.ToDictionary(item => item.Id, item => item.Text);
        var seen = new HashSet<Guid>();
        var ordered = new List<Guid>(definition.Length);
        foreach (var choice in answer.EnumerateArray())
        {
            if (!TryReadId(choice, out var id) || !choices.ContainsKey(id) || !seen.Add(id))
                return Invalid();

            ordered.Add(id);
        }

        return new(true, ordered.SequenceEqual(definition.Select(item => item.Id)),
            JsonSerializer.Serialize(ordered, JsonOptions), string.Join(" → ", ordered.Select(id => choices[id])), correctAnswer);
    }

    private static StructuredAnswerEvaluation EvaluateMatches(JsonElement answer, DefinitionItem[] definition, string correctAnswer)
    {
        var leftChoices = definition.Select(item => item.Id).ToHashSet();
        var rightChoices = definition.ToDictionary(item => item.MatchId!.Value, item => item.MatchText!);
        var pairs = new Dictionary<Guid, Guid>();
        var usedRight = new HashSet<Guid>();
        foreach (var choice in answer.EnumerateArray())
        {
            if (!TryReadPair(choice, out var leftId, out var rightId) || !leftChoices.Contains(leftId) ||
                !rightChoices.ContainsKey(rightId) || !pairs.TryAdd(leftId, rightId) || !usedRight.Add(rightId))
                return Invalid();
        }

        var submitted = definition.Select(item => new AnswerPair(item.Id, pairs[item.Id]));
        return new(true, definition.All(item => pairs[item.Id] == item.MatchId),
            JsonSerializer.Serialize(submitted, JsonOptions),
            FormatMatches(definition.Select(item => (item.Text, rightChoices[pairs[item.Id]]))), correctAnswer);
    }

    private static bool TryReadPair(JsonElement element, out Guid left, out Guid right)
    {
        left = right = Guid.Empty;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("leftId") && left == Guid.Empty && TryReadId(property.Value, out var leftId))
                left = leftId;
            else if (property.NameEquals("rightId") && right == Guid.Empty && TryReadId(property.Value, out var rightId))
                right = rightId;
            else
                return false;
        }

        return left != Guid.Empty && right != Guid.Empty;
    }

    private static bool TryReadId(JsonElement element, out Guid id)
    {
        id = Guid.Empty;
        return element.ValueKind == JsonValueKind.String && element.TryGetGuid(out id) && id != Guid.Empty;
    }

    private static DefinitionItem[]? ReadDefinition(Question question)
    {
        if (!IsStructured(question.QuestionType) || question.StructuredDefinitionJson is not { Length: > 0 and <= MaxDefinitionLength } json)
            return null;

        DefinitionItem[]? definition;
        try
        {
            definition = JsonSerializer.Deserialize<DefinitionItem[]>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (definition is null || definition.Length is < MinItems or > MaxItems ||
            definition.Any(item => item is null || item.Id == Guid.Empty) ||
            !IsValidAuthorItems(question.QuestionType, definition.Select(item => new StructuredQuestionAuthorItem(item.Text, item.MatchText)).ToArray()))
            return null;

        var seen = new HashSet<Guid>();
        foreach (var item in definition)
        {
            if (!seen.Add(item.Id))
                return null;

            if (question.QuestionType == QuestionType.Matching)
            {
                if (item.MatchId is not Guid matchId || matchId == Guid.Empty || !seen.Add(matchId))
                    return null;
            }
            else if (item.MatchId is not null)
                return null;
        }

        return definition;
    }

    private static bool IsValidText(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= MaxItemLength;

    private static string FormatMatches(IEnumerable<(string Left, string Right)> pairs) =>
        string.Join(" · ", pairs.Select(pair => $"{pair.Left} ↔ {pair.Right}"));

    private static StructuredAnswerEvaluation Invalid() => new(false, false, null, null, string.Empty);
}
