using System.Security.Claims;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LessonMediaController(IStudentLearningCourseService learning, LessonMediaStore store) : Controller
{
    [HttpGet(LessonMediaResource.RoutePrefix + "{resourceId}/{fileId}/{format}")]
    public async Task<IActionResult> Open(string resourceId, string fileId, string format, CancellationToken ct)
    {
        var url = $"{LessonMediaResource.RoutePrefix}{resourceId}/{fileId}/{format}";
        if (!LessonMediaResource.TryParse(url, out var media) ||
            !Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return NotFound();

        var result = await learning.OpenResourceAsync(userId, media.ResourceId, ct);
        if (result.Access != LearningAccessResult.Allowed || result.Value is not { } resource ||
            resource.Url != media.Url || !media.Matches(resource.Type))
            return NotFound();

        var path = store.PathFor(media);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, media.ContentType!, enableRangeProcessing: true);
    }
}
