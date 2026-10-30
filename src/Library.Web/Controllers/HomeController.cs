using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Library.Web.Controllers;
[Authorize] public class HomeController : Controller {
 public IActionResult Index() => View(User.IsInRole("Admin") ? "Admin" : "Reader");
 [AllowAnonymous, IgnoreAntiforgeryToken] public IActionResult Error() { Response.StatusCode = 500; return View(); }
}
