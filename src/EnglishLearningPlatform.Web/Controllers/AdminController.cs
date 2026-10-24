using System.Security.Claims;
using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EnglishLearningPlatform.Web.Models.Admin;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminController(IAdminQueryService queries, IAdminManagementService management,
    IAdminDashboardQueryService dashboard, IAdminOverviewDetailQueryService details,
    IAdminRevenueQueryService revenue) : Controller
{
    private Guid? UserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private bool InvalidNonDateFilter => ModelState.Any(entry =>
        !entry.Key.Equals("fromDate", StringComparison.OrdinalIgnoreCase) &&
        !entry.Key.Equals("toDate", StringComparison.OrdinalIgnoreCase) && entry.Value!.Errors.Count > 0);

    private AdminDateRange? PrepareDates(DateOnly? fromDate, DateOnly? toDate)
    {
        ViewData["FromDate"] = fromDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        ViewData["ToDate"] = toDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var dates = new AdminDateRange(fromDate, toDate);
        if (!ModelState.IsValid || !dates.IsValid)
        {
            ModelState.AddModelError("", "Ngày không hợp lệ. Từ ngày phải nhỏ hơn hoặc bằng đến ngày (phạm vi 02/01/0001–30/12/9999).");
            return null;
        }
        return dates;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!UserId.HasValue) return Challenge();
        ViewData["ActiveSection"] = "admin";
        var result = await dashboard.GetAsync(UserId.Value, ct);
        if (result is null) return Forbid();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var month = new AdminDateRange(new(today.Year, today.Month, 1), today);
        var monthRevenue = await revenue.GetAsync(UserId.Value, month, ct: ct);
        return monthRevenue is null ? Forbid() : View(new AdminDashboardViewModel(result, new(monthRevenue, "day", 1), today));
    }

    [HttpGet]
    public async Task<IActionResult> Revenue(DateOnly? fromDate = null, DateOnly? toDate = null,
        string? currency = null, string? groupBy = "day", int page = 1, CancellationToken ct = default, string? period = null)
    {
        if (!UserId.HasValue) return Challenge();
        groupBy ??= "day";
        period ??= fromDate.HasValue || toDate.HasValue ? "custom" : "month";
        currency = string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim().ToUpperInvariant();
        if (InvalidNonDateFilter || groupBy is not ("day" or "month") ||
            currency != "VND" || period is not ("all" or "custom" or "week" or "month" or "year"))
            return BadRequest();
        ViewData["ActiveSection"] = "admin";
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        ViewData["VietnamToday"] = today.ToString("yyyy-MM-dd");
        var range = AdminRevenueRanges.Resolve(period, today, fromDate, toDate);
        fromDate = range.FromDate;
        toDate = range.ToDate;
        var dates = PrepareDates(fromDate, toDate);
        if (!ModelState.IsValid)
            return View(new AdminRevenueViewModel(new(new(fromDate, toDate), currency, [], [], []), groupBy, 1, period));
        var result = await revenue.GetAsync(UserId.Value, dates, currency, ct);
        return result is null ? Forbid() : View(new AdminRevenueViewModel(result, groupBy, page, period));
    }

    [HttpGet]
    public async Task<IActionResult> Courses(string? search, CourseStatus? status, int page = 1, CancellationToken ct = default,
        DateOnly? fromDate = null, DateOnly? toDate = null, AdminCourseSort sort = AdminCourseSort.Newest)
    {
        if (!UserId.HasValue) return Challenge();
        if (InvalidNonDateFilter || status.HasValue && !Enum.IsDefined(status.Value) || !Enum.IsDefined(sort)) return BadRequest();
        ViewData["ActiveSection"] = "admin";
        ViewData["Status"] = status;
        ViewData["Sort"] = sort;
        var dates = PrepareDates(fromDate, toDate);
        var result = await details.CoursesAsync(UserId.Value, search, status, page, ct, dates, sort);
        if (result is not null && !ModelState.IsValid) result = result with { Items = [], Total = 0, Page = 1, Pages = 1 };
        return result is null ? Forbid() : View(result);
    }

    [HttpGet]
    public async Task<IActionResult> Enrollments(string? search, bool? completed, int page = 1,
        CancellationToken ct = default, Guid? courseId = null, DateOnly? fromDate = null, DateOnly? toDate = null)
    {
        if (!UserId.HasValue) return Challenge();
        if (InvalidNonDateFilter || courseId == Guid.Empty) return BadRequest();
        ViewData["ActiveSection"] = "admin";
        ViewData["Completed"] = completed;
        ViewData["CourseId"] = courseId;
        var dates = PrepareDates(fromDate, toDate);
        var result = await details.EnrollmentsAsync(UserId.Value, search, completed, page, ct, courseId, dates);
        if (result is not null && !ModelState.IsValid) result = result with { Items = [], Total = 0, Page = 1, Pages = 1 };
        return result is null ? Forbid() : View(result);
    }

    [HttpGet]
    public IActionResult CreateTeacher() { ViewData["ActiveSection"] = "admin"; return View(new CreateTeacherInput()); }

    [HttpGet]
    public IActionResult CreateUser() { ViewData["ActiveSection"] = "admin"; return View(new CreateAdminUserInput()); }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(CreateAdminUserInput input, CancellationToken ct)
    {
        if (!UserId.HasValue) return Challenge();
        ViewData["ActiveSection"] = "admin";
        if (ModelState.IsValid)
        {
            var result = await management.CreateUserAsync(UserId.Value, input.FullName, input.Email, input.Password, input.Role, ct);
            if (result.Success) { TempData["AdminSuccess"] = "Đã tạo người dùng."; return RedirectToAction(nameof(Users)); }
            ModelState.AddModelError("", result.Error!);
        }
        input.Password = "";
        if (ModelState.TryGetValue(nameof(input.Password), out var passwordState))
        {
            passwordState.RawValue = null;
            passwordState.AttemptedValue = null;
        }
        return View(input);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTeacher(CreateTeacherInput input, CancellationToken ct)
    {
        if (!UserId.HasValue) return Challenge();
        ViewData["ActiveSection"] = "admin";
        if (ModelState.IsValid)
        {
            var result = await management.CreateTeacherAsync(UserId.Value, input.FullName, input.Email, input.Password, ct);
            if (result.Success) { TempData["AdminSuccess"] = "Đã tạo tài khoản Teacher."; return RedirectToAction(nameof(Users)); }
            ModelState.AddModelError("", result.Error!);
        }
        // Do not echo a submitted password into HTML after validation failure.
        input.Password = "";
        return View(input);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateUser(UpdateAdminUserInput input, CancellationToken ct)
    {
        if (!UserId.HasValue) return Challenge();
        if (!ModelState.IsValid || input.UserId == Guid.Empty) return BadRequest();
        var result = await management.UpdateUserAsync(UserId.Value, input.UserId, input.Role, input.Status, ct);
        TempData[result.Success ? "AdminSuccess" : "AdminError"] = result.Success ? "Đã cập nhật tài khoản." : result.Error;
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> Audit(string? search, int page = 1, CancellationToken ct = default, DateOnly? fromDate = null, DateOnly? toDate = null)
    {
        if (!UserId.HasValue) return Challenge();
        ViewData["ActiveSection"] = "admin";
        var dates = PrepareDates(fromDate, toDate);
        var result = await queries.AuditAsync(UserId.Value, search, page, ct, dates);
        return result is null ? Forbid() : View(ModelState.IsValid ? result : result with { Items = [], Total = 0, Page = 1, Pages = 1 });
    }

    [HttpGet]
    public async Task<IActionResult> Transactions(string? search, PaymentStatus? status, int page = 1, CancellationToken ct = default, DateOnly? fromDate = null, DateOnly? toDate = null)
    {
        if (!UserId.HasValue) return Challenge();
        if (InvalidNonDateFilter || status.HasValue && !Enum.IsDefined(status.Value)) return BadRequest();
        ViewData["ActiveSection"] = "admin";
        var dates = PrepareDates(fromDate, toDate);
        ViewData["Status"] = status;
        var result = await queries.TransactionsAsync(UserId.Value, search, status, page, ct, dates);
        return result is null ? Forbid() : View(ModelState.IsValid ? result : result with { Items = [], Total = 0, Page = 1, Pages = 1 });
    }

    [HttpGet]
    public async Task<IActionResult> Users(string? search, AccountStatus? status, int page = 1, CancellationToken ct = default, string? role = null, DateOnly? fromDate = null, DateOnly? toDate = null)
    {
        if (!UserId.HasValue) return Challenge();
        if (InvalidNonDateFilter || status.HasValue && !Enum.IsDefined(status.Value)) return BadRequest();
        ViewData["ActiveSection"] = "admin";
        var dates = PrepareDates(fromDate, toDate);
        ViewData["Status"] = status;
        if (!string.IsNullOrEmpty(role) && !new[] { "Student", "Teacher", "Admin" }.Contains(role)) return BadRequest();
        ViewData["Role"] = role;
        var result = await queries.UsersAsync(UserId.Value, search, status, page, ct, role, dates);
        return result is null ? Forbid() : View(ModelState.IsValid ? result : result with { Items = [], Total = 0, Page = 1, Pages = 1 });
    }
}
