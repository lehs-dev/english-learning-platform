using System.Security.Claims;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Web.Models.Assessment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Teacher)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherAssessmentQuestionsController : Controller
{
    private readonly ITeacherAssessmentQuestionService _service;
    private readonly IQuestionQueryService _questions;

    public TeacherAssessmentQuestionsController(
        ITeacherAssessmentQuestionService service,
        IQuestionQueryService questions)
    {
        if (service == null)
        {
            throw new ArgumentNullException(nameof(service));
        }

        if (questions == null)
        {
            throw new ArgumentNullException(nameof(questions));
        }

        _service = service;
        _questions = questions;
    }

    /// <summary>
    /// Mở đề đã lưu và tìm câu hỏi trong Question Bank.
    /// GET không thay đổi database.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(
        Guid id,
        [FromQuery] QuestionSearchRequest query,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out Guid actorUserId))
        {
            return Challenge();
        }

        if (query == null || !ModelState.IsValid
            || query.PageNumber < 1
            || query.PageSize < 1 || query.PageSize > 100)
        {
            return BadRequest("Thông tin tìm kiếm không hợp lệ.");
        }

        if (query.PrimarySkill.HasValue
            && !Enum.IsDefined(
                typeof(EnglishSkill), query.PrimarySkill.Value))
        {
            return BadRequest("Kỹ năng không hợp lệ.");
        }

        if (query.Status.HasValue
            && !Enum.IsDefined(
                typeof(QuestionStatus), query.Status.Value))
        {
            return BadRequest("Trạng thái không hợp lệ.");
        }

        var editor = await _service.GetEditorAsync(
            actorUserId, id, cancellationToken);

        if (editor.Access != LearningAccessResult.Allowed)
        {
            return Denied(editor.Access);
        }

        if (editor.Value == null)
        {
            return NotFound();
        }

        // Với Skill Assessment, chỉ tìm câu khớp TargetSkill.
        // Khi lưu, service vẫn kiểm tra lại, không tin bộ lọc này.
        if (editor.Value.AssessmentType
            == AssessmentType.SkillAssessment)
        {
            query.PrimarySkill = editor.Value.TargetSkill;
        }

        query.Level = QuestionLevels.Normalize(query.Level);

        var result = await _questions.SearchAsync(
            actorUserId, query, cancellationToken);

        if (result == null)
        {
            return Forbid();
        }

        return View(new TeacherAssessmentQuestionsViewModel
        {
            Editor = editor.Value,
            Query = query,
            Result = result
        });
    }

    /// <summary>
    /// Nhận JSON để lưu danh sách; lỗi không làm mất lựa chọn trên trang.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        Guid id,
        [FromBody] UpdateAssessmentQuestionsRequest input,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out Guid actorUserId))
        {
            return Unauthorized(new { message = "Bạn cần đăng nhập lại." });
        }

        if (input == null || !ModelState.IsValid)
        {
            return BadRequest(new { message = "Dữ liệu gửi lên không hợp lệ." });
        }

        var result = await _service.SaveAsync(
            actorUserId, id, input, cancellationToken);

        if (result.Access == LearningAccessResult.NotFound)
        {
            return NotFound(new { message = "Không tìm thấy bài kiểm tra." });
        }

        if (result.Access != LearningAccessResult.Allowed)
        {
            return StatusCode(403, new
            {
                message = "Bạn không có quyền chỉnh sửa đề này."
            });
        }

        if (!result.Success)
        {
            foreach (var error in result.Errors)
            {
                if (error.Value.Length > 0)
                {
                    return BadRequest(new { message = error.Value[0] });
                }
            }

            return BadRequest(new { message = "Chưa thể lưu câu hỏi." });
        }

        return Json(new { success = true });
    }

    private bool TryGetActorUserId(out Guid actorUserId)
    {
        return Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out actorUserId)
            && actorUserId != Guid.Empty;
    }

    private IActionResult Denied(LearningAccessResult access)
    {
        if (access == LearningAccessResult.NotFound)
        {
            return NotFound();
        }

        return Forbid();
    }
}
