using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Web.Models.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Teacher)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherQuestionBankController : Controller
{
    private readonly IQuestionQueryService _questionQueryService;
    private readonly IQuestionCreationService _questionCreationService;
    private readonly IQuestionAuthoringService _questionAuthoringService;

    public TeacherQuestionBankController(
        IQuestionQueryService questionQueryService,
        IQuestionCreationService questionCreationService,
        IQuestionAuthoringService questionAuthoringService)
    {
        _questionQueryService = questionQueryService ?? throw new ArgumentNullException(nameof(questionQueryService));
        _questionCreationService = questionCreationService ?? throw new ArgumentNullException(nameof(questionCreationService));
        _questionAuthoringService = questionAuthoringService ?? throw new ArgumentNullException(nameof(questionAuthoringService));
    }

    private Guid? CurrentUserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    // Nhận Get để xem chi tiết 1 câu hỏi.
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        // Lấy ID từ tài khoản đang đăng nhập.
        // Không nhận actorUserId từ URL vì người dùng có thể sửa URL.
        string? userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid actorUserId;
        if (!Guid.TryParse(userIdValue, out actorUserId))
        {   // Không xác định được tài khoản: yêu cầu đăng nhập lại.
            return Challenge();
        }
        // ModelState phát hiện trường hợp ID không chuyển được thành Guid
        if (!ModelState.IsValid || id == Guid.Empty)
        {
            return NotFound(ModelState);
        }
        ViewData["ActiveSection"] = "question-bank";
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
        ViewData["ActiveSection"] = "question-bank";

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

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue)
        {
            return Challenge();
        }

        ViewData["ActiveSection"] = "question-bank";
        var stimuli = await _questionAuthoringService.GetStimuliAsync(CurrentUserId.Value, cancellationToken);
        var model = new QuestionFormViewModel
        {
            Stimuli = stimuli.Select(s => new StimulusOptionViewModel { Id = s.Id, Type = s.Type, Title = s.Title }).ToList()
        };

        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        QuestionFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue)
        {
            return Challenge();
        }

        ViewData["ActiveSection"] = "question-bank";
        model.IsEdit = false;

        var request = ToCreateRequest(model);
        var result = await _questionCreationService.CreateAsync(CurrentUserId.Value, request, cancellationToken);

        if (result.Succeeded)
        {
            TempData["Success"] = "Đã tạo câu hỏi trong Question Bank.";
            return RedirectToAction(nameof(Index));
        }

        if (result.Errors.Any(e => e.Code == "forbidden"))
        {
            return Forbid();
        }

        AddErrorsToModelState(result.Errors);
        var stimuli = await _questionAuthoringService.GetStimuliAsync(CurrentUserId.Value, cancellationToken);
        model.Stimuli = stimuli.Select(s => new StimulusOptionViewModel { Id = s.Id, Type = s.Type, Title = s.Title }).ToList();
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue)
        {
            return Challenge();
        }

        if (id == Guid.Empty)
        {
            return NotFound();
        }

        ViewData["ActiveSection"] = "question-bank";
        var editor = await _questionAuthoringService.GetEditorAsync(CurrentUserId.Value, id, cancellationToken);
        if (editor is null)
        {
            return NotFound();
        }

        var stimuli = await _questionAuthoringService.GetStimuliAsync(CurrentUserId.Value, cancellationToken);
        var model = FromEditor(editor, stimuli);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        QuestionFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue)
        {
            return Challenge();
        }

        if (id == Guid.Empty)
        {
            return NotFound();
        }

        ViewData["ActiveSection"] = "question-bank";
        model.Id = id;
        model.IsEdit = true;

        var editor = await _questionAuthoringService.GetEditorAsync(CurrentUserId.Value, id, cancellationToken);
        if (editor is null)
        {
            return NotFound();
        }

        // If scoring content is locked and fields were omitted from POST, restore them from persisted editor
        if (editor.ScoringContentLocked)
        {
            model.ScoringContentLocked = true;
            model.LockReason = editor.LockReason;
            if (string.IsNullOrWhiteSpace(model.Content))
            {
                model.Content = editor.Content;
            }
            if (!model.PrimarySkill.HasValue)
            {
                model.PrimarySkill = editor.PrimarySkill;
            }
            if (!model.StimulusId.HasValue && editor.StimulusId.HasValue)
            {
                model.StimulusId = editor.StimulusId;
            }
            if (model.Options == null || model.Options.Count == 0)
            {
                model.Options = editor.Options.Select(o => new QuestionOptionFormModel { Content = o.Content }).ToList();
                model.CorrectOptionIndex = editor.Options.Select((o, i) => (Option: o, Index: i)).FirstOrDefault(x => x.Option.IsCorrect).Index;
            }
        }

        var request = ToCreateRequest(model);
        var result = await _questionAuthoringService.UpdateAsync(CurrentUserId.Value, id, request, cancellationToken);

        if (result.Succeeded)
        {
            TempData["Success"] = "Đã lưu câu hỏi.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (result.Errors.Any(e => e.Code == "not_found"))
        {
            return NotFound();
        }

        if (result.Errors.Any(e => e.Code == "forbidden"))
        {
            return Forbid();
        }

        AddErrorsToModelState(result.Errors);
        var stimuli = await _questionAuthoringService.GetStimuliAsync(CurrentUserId.Value, cancellationToken);
        model.Stimuli = stimuli.Select(s => new StimulusOptionViewModel { Id = s.Id, Type = s.Type, Title = s.Title }).ToList();
        model.ScoringContentLocked = editor.ScoringContentLocked;
        model.LockReason = editor.LockReason;
        model.Status = editor.Status;
        return View(model);
    }

    private static CreateQuestionRequest ToCreateRequest(QuestionFormViewModel model)
    {
        var options = new List<QuestionOptionRequest>();
        if (model.Options != null)
        {
            for (var i = 0; i < model.Options.Count; i++)
            {
                var content = model.Options[i]?.Content ?? string.Empty;
                var isCorrect = model.CorrectOptionIndex.HasValue && model.CorrectOptionIndex.Value == i;
                options.Add(new QuestionOptionRequest(content, isCorrect));
            }
        }

        return new CreateQuestionRequest
        {
            Content = model.Content?.Trim() ?? string.Empty,
            PrimarySkill = model.PrimarySkill,
            Level = string.IsNullOrWhiteSpace(model.Level) ? null : model.Level.Trim(),
            Difficulty = string.IsNullOrWhiteSpace(model.Difficulty) ? null : model.Difficulty.Trim(),
            Explanation = string.IsNullOrWhiteSpace(model.Explanation) ? null : model.Explanation.Trim(),
            StimulusId = model.StimulusId,
            Options = options
        };
    }

    private static QuestionFormViewModel FromEditor(
        QuestionEditor editor,
        IReadOnlyList<QuestionStimulusOption> stimuli)
    {
        var options = editor.Options.OrderBy(o => o.OrderIndex).ToList();
        var correctIndex = options.FindIndex(o => o.IsCorrect);

        return new QuestionFormViewModel
        {
            Id = editor.Id,
            Content = editor.Content,
            PrimarySkill = editor.PrimarySkill,
            Level = editor.Level,
            Difficulty = editor.Difficulty,
            Explanation = editor.Explanation,
            StimulusId = editor.StimulusId,
            Status = editor.Status,
            IsEdit = true,
            ScoringContentLocked = editor.ScoringContentLocked,
            LockReason = editor.LockReason,
            Options = options.Select(o => new QuestionOptionFormModel { Content = o.Content }).ToList(),
            CorrectOptionIndex = correctIndex >= 0 ? correctIndex : null,
            Stimuli = stimuli.Select(s => new StimulusOptionViewModel { Id = s.Id, Type = s.Type, Title = s.Title }).ToList()
        };
    }

    private void AddErrorsToModelState(IReadOnlyList<QuestionError> errors)
    {
        foreach (var error in errors)
        {
            var key = error.Field switch
            {
                nameof(CreateQuestionRequest.Content) => "Content",
                nameof(CreateQuestionRequest.PrimarySkill) => "PrimarySkill",
                nameof(CreateQuestionRequest.Level) => "Level",
                nameof(CreateQuestionRequest.Difficulty) => "Difficulty",
                nameof(CreateQuestionRequest.Explanation) => "Explanation",
                nameof(CreateQuestionRequest.StimulusId) => "StimulusId",
                nameof(CreateQuestionRequest.Options) => "Options",
                _ when error.Field.StartsWith("Options[", StringComparison.Ordinal) => error.Field,
                _ => string.Empty
            };

            ModelState.AddModelError(key, error.Message);
        }
    }
}
