using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Library.Web.Controllers;
/// <summary>按登录角色选择首页，并提供不泄露内部异常的统一错误页。</summary>
[Authorize]
public class HomeController : Controller
{
    /// <summary>根据管理员或读者角色进入对应工作台，无有效登录时交由授权流程处理。</summary>
    public IActionResult Index() => View(User.IsInRole("Admin") ? "Admin" : "Reader");
    /// <summary>展示通用故障页，避免将异常堆栈、连接信息或内部路径输出给访问者。</summary>
    [AllowAnonymous, IgnoreAntiforgeryToken]
    public IActionResult Error()
    {
        Response.StatusCode = 500;
        return View();
    }
}
