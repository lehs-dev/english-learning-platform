using System.Net;
using System.Text.Json;
using EnglishLearningPlatform.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Student)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DictionaryController(IHttpClientFactory httpClientFactory) : Controller
{
    public sealed record WordDefinitionItem(string Definition, string? Example);
    public sealed record WordMeaningGroup(
        string PartOfSpeech,
        string PartOfSpeechLabel,
        IReadOnlyList<WordDefinitionItem> Definitions,
        IReadOnlyList<string> Synonyms,
        IReadOnlyList<string> Antonyms);

    public sealed record DictionaryLookupResponse(
        string Query,
        string Direction,
        string Translation,
        IReadOnlyList<string> AlternativeTranslations,
        string? Phonetic,
        string? AudioUrl,
        IReadOnlyList<WordMeaningGroup> MeaningGroups,
        string Source);

    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> Lookup(string? q, string? direction, CancellationToken ct)
    {
        var query = (q ?? "").Trim();
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(new { error = "Vui lòng nhập từ hoặc cụm từ cần tra." });
        if (query.Length > 200)
            query = query[..200];

        var dir = string.Equals(direction, "vi-en", StringComparison.OrdinalIgnoreCase) ? "vi-en" : "en-vi";
        var (sourceLang, targetLang) = dir == "vi-en" ? ("vi", "en") : ("en", "vi");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(6));
        var token = timeoutCts.Token;

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EstudyDictionary/1.0");

        var (translation, alternatives, sourceName) = await TranslateViaMyMemoryAsync(client, query, sourceLang, targetLang, token);
        if (string.IsNullOrWhiteSpace(translation))
        {
            return StatusCode((int)HttpStatusCode.ServiceUnavailable, new
            {
                error = "Không thể kết nối máy chủ từ điển lúc này. Vui lòng thử lại sau."
            });
        }

        var wordForEnglishLookup = dir == "en-vi" ? query : translation;
        var dictTask = TryLookupEnglishEntryAsync(client, wordForEnglishLookup, token);
        var synTask = TryLookupDatamuseSynonymsAsync(client, wordForEnglishLookup, token);
        await Task.WhenAll(dictTask, synTask);

        var (phonetic, audioUrl, dictGroups) = dictTask.Result;
        var datamuseByPos = synTask.Result;
        var mergedGroups = MergeMeaningGroups(dictGroups, datamuseByPos);

        var sources = new List<string> { sourceName };
        if (dictGroups.Count > 0 || !string.IsNullOrWhiteSpace(phonetic)) sources.Add("DictionaryAPI");
        if (datamuseByPos.Count > 0) sources.Add("Datamuse");

        return Json(new DictionaryLookupResponse(
            query,
            dir,
            translation,
            alternatives,
            phonetic,
            audioUrl,
            mergedGroups,
            string.Join(" + ", sources.Distinct())));
    }

    private static async Task<(string Translation, IReadOnlyList<string> Alternatives, string Source)> TranslateViaMyMemoryAsync(
        HttpClient client, string text, string sl, string tl, CancellationToken ct)
    {
        try
        {
            var mmUrl = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair={sl}|{tl}";
            using var response = await client.GetAsync(mmUrl, ct);
            if (!response.IsSuccessStatusCode)
                return ("", [], "");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var translation = "";
            if (root.TryGetProperty("responseData", out var data) &&
                data.TryGetProperty("translatedText", out var textEl))
            {
                translation = (textEl.GetString() ?? "").Trim();
            }

            var alternatives = new List<string>();
            if (root.TryGetProperty("matches", out var matches) && matches.ValueKind == JsonValueKind.Array)
            {
                foreach (var match in matches.EnumerateArray())
                {
                    if (!match.TryGetProperty("translation", out var altEl) || altEl.ValueKind != JsonValueKind.String)
                        continue;
                    var alt = (altEl.GetString() ?? "").Trim().TrimEnd(';', ',', '.');
                    if (string.IsNullOrWhiteSpace(alt)) continue;
                    if (string.Equals(alt, translation, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(alt, text, StringComparison.OrdinalIgnoreCase)) continue;
                    if (alt.Length > 80) continue;
                    if (!alternatives.Contains(alt, StringComparer.OrdinalIgnoreCase))
                        alternatives.Add(alt);
                    if (alternatives.Count >= 5) break;
                }
            }

            return string.IsNullOrWhiteSpace(translation)
                ? ("", [], "")
                : (translation, alternatives, "MyMemory");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (HttpRequestException) { }
        catch (JsonException) { }

        return ("", [], "");
    }

    private static async Task<(string? Phonetic, string? AudioUrl, List<WordMeaningGroup> Groups)> TryLookupEnglishEntryAsync(
        HttpClient client, string candidate, CancellationToken ct)
    {
        var normalized = candidate.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains(' ') || normalized.Length > 45)
            return (null, null, []);

        try
        {
            var url = $"https://api.dictionaryapi.dev/api/v2/entries/en/{Uri.EscapeDataString(normalized.ToLowerInvariant())}";
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return (null, null, []);

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return (null, null, []);

            var entry = doc.RootElement[0];
            string? phonetic = entry.TryGetProperty("phonetic", out var phEl) ? phEl.GetString() : null;
            string? audioUrl = null;
            if (entry.TryGetProperty("phonetics", out var phArray) && phArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in phArray.EnumerateArray())
                {
                    if (string.IsNullOrWhiteSpace(phonetic) && p.TryGetProperty("text", out var txt))
                        phonetic = txt.GetString();
                    if (string.IsNullOrWhiteSpace(audioUrl) && p.TryGetProperty("audio", out var aud))
                    {
                        var candidateAudio = aud.GetString();
                        if (!string.IsNullOrWhiteSpace(candidateAudio) && candidateAudio.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            audioUrl = candidateAudio;
                    }
                }
            }

            var groups = new List<WordMeaningGroup>();
            if (entry.TryGetProperty("meanings", out var meanings) && meanings.ValueKind == JsonValueKind.Array)
            {
                foreach (var meaning in meanings.EnumerateArray())
                {
                    var pos = meaning.TryGetProperty("partOfSpeech", out var posEl)
                        ? (posEl.GetString() ?? "").Trim().ToLowerInvariant()
                        : "other";
                    var defs = new List<WordDefinitionItem>();
                    var synSet = new List<string>();
                    var antSet = new List<string>();

                    AddStringArray(meaning, "synonyms", synSet);
                    AddStringArray(meaning, "antonyms", antSet);

                    if (meaning.TryGetProperty("definitions", out var defsEl) && defsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var d in defsEl.EnumerateArray())
                        {
                            if (d.TryGetProperty("definition", out var df) && df.ValueKind == JsonValueKind.String)
                            {
                                var text = df.GetString();
                                var ex = d.TryGetProperty("example", out var exEl) && exEl.ValueKind == JsonValueKind.String
                                    ? exEl.GetString()
                                    : null;
                                if (!string.IsNullOrWhiteSpace(text) && defs.Count < 3)
                                    defs.Add(new WordDefinitionItem(text, ex));
                            }
                            AddStringArray(d, "synonyms", synSet);
                            AddStringArray(d, "antonyms", antSet);
                        }
                    }

                    groups.Add(new WordMeaningGroup(
                        pos,
                        FormatPartOfSpeech(pos),
                        defs,
                        synSet.Where(s => !string.Equals(s, normalized, StringComparison.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList(),
                        antSet.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList()));
                }
            }

            return (phonetic, audioUrl, groups);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (HttpRequestException) { }
        catch (JsonException) { }

        return (null, null, []);
    }

    private static async Task<Dictionary<string, List<string>>> TryLookupDatamuseSynonymsAsync(
        HttpClient client, string candidate, CancellationToken ct)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var normalized = candidate.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains(' ') || normalized.Length > 45)
            return result;

        try
        {
            var relSynUrl = $"https://api.datamuse.com/words?rel_syn={Uri.EscapeDataString(normalized.ToLowerInvariant())}&md=p&max=12";
            using var synRes = await client.GetAsync(relSynUrl, ct);
            if (synRes.IsSuccessStatusCode)
            {
                await using var stream = await synRes.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                CollectDatamuseWords(doc.RootElement, normalized, requireSynTag: false, result);
            }

            var mlUrl = $"https://api.datamuse.com/words?ml={Uri.EscapeDataString(normalized.ToLowerInvariant())}&md=p&max=12";
            using var mlRes = await client.GetAsync(mlUrl, ct);
            if (mlRes.IsSuccessStatusCode)
            {
                await using var stream = await mlRes.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                CollectDatamuseWords(doc.RootElement, normalized, requireSynTag: true, result);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (HttpRequestException) { }
        catch (JsonException) { }

        return result;
    }

    private static void CollectDatamuseWords(JsonElement array, string sourceWord, bool requireSynTag,
        Dictionary<string, List<string>> target)
    {
        if (array.ValueKind != JsonValueKind.Array) return;
        foreach (var item in array.EnumerateArray())
        {
            if (!item.TryGetProperty("word", out var wordEl) || wordEl.ValueKind != JsonValueKind.String) continue;
            var word = (wordEl.GetString() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(word) ||
                string.Equals(word, sourceWord, StringComparison.OrdinalIgnoreCase))
                continue;

            var posTags = new List<string>();
            var hasSynTag = false;
            if (item.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var tagEl in tagsEl.EnumerateArray())
                {
                    var tag = tagEl.GetString() ?? "";
                    if (tag == "syn") hasSynTag = true;
                    var mapped = tag switch
                    {
                        "n" => "noun",
                        "v" => "verb",
                        "adj" => "adjective",
                        "adv" => "adverb",
                        _ => null
                    };
                    if (mapped is not null) posTags.Add(mapped);
                }
            }

            if (requireSynTag && !hasSynTag) continue;
            if (posTags.Count == 0) posTags.Add("other");

            foreach (var pos in posTags)
            {
                if (!target.TryGetValue(pos, out var list))
                {
                    list = [];
                    target[pos] = list;
                }
                if (!list.Contains(word, StringComparer.OrdinalIgnoreCase) && list.Count < 8)
                    list.Add(word);
            }
        }
    }

    private static List<WordMeaningGroup> MergeMeaningGroups(
        List<WordMeaningGroup> dictGroups,
        Dictionary<string, List<string>> datamuseByPos)
    {
        var merged = new List<WordMeaningGroup>();
        var coveredPos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in dictGroups)
        {
            coveredPos.Add(group.PartOfSpeech);
            var syns = new List<string>(group.Synonyms);
            if (datamuseByPos.TryGetValue(group.PartOfSpeech, out var extra))
            {
                foreach (var w in extra)
                {
                    if (!syns.Contains(w, StringComparer.OrdinalIgnoreCase) && syns.Count < 10)
                        syns.Add(w);
                }
            }
            merged.Add(group with { Synonyms = syns });
        }

        foreach (var (pos, syns) in datamuseByPos)
        {
            if (coveredPos.Contains(pos) || syns.Count == 0) continue;
            merged.Add(new WordMeaningGroup(
                pos,
                FormatPartOfSpeech(pos),
                [],
                syns.Take(10).ToList(),
                []));
        }

        return merged;
    }

    private static void AddStringArray(JsonElement parent, string propertyName, List<string> target)
    {
        if (!parent.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                target.Add(item.GetString()!.Trim());
        }
    }

    private static string FormatPartOfSpeech(string pos) => pos switch
    {
        "noun" => "Danh từ (noun)",
        "verb" => "Động từ (verb)",
        "adjective" => "Tính từ (adjective)",
        "adverb" => "Trạng từ (adverb)",
        "preposition" => "Giới từ (preposition)",
        "conjunction" => "Liên từ (conjunction)",
        "pronoun" => "Đại từ (pronoun)",
        "interjection" => "Thán từ (interjection)",
        _ => "Từ liên quan"
    };
}
