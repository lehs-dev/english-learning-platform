using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Web.Models.Practice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

// Chỉ tài khoản đáp ứng policy Teacher được truy cập.
// Service vẫn kiểm tra tài khoản và quyền sở hữu từ database.
[Authorize(Policy = AuthorizationPolicies.Teacher)]

// Không cache dữ liệu quản lý riêng của Teacher.
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class TeacherPracticeQuestionsController : Controller
{
    private readonly ITeacherPracticeQuestionService _practiceQuestionService;
    private readonly IQuestionQueryService _questionQueryService;

    /// <summary>
    /// Nhận các service thông qua Dependency Injection.
    /// Controller không trực tiếp truy vấn database.
    /// </summary>
    public TeacherPracticeQuestionsController(
        ITeacherPracticeQuestionService practiceQuestionService,
        IQuestionQueryService questionQueryService)
    {
        if (practiceQuestionService == null)
        {
            throw new ArgumentNullException(
                nameof(practiceQuestionService));
        }

        if (questionQueryService == null)
        {
            throw new ArgumentNullException(
                nameof(questionQueryService));
        }

        _practiceQuestionService = practiceQuestionService;
        _questionQueryService = questionQueryService;
    }

    /// <summary>
    /// Mở trang câu hỏi của Practice và tìm kiếm trong Question Bank.
    /// GET chỉ đọc dữ liệu, không lưu hoặc Publish Practice.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(
        Guid id,
        [FromQuery] QuestionSearchRequest query,
        CancellationToken cancellationToken)
    {
        Guid actorUserId;

        // Không nhận ID Teacher từ URL hoặc form.
        // Luôn xác định Teacher bằng tài khoản đang đăng nhập.
        if (!TryGetActorUserId(out actorUserId))
        {
            return Challenge();
        }

        // ModelState phát hiện lỗi chuyển kiểu:
        // ví dụ id=abc hoặc PageNumber=abc.
        if (query == null || !ModelState.IsValid)
        {
            return BadRequest(
                "Thông tin Practice hoặc bộ lọc không hợp lệ.");
        }

        if (id == Guid.Empty)
        {
            return NotFound();
        }

        // Kiểm tra trước khi gọi service tìm kiếm.
        // Service vẫn có kiểm tra riêng khi được gọi từ nơi khác.
        if (query.PageNumber < 1)
        {
            return BadRequest(
                "Số trang phải từ 1 trở lên.");
        }

        if (query.PageSize < 1 || query.PageSize > 100)
        {
            return BadRequest(
                "Số câu mỗi trang phải từ 1 đến 100.");
        }

        if (query.PrimarySkill.HasValue)
        {
            bool validSkill = Enum.IsDefined(
                typeof(EnglishSkill),
                query.PrimarySkill.Value);

            if (!validSkill)
            {
                return BadRequest(
                    "Kỹ năng không hợp lệ.");
            }
        }

        if (query.Status.HasValue)
        {
            bool validStatus = Enum.IsDefined(
                typeof(QuestionStatus),
                query.Status.Value);

            if (!validStatus)
            {
                return BadRequest(
                    "Trạng thái câu hỏi không hợp lệ.");
            }
        }

        // Kiểm tra quyền với Practice trước khi tải Question Bank.
        // Service kiểm tra Teacher đang hoạt động, owner của Practice,
        // owner của Course chứa parent và các câu hỏi đã liên kết.
        var editorResult =
            await _practiceQuestionService.GetEditorAsync(
                actorUserId,
                id,
                cancellationToken);

        if (editorResult.Access != LearningAccessResult.Allowed)
        {
            return Denied(editorResult.Access);
        }

        if (editorResult.Value == null)
        {
            return NotFound();
        }

        // Tái sử dụng chức năng tìm kiếm đã có.
        // Service tự giới hạn câu hỏi theo Teacher hiện tại.
        QuestionSearchResult? searchResult =
            await _questionQueryService.SearchAsync(
                actorUserId,
                query,
                cancellationToken);

        // Service này trả null khi tài khoản không có quyền.
        // Không nhầm null với danh sách hợp lệ nhưng không có kết quả.
        if (searchResult == null)
        {
            return Forbid();
        }

        var model = new TeacherPracticeQuestionsViewModel(
            editorResult.Value,
            query,
            searchResult);

        // Đánh dấu mục Practice trên menu Teacher.
        ViewData["ActiveSection"] = "practices";

        // MVC sẽ tìm file:
        // Views/TeacherPracticeQuestions/Index.cshtml.
        return View(model);
    }

    /// <summary>
    /// Nhận toàn bộ danh sách câu hỏi theo thứ tự trên giao diện.
    /// Trả JSON để lưu lỗi không làm mất lựa chọn đang sửa.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        Guid id,
        [FromForm] UpdatePracticeQuestionsRequest input,
        CancellationToken cancellationToken)
    {
        Guid actorUserId;

        if (!TryGetActorUserId(out actorUserId))
        {
            return Unauthorized(new { message = "Bạn cần đăng nhập lại." });
        }

        if (input == null || !ModelState.IsValid)
        {
            return BadRequest(new
            {
                message = "Danh sách câu hỏi gửi lên không hợp lệ."
            });
        }

        var result = await _practiceQuestionService.UpdateQuestionsAsync(
            actorUserId, id, input, cancellationToken);

        if (result.Access == LearningAccessResult.Forbidden)
        {
            return StatusCode(403, new
            {
                message = "Bạn không có quyền thực hiện thao tác này."
            });
        }

        if (result.Access == LearningAccessResult.NotFound)
        {
            return NotFound(new { message = "Không tìm thấy Practice." });
        }

        if (!result.Success)
        {
            return BadRequest(new { message = FirstError(result) });
        }

        return Json(new { success = true });
    }

    /// <summary>
    /// Publish chỉ dùng danh sách câu hỏi đã lưu trên server.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(
        Guid id,
        Guid? lessonId,
        Guid? moduleId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken)
    {
        Guid actorUserId;

        if (!TryGetActorUserId(out actorUserId))
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return BadRequest("Thông tin Publish không hợp lệ.");
        }

        var result = await _practiceQuestionService.PublishAsync(
            actorUserId, id, lessonId, moduleId,
            expectedUpdatedAtUtc, cancellationToken);

        return FinishPublication(id, result,
            "Đã Publish Practice. Student có quyền học sẽ thấy bài "
            + "khi Course và Lesson/Module đang khả dụng.");
    }

    /// <summary>
    /// Rút bài mà không xóa kết quả hoặc mở khóa định nghĩa.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unpublish(
        Guid id,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken)
    {
        Guid actorUserId;

        if (!TryGetActorUserId(out actorUserId))
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return BadRequest();
        }

        var result = await _practiceQuestionService.UnpublishAsync(
            actorUserId, id, expectedUpdatedAtUtc, cancellationToken);

        return FinishPublication(id, result,
            "Đã Unpublish Practice. Kết quả cũ được giữ nguyên.");
    }

    /// <summary>
    /// Dùng chung cách hiển thị thông báo sau Publish/Unpublish.
    /// </summary>
    private IActionResult FinishPublication(
        Guid id, WriteOutcome result, string successMessage)
    {
        if (result.Access != LearningAccessResult.Allowed)
        {
            return Denied(result.Access);
        }

        if (result.Success)
        {
            TempData["Success"] = successMessage;
        }
        else
        {
            TempData["Error"] = FirstError(result);
        }

        return RedirectToAction(nameof(Index), new { id = id });
    }

    /// <summary>
    /// Chỉ lấy lỗi đầu tiên, không nhét danh sách lỗi dài vào TempData.
    /// </summary>
    private static string FirstError(WriteOutcome result)
    {
        foreach (var error in result.Errors)
        {
            if (error.Value.Length > 0)
            {
                return error.Value[0];
            }
        }

        return "Không thể thực hiện thao tác. Vui lòng thử lại.";
    }

    /// <summary>
    /// Đọc ID người dùng từ thông tin đăng nhập.
    /// Trả false nếu thiếu ID hoặc ID không hợp lệ.
    /// </summary>
    private bool TryGetActorUserId(out Guid actorUserId)
    {
        string? userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        return Guid.TryParse(userIdValue, out actorUserId)
            && actorUserId != Guid.Empty;
    }

    /// <summary>
    /// Chuyển kết quả kiểm tra quyền của service sang HTTP.
    /// Không tiết lộ Practice không tồn tại hay thuộc Teacher khác.
    /// </summary>
    private IActionResult Denied(LearningAccessResult access)
    {
        if (access == LearningAccessResult.NotFound)
        {
            return NotFound();
        }

        return Forbid();
    }
}
