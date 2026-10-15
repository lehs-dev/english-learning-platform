using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Web.Models.Practice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EnglishLearningPlatform.Web.Controllers;

// Chỉ tài khoản đáp ứng policy Teacher được vào controller.
[Authorize(Policy = AuthorizationPolicies.Teacher)]

// Không cache trang quản lý dữ liệu riêng của Teacher.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherPracticesController : Controller
{
    private readonly ICourseService _courseService;
    private readonly ITeacherPracticeService _practiceService;

    public TeacherPracticesController(
        ICourseService courseService,
        ITeacherPracticeService practiceService)
    {
        if (courseService is null)
        {
            throw new ArgumentNullException(nameof(courseService));
        }

        if (practiceService is null)
        {
            throw new ArgumentNullException(nameof(practiceService));
        }

        _courseService = courseService;
        _practiceService = practiceService;
    }

    /// <summary>
    /// Hiển thị form. GET tuyệt đối không tạo Practice.
    /// Các parent tùy chọn dùng khi mở form từ trang quản lý Course.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Create(
        Guid? courseId,
        Guid? lessonId,
        Guid? moduleId,
        CancellationToken cancellationToken)
    {
        Guid actorUserId;

        if (!TryGetActorUserId(out actorUserId))
        {
            return Challenge();
        }

        // Bắt lỗi chuyển kiểu, ví dụ courseId=abc.
        if (!ModelState.IsValid || courseId == Guid.Empty)
        {
            return BadRequest();
        }

        var input = new CreatePracticeRequest
        {
            LessonId = lessonId,
            ModuleId = moduleId
        };

        var page = await LoadCreateModelAsync(
            actorUserId,
            courseId,
            input,
            cancellationToken);

        if (page.Access != LearningAccessResult.Allowed)
        {
            return Denied(page.Access);
        }

        if (page.Value is null)
        {
            return NotFound();
        }

        // Không cho URL chọn parent ngoài Course đang mở.
        ValidateParentSelection(page.Value);

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        return View(page.Value);
    }

    /// <summary>
    /// Nhận dữ liệu form và gọi service tạo Draft.
    /// Chỉ bind request, không nhận danh sách dropdown từ client.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        Guid? courseId,
        [Bind(Prefix = "Input")] CreatePracticeRequest input,
        CancellationToken cancellationToken)
    {
        Guid actorUserId;

        if (!TryGetActorUserId(out actorUserId))
        {
            return Challenge();
        }

        if (input is null || courseId == Guid.Empty)
        {
            return BadRequest();
        }

        // Luôn lấy lại dropdown từ server.
        // Không tin CourseTitle hoặc các option do client gửi lên.
        var page = await LoadCreateModelAsync(
            actorUserId,
            courseId,
            input,
            cancellationToken);

        if (page.Access != LearningAccessResult.Allowed)
        {
            return Denied(page.Access);
        }

        if (page.Value is null)
        {
            return NotFound();
        }

        var model = page.Value;

        // Kiểm tra parent thuộc Course đang chọn trên form.
        // Service vẫn kiểm tra ownership và các rule domain riêng.
        ValidateParentSelection(model);

        if (!ModelState.IsValid)
        {
            // Giữ dữ liệu đã nhập và hiển thị lại đầy đủ dropdown.
            return View(model);
        }

        var result = await _practiceService.CreateDraftAsync(
            actorUserId,
            input,
            cancellationToken);

        if (result.Access != LearningAccessResult.Allowed)
        {
            return Denied(result.Access);
        }

        if (!result.Success)
        {
            AddWriteErrors(result);
            return View(model);
        }

        TempData["Success"] =
            "Đã tạo Practice Draft. Bài chưa Publish nên Student chưa thể làm.";

        // Chỉ quay lại Course nếu Practice thực sự có parent.
        // Chọn Course trên form không tự gắn Practice vào Course.
        if (courseId.HasValue
            && (input.LessonId.HasValue || input.ModuleId.HasValue))
        {
            return RedirectToAction(
                nameof(TeacherCoursesController.Manage),
                "TeacherCourses",
                new { id = courseId.Value });
        }

        // Draft chưa gắn parent vẫn được tạo theo TBR16.
        return RedirectToAction(
            nameof(TeacherCoursesController.Index),
            "TeacherCourses");
    }

    /// <summary>
    /// Lấy ID từ tài khoản đăng nhập, không lấy từ URL hoặc form.
    /// </summary>
    private bool TryGetActorUserId(out Guid actorUserId)
    {
        string? value = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out actorUserId)
            && actorUserId != Guid.Empty;
    }

    /// <summary>
    /// Lấy Course của Teacher và parent trong Course đã chọn.
    /// Dùng chung khi mở form và khi cần hiển thị lại lỗi POST.
    /// </summary>
    private async Task<LearningOutcome<CreatePracticeViewModel>>
        LoadCreateModelAsync(
            Guid actorUserId,
            Guid? courseId,
            CreatePracticeRequest input,
            CancellationToken cancellationToken)
    {
        var courses = await _courseService.MyCoursesAsync(
            actorUserId,
            true,
            cancellationToken);

        if (courses.Access != LearningAccessResult.Allowed)
        {
            return new LearningOutcome<CreatePracticeViewModel>(
                courses.Access);
        }

        if (courses.Value is null)
        {
            return new LearningOutcome<CreatePracticeViewModel>(
                LearningAccessResult.NotFound);
        }

        var model = new CreatePracticeViewModel
        {
            CourseId = courseId,
            Input = input
        };

        var courseOptions = new List<SelectListItem>();

        foreach (var course in courses.Value)
        {
            courseOptions.Add(new SelectListItem
            {
                Value = course.Id.ToString(),
                Text = course.Title,
                Selected = courseId.HasValue
                    && course.Id == courseId.Value
            });
        }

        model.CourseOptions = courseOptions;

        // Chưa chọn Course: vẫn cho mở form tạo Draft chưa có parent.
        if (!courseId.HasValue)
        {
            return new LearningOutcome<CreatePracticeViewModel>(
                LearningAccessResult.Allowed,
                model);
        }

        // true là mở Course để quản lý.
        // Service hiện có sẽ kiểm tra quyền sở hữu.
        var courseResult = await _courseService.OpenCourseAsync(
            actorUserId,
            courseId.Value,
            true,
            cancellationToken);

        if (courseResult.Access != LearningAccessResult.Allowed)
        {
            return new LearningOutcome<CreatePracticeViewModel>(
                courseResult.Access);
        }

        if (courseResult.Value is null)
        {
            return new LearningOutcome<CreatePracticeViewModel>(
                LearningAccessResult.NotFound);
        }

        var coursePage = courseResult.Value;

        // Không trả metadata nếu kết quả không xác nhận đúng owner.
        if (!coursePage.IsOwner
            || coursePage.Course.OwnerId != actorUserId)
        {
            return new LearningOutcome<CreatePracticeViewModel>(
                LearningAccessResult.Forbidden);
        }

        model.CourseTitle = coursePage.Course.Title;

        var moduleOptions = new List<SelectListItem>();
        var lessonOptions = new List<SelectListItem>();

        foreach (var module in coursePage.Modules)
        {
            moduleOptions.Add(new SelectListItem
            {
                Value = module.Id.ToString(),
                Text = module.Title
            });

            foreach (var lesson in module.Lessons)
            {
                lessonOptions.Add(new SelectListItem
                {
                    Value = lesson.Id.ToString(),

                    // Kèm tên Module để phân biệt các Lesson trùng tên.
                    Text = module.Title + " — " + lesson.Title
                });
            }
        }

        model.ModuleOptions = moduleOptions;
        model.LessonOptions = lessonOptions;

        return new LearningOutcome<CreatePracticeViewModel>(
            LearningAccessResult.Allowed,
            model);
    }

    /// <summary>
    /// Kiểm tra ID gửi lên có trong parent của Course đang chọn.
    /// Dropdown không phải là cơ chế bảo vệ quyền truy cập.
    /// </summary>
    private void ValidateParentSelection(
        CreatePracticeViewModel model)
    {
        var input = model.Input;

        bool hasParent =
            input.LessonId.HasValue || input.ModuleId.HasValue;

        if (hasParent && !model.CourseId.HasValue)
        {
            ModelState.AddModelError(
                nameof(CreatePracticeViewModel.CourseId),
                "Bạn phải chọn Course trước khi chọn Lesson hoặc Module.");

            return;
        }

        if (input.LessonId.HasValue)
        {
            bool validLesson = model.LessonOptions.Any(
                option => option.Value == input.LessonId.Value.ToString());

            if (!validLesson)
            {
                ModelState.AddModelError(
                    "Input.LessonId",
                    "Lesson không hợp lệ trong Course đang chọn.");
            }
        }

        if (input.ModuleId.HasValue)
        {
            bool validModule = model.ModuleOptions.Any(
                option => option.Value == input.ModuleId.Value.ToString());

            if (!validModule)
            {
                ModelState.AddModelError(
                    "Input.ModuleId",
                    "Module không hợp lệ trong Course đang chọn.");
            }
        }
    }

    /// <summary>
    /// Chuyển lỗi từ service sang lỗi hiển thị trên form.
    /// Prefix Input phải khớp với tên trường trong view.
    /// </summary>
    private void AddWriteErrors(WriteOutcome result)
    {
        foreach (var error in result.Errors)
        {
            string field = string.IsNullOrEmpty(error.Key)
                ? string.Empty
                : "Input." + error.Key;

            foreach (var message in error.Value)
            {
                ModelState.AddModelError(field, message);
            }
        }
    }

    /// <summary>
    /// Phân biệt tài nguyên không tìm thấy và thao tác không có quyền.
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