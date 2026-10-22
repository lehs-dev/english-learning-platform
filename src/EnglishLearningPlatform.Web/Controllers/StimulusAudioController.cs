using System.Security.Claims;
using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StimulusAudioController(IStimulusAudioStore store, IStimulusAudioAccessService access,
    ILogger<StimulusAudioController> logger) : Controller
{
    [HttpGet("/StimulusAudio/Play/{hash}")]
    public async Task<IActionResult> Play(string hash, CancellationToken ct)
    {
        string url = StimulusAudioConstants.RoutePrefix + hash;
        if (!StimulusAudioUrl.TryGetHash(url, out _)
            || !Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid actor)
            || !await access.CanReadAsync(actor, url, ct)) return NotFound();
        try
        {
            var audio = await store.ReadAsync(hash, ct);
            if (audio == null) return NotFound();
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(audio.Content, audio.ContentType, enableRangeProcessing: true);
        }
        catch (Exception exception) when (exception is IOException || exception is InvalidDataException)
        {
            logger.LogError(exception, "Stored stimulus audio unavailable: {Hash}.", hash);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
