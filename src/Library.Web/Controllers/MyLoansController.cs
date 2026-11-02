using System.Security.Claims;
using Library.Web.Data;
using Library.Web.Models;
using Library.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Library.Web.Controllers;
// 读者只读入口：始终从登录声明寻找档案，客户端的readerId/id没有绑定位置。
/// <summary>读者个人借阅入口，使用登录声明映射档案后再检索，拒绝客户端指定身份。</summary>
[Authorize(Roles = "Reader")]
public class MyLoansController(LibraryDbContext db, LoanQueryService queries) : Controller
{
    /// <summary>先用登录账号映射读者档案，再查询该读者自己的借阅；缺失档案返回409和页面说明。</summary>
    public async Task<IActionResult> Index(string? q, string? status, string? sort)
    {
        var filters = LoanFilters.Normalize(q, status, sort);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var readerId = await db.Readers.AsNoTracking().Where(x => x.UserId == userId).Select(x => (int? )x.Id).SingleOrDefaultAsync();
        // 没有档案时明确返回409，绝不退回管理员的全量查询。
        if (!readerId.HasValue)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            return View(new LoanListPage([], filters, true, true));
        }

        return View(await queries.ForReaderAsync(readerId.Value, filters));
    }
}
