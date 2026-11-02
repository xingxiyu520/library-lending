using Library.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Library.Web.Controllers;
/// <summary>处理登录、退出及拒绝访问页面，账号验证交给ASP.NET Core Identity。</summary>
public class AccountController(SignInManager<IdentityUser> signIn) : Controller
{
    /// <summary>展示登录表单或验证账号密码；失败使用统一提示，成功只允许跳转到站内地址。</summary>
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null) => User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Home") : View(new LoginInput { ReturnUrl = returnUrl });
    /// <summary>展示登录表单或验证账号密码；失败使用统一提示，成功只允许跳转到站内地址。 写操作受防伪令牌过滤器保护。</summary>
    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginInput input)
    {
        if (!ModelState.IsValid)
            return View(input);
        var result = await signIn.PasswordSignInAsync(input.Email.Trim().ToLowerInvariant(), input.Password, false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", "Unable to sign in with these credentials.");
            return View(input);
        }

        return Url.IsLocalUrl(input.ReturnUrl) ? LocalRedirect(input.ReturnUrl!) : RedirectToAction("Index", "Home");
    }

    /// <summary>撤销当前登录Cookie并返回登录页。 写操作受防伪令牌过滤器保护。</summary>
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    /// <summary>返回403页面，使已登录但权限不足的请求得到明确响应。</summary>
    [AllowAnonymous, HttpGet]
    public IActionResult Denied()
    {
        Response.StatusCode = 403;
        return View();
    }
}
