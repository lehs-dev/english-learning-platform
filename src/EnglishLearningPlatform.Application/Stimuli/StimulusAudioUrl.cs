using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Application.Stimuli;

public static class StimulusAudioUrl
{
    // Accept only server-generated paths, without traversal, query strings or another host.
    public static bool TryGetHash(string? url, out string hash)
    {
        hash = string.Empty;
        if (url == null || !url.StartsWith(StimulusAudioConstants.RoutePrefix, StringComparison.Ordinal)) return false;
        string candidate = url[StimulusAudioConstants.RoutePrefix.Length..];
        if (!StimulusAudioResource.IsValidHash(candidate)) return false;
        hash = candidate;
        return true;
    }
}
