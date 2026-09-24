using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;


namespace EnglishLearningPlatform.Web.Controllers;

public sealed class AccountController : Controller
{
    private readonly IRegistrationService _registrationService;
    private readonly ILoginService _loginService;

    public AccountController(
        IRegistrationService registrationService,
        ILoginService loginService)
    {
        _registrationService = registrationService;
        _loginService = loginService;
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Register()
    {
        ViewBag.SuccessMessage = TempData["SuccessMessage"];
        return View();
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var request = new RegisterStudentRequest(
            model.FullName,
            model.Email,
            model.Password
        );

        var result = await _registrationService.RegisterStudentAsync(request);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error);

            return View(model);
        }

        TempData["SuccessMessage"] = "Đăng ký thành công.";

        return RedirectToAction(nameof(Register));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _loginService.LoginAsync(
            new LoginRequest(model.Email, model.Password, model.RememberMe));

        if (result.Status == LoginStatus.Succeeded)
        {
            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                return LocalRedirect(model.ReturnUrl);

            return RedirectToAction("Index", "Home");
        }

        var message = result.Status == LoginStatus.TemporarilyLocked
            ? "Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau."
            : "Không thể đăng nhập. Vui lòng kiểm tra thông tin hoặc trạng thái tài khoản.";

        ModelState.AddModelError(string.Empty, message);
        return View(model);
    }
}
