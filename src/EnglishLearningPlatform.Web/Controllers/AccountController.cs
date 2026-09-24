using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;


namespace EnglishLearningPlatform.Web.Controllers;

[AllowAnonymous]
public sealed class AccountController : Controller
{
    private readonly IAccountService _accountService;

    public AccountController(IAccountService accountService)
    {
        _accountService = accountService;
    }

    [HttpGet]
    public IActionResult Register()
    {
        ViewBag.SuccessMessage = TempData["SuccessMessage"];
        return View();
    }

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

        var result = await _accountService.RegisterStudentAsync(request);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error);

            return View(model);
        }

        TempData["SuccessMessage"] = "Đăng ký thành công.";

        return RedirectToAction(nameof(Register));
    }
}
