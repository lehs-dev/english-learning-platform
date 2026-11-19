using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.LearningPath;
using EnglishLearningPlatform.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Student)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LearningPathController(ILearningPathService paths) : Controller
{
    private Guid StudentId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var result = await paths.GetActivePlanAsync(StudentId, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Forbid();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(int durationDays = LearningPlan.DefaultDurationDays, CancellationToken ct = default)
    {
        if (!ModelState.IsValid || !LearningPlan.IsSupportedDuration(durationDays)) return BadRequest();
        var result = await paths.GeneratePlanAsync(StudentId, durationDays, ct);
        if (result.Access != LearningAccessResult.Allowed) return Forbid();
        if (result.Value?.Plan is not null && result.Value.UnavailableRequestedDuration is null)
            return RedirectToAction(nameof(Index));
        return View("Index", result.Value);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Skip(Guid id, CancellationToken ct)
    {
        if (!ModelState.IsValid || id == Guid.Empty) return BadRequest();
        var access = await paths.SkipItemAsync(StudentId, id, ct);
        return access switch
        {
            LearningAccessResult.Allowed => RedirectToAction(nameof(Index)),
            LearningAccessResult.NotFound => NotFound(),
            _ => Forbid()
        };
    }
}
