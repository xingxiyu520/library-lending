using Library.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
namespace Library.Web.Controllers;
public class AccountController(SignInManager<IdentityUser> signIn) : Controller {
 [AllowAnonymous, HttpGet] public IActionResult Login(string? returnUrl = null) => User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Home") : View(new LoginInput { ReturnUrl = returnUrl });
 [AllowAnonymous, HttpPost] public async Task<IActionResult> Login(LoginInput input) {
  if (!ModelState.IsValid) return View(input);
  var result = await signIn.PasswordSignInAsync(input.Email.Trim().ToLowerInvariant(), input.Password, false, lockoutOnFailure: true);
  if (!result.Succeeded) { ModelState.AddModelError("", "Unable to sign in with these credentials."); return View(input); }
  return Url.IsLocalUrl(input.ReturnUrl) ? LocalRedirect(input.ReturnUrl!) : RedirectToAction("Index", "Home");
 }
 [Authorize, HttpPost] public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return RedirectToAction(nameof(Login)); }
 [AllowAnonymous, HttpGet] public IActionResult Denied() { Response.StatusCode = 403; return View(); }
}
