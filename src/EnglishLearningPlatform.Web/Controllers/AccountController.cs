using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Web.Infrastructure;
using EnglishLearningPlatform.Web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;


namespace EnglishLearningPlatform.Web.Controllers;

public sealed class AccountController : Controller
{
    private readonly IRegistrationService _registrationService;
    private readonly ILoginService _loginService;
    private readonly ILogoutService _logoutService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public AccountController(
        IRegistrationService registrationService,
        ILoginService loginService,
        ILogoutService logoutService,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _registrationService = registrationService;
        _loginService = loginService;
        _logoutService = logoutService;
        _configuration = configuration;
        _environment = environment;
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Register(string? returnUrl = null)
    {
        ViewBag.SuccessMessage = TempData["SuccessMessage"];
        return View(new RegisterViewModel
        {
            ReturnUrl = RoleNavigation.IsValidReturnUrl(Url, returnUrl) ? returnUrl : null
        });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        model.ReturnUrl = RoleNavigation.IsValidReturnUrl(Url, model.ReturnUrl) ? model.ReturnUrl : null;
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

        TempData["SuccessMessage"] = "Đăng ký thành công. Vui lòng đăng nhập.";

        return RedirectToAction(nameof(Login), new
        {
            returnUrl = RoleNavigation.IsValidReturnUrl(Url, model.ReturnUrl) ? model.ReturnUrl : null
        });
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        return LoginView(new LoginViewModel
        {
            ReturnUrl = RoleNavigation.IsValidReturnUrl(Url, returnUrl) ? returnUrl : null
        });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return LoginView(model);

        var result = await _loginService.LoginAsync(
            new LoginRequest(model.Email, model.Password, model.RememberMe));

        if (result.Status == LoginStatus.Succeeded)
        {
            if (Request.Headers["X-Auth-Modal"] == "true")
            {
                var redirectUrl = RoleNavigation.IsValidReturnUrl(Url, model.ReturnUrl)
                    ? Url.Content(model.ReturnUrl!)
                    : Url.Content("~" + RoleNavigation.GetWorkspaceUrl(result.Role));
                return Json(new { redirectUrl });
            }

            if (RoleNavigation.IsValidReturnUrl(Url, model.ReturnUrl))
                return LocalRedirect(model.ReturnUrl!);

            return this.RedirectToRoleWorkspace(result.Role);
        }

        var message = result.Status == LoginStatus.TemporarilyLocked
            ? "Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau."
            : "Không thể đăng nhập. Vui lòng kiểm tra thông tin hoặc trạng thái tài khoản.";

        ModelState.AddModelError(string.Empty, message);
        return LoginView(model);
    }

    private ViewResult LoginView(LoginViewModel model)
    {
        if (!RoleNavigation.IsValidReturnUrl(Url, model.ReturnUrl)) model.ReturnUrl = null;
        model.DemoPassword = _environment.IsDevelopment()
            ? _configuration["LearningDemo:Password"]
            : null;
        return View(nameof(Login), model);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _logoutService.LogoutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }
}
