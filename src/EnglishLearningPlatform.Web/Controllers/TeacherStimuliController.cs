using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Stimuli;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Teacher)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherStimuliController : Controller
{
    private readonly IStimulusCreationService _service;

    /// <summary>
    /// Controller chỉ điều phối HTTP; việc kiểm tra quyền và lưu EF nằm trong service.
    /// </summary>
    public TeacherStimuliController(IStimulusCreationService service)
    {
        if (service == null)
        {
            throw new ArgumentNullException(nameof(service));
        }
        _service = service;
    }

    /// <summary>
    /// GET chỉ mở form trống, không tạo ngữ liệu hoặc tải URL bên ngoài.
    /// </summary>
    [HttpGet]
    public IActionResult Create()
    {
        if (!TryGetActorUserId(out _))
        {
            return Challenge();
        }
        return View(new CreateStimulusRequest());
    }

    /// <summary>
    /// Chỉ bind bốn trường nhập liệu; owner và dấu khóa không nhận từ client.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("StimulusType,Title,ContentText,ResourceUrl")] CreateStimulusRequest input,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out Guid actorUserId))
        {
            return Challenge();
        }
        if (input == null)
        {
            return BadRequest();
        }

        // Lỗi chuyển kiểu (ví dụ StimulusType=abc) do MVC phát hiện trước service.
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var result = await _service.CreateAsync(actorUserId, input, cancellationToken);
        if (result.Access != LearningAccessResult.Allowed)
        {
            return Forbid();
        }
        if (!result.Success)
        {
            AddWriteErrors(result);
            return View(input);
        }

        // Redirect sau POST để refresh không gửi lại thao tác vừa lưu.
        // Dropdown của form Question đã có sẵn cơ chế đọc ngữ liệu cùng owner.
        TempData["Success"] = "Đã tạo ngữ liệu. Hãy chọn ngữ liệu vừa tạo trong mục Ngữ liệu đọc/nghe.";
        return RedirectToAction(nameof(TeacherQuestionBankController.Create), "TeacherQuestionBank");
    }

    /// <summary>
    /// Lấy danh tính từ claims do hệ thống đăng nhập cấp, không đọc actor ID từ form.
    /// </summary>
    private bool TryGetActorUserId(out Guid actorUserId)
    {
        string? value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out actorUserId) && actorUserId != Guid.Empty;
    }

    /// <summary>
    /// Đưa lỗi service về đúng trường của form, đồng thời giữ dữ liệu đã nhập.
    /// </summary>
    private void AddWriteErrors(WriteOutcome result)
    {
        foreach (var error in result.Errors)
        {
            foreach (string message in error.Value)
            {
                ModelState.AddModelError(error.Key, message);
            }
        }
    }
}
