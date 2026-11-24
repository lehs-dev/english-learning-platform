using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Flashcards;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.LearningPath;
using EnglishLearningPlatform.Application.Gamification;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Web.Models.Courses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Roles = AppRoles.Student + "," + AppRoles.Teacher, Policy = AuthorizationPolicies.SingleActiveRole)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LearningController(IStudentLearningCourseService courses, IAssessmentQueryService assessments,
    IStudentLearningService learning, ICourseAuthoringService authoring, ICourseCatalogService catalog,
    IGamificationService? gamification = null, IFlashcardService? flashcards = null,
    ILearningPathService? learningPath = null) : Controller
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private IActionResult Denied(LearningAccessResult access) => access == LearningAccessResult.NotFound ? NotFound() : Forbid();
    [HttpGet, Authorize(Policy = AuthorizationPolicies.Student)]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var result = await learning.DashboardAsync(UserId, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        if (gamification is not null)
            ViewData["Gamification"] = (await gamification.GetSummaryAsync(UserId, ct)).Value;
        if (flashcards is not null)
        {
            var deckOutcome = await flashcards.GetDecksAsync(UserId, ct);
            if (deckOutcome.Access == LearningAccessResult.Allowed && deckOutcome.Value is { } decks)
            {
                ViewData["DueDecks"] = decks.Where(d => d.DueCount > 0).ToList();
            }
        }
        if (learningPath is not null)
        {
            var planOutcome = await learningPath.GetActivePlanAsync(UserId, ct);
            if (planOutcome.Access == LearningAccessResult.Allowed && planOutcome.Value is { } lpPage)
            {
                ViewData["LearningPathPage"] = lpPage;
            }
        }
        if (result.Value!.Courses.Count == 0)
            ViewData["SuggestedCourses"] = (await catalog.CatalogAsync(new CatalogQuery(), ct)).Courses.Take(3).ToArray();
        return View(result.Value);
    }
    [HttpGet, Authorize(Policy = AuthorizationPolicies.Student)]
    public async Task<IActionResult> Continue(Guid id, CancellationToken ct)
    {
        // Resolve the current target each time, so a hidden lesson cannot leave a stale resume link.
        var result = await courses.OpenCourseAsync(UserId, id, false, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        var progress = result.Value!.Progress;
        // Khi đã xong toàn bộ lesson (hoặc không còn lesson), Course là nơi
        // hiển thị bài thi cuối khóa thay vì quay lại bài đầu tiên.
        return progress is { TotalLessons: > 0, ContinueLessonId: Guid lessonId } &&
               progress.CompletedLessons < progress.TotalLessons
            ? RedirectToAction(nameof(Lesson), new { id = lessonId })
            : RedirectToAction(nameof(Course), new { id });
    }
    [HttpGet]
    public async Task<IActionResult> Index(string? status, string? search, CatalogQuery query, CancellationToken ct)
    {
        if (User.IsInRole(AppRoles.Teacher)) return RedirectToAction("Index", "TeacherCourses");
        var result = await courses.MyCoursesAsync(UserId, false, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        if (!ModelState.IsValid) ViewData["QueryNotice"] = "Một số bộ lọc không hợp lệ đã được bỏ qua.";
        var enrolled = result.Value!;
        var effectiveSearch = !string.IsNullOrWhiteSpace(search) ? search : query.Search;
        var catalogPage = await catalog.CatalogAsync(query, ct, enrolled.Select(course => course.Id).ToArray());
        ModelState.Clear();
        return View(new StudentCoursesPage(enrolled, catalogPage, status, effectiveSearch));
    }
    [HttpGet]
    public async Task<IActionResult> Course(Guid id, string? tab, CancellationToken ct)
    {
        var result = await courses.OpenCourseAsync(UserId, id, false, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        ViewData["ActiveTab"] = tab?.ToLowerInvariant() switch
        {
            "practice" or "ontap" => "practice",
            "assessment" or "danhgia" => "assessment",
            _ => "lessons"
        };
        if (User.IsInRole(AppRoles.Student) && result.Value!.FinalAssessmentId is Guid assessmentId)
            ViewData["FinalAssessment"] = await assessments.GetAssessmentOverviewAsync(UserId, assessmentId, ct);
        if (User.IsInRole(AppRoles.Student))
        {
            var activities = await learning.ActivitiesAsync(UserId, id, ct);
            if (activities.Access != LearningAccessResult.Allowed) return Denied(activities.Access);
            ViewData["Activities"] = activities.Value;
            if (flashcards is not null)
            {
                var deckOutcome = await flashcards.GetDecksAsync(UserId, ct);
                if (deckOutcome.Access == LearningAccessResult.Allowed && deckOutcome.Value is { } decks)
                {
                    ViewData["CourseFlashcardDecks"] = decks.Where(d => d.CourseId == id).ToList();
                }
            }
        }
        return View(result.Value);
    }
    [HttpGet, Authorize(Policy = AuthorizationPolicies.Student)]
    public async Task<IActionResult> Progress(string? tab, CancellationToken ct)
    {
        var enrolledResult = await courses.MyCoursesAsync(UserId, false, ct);
        if (enrolledResult.Access != LearningAccessResult.Allowed) return Denied(enrolledResult.Access);
        var representative = await learning.GetRepresentativeResultsAsync(UserId, ct);
        var assessmentsResult = await learning.AssessmentsAsync(UserId, ct);
        var dashboardResult = await learning.DashboardAsync(UserId, ct);
        GamificationSummary? gamificationSummary = null;
        if (gamification is not null)
            gamificationSummary = (await gamification.GetSummaryAsync(UserId, ct)).Value;

        var activeTab = tab?.ToLowerInvariant() switch
        {
            "assessments" or "exams" => "assessments",
            "achievements" or "gamification" => "achievements",
            _ => "overview"
        };

        var model = new StudentProgressPage(
            enrolledResult.Value ?? [],
            representative.Value ?? [],
            assessmentsResult.Value ?? [],
            gamificationSummary,
            dashboardResult.Value?.Recommendations ?? [],
            activeTab
        );
        return View(model);
    }
    [HttpGet]
    public async Task<IActionResult> Module(Guid id, CancellationToken ct)
    {
        var result = await courses.OpenModuleAsync(UserId, id, ct);
        if (result.Access == LearningAccessResult.Allowed && !result.Value!.IsOwner)
        {
            var activities = await learning.ActivitiesAsync(UserId, result.Value.CourseId, ct);
            if (activities.Access != LearningAccessResult.Allowed) return Denied(activities.Access);
            var lessons = result.Value.Lessons.Select(l => l.Id).ToHashSet();
            ViewData["Activities"] = new CourseActivities(activities.Value!.Practices.Where(p => p.ModuleId == id ||
                (p.LessonId.HasValue && lessons.Contains(p.LessonId.Value))).ToList(), []);
        }
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Denied(result.Access);
    }
    [HttpGet]
    public async Task<IActionResult> Lesson(Guid id, CancellationToken ct)
    {
        var result = await courses.OpenLessonAsync(UserId, id, ct);
        if (result.Access == LearningAccessResult.Allowed && User.IsInRole(AppRoles.Student))
        {
            var activities = await learning.ActivitiesAsync(UserId, result.Value!.CourseId, ct);
            if (activities.Access != LearningAccessResult.Allowed) return Denied(activities.Access);
            ViewData["Activities"] = new CourseActivities(activities.Value!.Practices.Where(p => p.LessonId == id ||
                p.ModuleId == result.Value.ModuleId).ToList(), []);
            if (result.Value.FinalAssessmentId is Guid finalAssessmentId)
                ViewData["FinalAssessment"] = await assessments.GetAssessmentOverviewAsync(UserId, finalAssessmentId, ct);
        }
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Denied(result.Access);
    }
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AuthorizationPolicies.Student)]
    public async Task<IActionResult> Completion(Guid id, bool? completed, CancellationToken ct, bool goNext = false)
    {
        if (!ModelState.IsValid || !completed.HasValue) return BadRequest();
        var result = await courses.SetLessonCompletedAsync(UserId, id, completed.Value, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        TempData["Success"] = completed.Value ? "Đã đánh dấu bài học hoàn thành." : "Đã đánh dấu bài học chưa hoàn thành.";
        if (completed.Value && goNext)
        {
            var lesson = await courses.OpenLessonAsync(UserId, id, ct);
            if (lesson.Value?.Next is { } next) return RedirectToAction(nameof(Lesson), new { id = next.Id });
        }
        return RedirectToAction(nameof(Lesson), new { id });
    }
    [HttpGet]
    public async Task<IActionResult> Resource(Guid id, CancellationToken ct)
    {
        var result = await courses.OpenResourceAsync(UserId, id, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Denied(result.Access);
    }
    [Authorize(Policy = AuthorizationPolicies.Teacher), HttpGet]
    public async Task<IActionResult> ManageCourse(Guid id, CancellationToken ct)
    {
        var result = await courses.OpenCourseAsync(UserId, id, true, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        var resources = await authoring.AuthoringResourcesAsync(UserId, id, ct);
        return resources.Access == LearningAccessResult.Allowed
            ? View("~/Views/TeacherCourses/Manage.cshtml", new EnglishLearningPlatform.Web.Models.Courses.AuthoringModel(result.Value!, resources.Value!))
            : Denied(resources.Access);
    }
}
