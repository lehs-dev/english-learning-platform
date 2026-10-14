using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Web.Models.QuestionBank;

namespace EnglishLearningPlatform.Web.Controllers;
[Authorize(Policy = AuthorizationPolicies.Teacher)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherQuestionBankController : Controller
{
    private readonly IQuestionQueryService _questionQueryService;
    public TeacherQuestionBankController(IQuestionQueryService questionQueryService)
    {
        if (questionQueryService == null)
        {
            throw new ArgumentNullException(nameof(questionQueryService));
        }
        _questionQueryService = questionQueryService;
    }
    // Nhận Get để xem chi tiết 1 câu hỏi.
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        // Lấy ID từ tài khoản đang đăng nhập.
        // Không nhận actorUserId từ URL vì người dùng có thể sửa URL.
        string? userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid actorUserId;
        if(!Guid.TryParse(userIdValue, out actorUserId))
        {   // Không xác định được tài khoản: yêu cầu đăng nhập lại.
            return Challenge();
        }
        // ModelState phát hiện trường hợp ID không chuyển được thành Guid
        if (!ModelState.IsValid || id == Guid.Empty)
        {
            return NotFound(ModelState);
        }
        // Service kiểm tra tài khoản và quyền sở hữu trước khi trả dữ liệu.
        QuestionDetail? detail = await _questionQueryService.GetByIdAsync(actorUserId, id, cancellationToken);
        if (detail == null)
        {   // Không tiết lộ câu hỏi không tồn tại hay thuộc Teacher khác.
            return NotFound();
        }
        // MVC tìm Views/TeacherQuestionBank/Details.cshtml
        // và truyền detail vào Model của view.
        return View(detail);
    }
    // Hiển thị danh sách câu hỏi thuộc Teacher đang đăng nhập.
    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] QuestionSearchRequest query,
        CancellationToken cancellationToken)
    {
        // Lấy ID từ tài khoản đăng nhập, không lấy owner từ URL.
        string? userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        Guid actorUserId;

        if (!Guid.TryParse(userIdValue, out actorUserId)
            || actorUserId == Guid.Empty)
        {
            return Challenge();
        }

        // FromQuery yêu cầu MVC đọc các bộ lọc từ query string.
        // ModelState phát hiện lỗi chuyển kiểu, ví dụ PageNumber=abc.
        if (query == null || !ModelState.IsValid)
        {
            return BadRequest("Thông tin tìm kiếm không hợp lệ.");
        }

        // Kiểm tra trước khi gọi service.
        // Service cũng bảo vệ các điều kiện này bằng exception.
        if (query.PageNumber < 1)
        {
            return BadRequest("Số trang phải từ 1 trở lên.");
        }

        if (query.PageSize < 1 || query.PageSize > 100)
        {
            return BadRequest("Số câu mỗi trang phải từ 1 đến 100.");
        }

        // Có giá trị skill thì phải là một giá trị enum được định nghĩa.
        if (query.PrimarySkill.HasValue)
        {
            bool validSkill = Enum.IsDefined(
                typeof(EnglishSkill),
                query.PrimarySkill.Value);

            if (!validSkill)
            {
                return BadRequest("Kỹ năng không hợp lệ.");
            }
        }

        // Status là bộ lọc tùy chọn, nhưng giá trị được nhập phải hợp lệ.
        if (query.Status.HasValue)
        {
            bool validStatus = Enum.IsDefined(
                typeof(QuestionStatus),
                query.Status.Value);

            if (!validStatus)
            {
                return BadRequest("Trạng thái không hợp lệ.");
            }
        }

        // Service chịu trách nhiệm ownership, lọc và phân trang.
        QuestionSearchResult? result =
            await _questionQueryService.SearchAsync(
                actorUserId,
                query,
                cancellationToken);

        // null nghĩa là không có quyền.
        // Không nhầm với kết quả hợp lệ nhưng danh sách rỗng.
        if (result == null)
        {
            return Forbid();
        }

        // Gộp bộ lọc và kết quả vào model dành cho trang danh sách.
        QuestionBankIndexModel model =
            new QuestionBankIndexModel(query, result);

        return View(model);
    }
}
