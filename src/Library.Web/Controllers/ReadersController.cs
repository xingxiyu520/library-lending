using Library.Web.Data;
using Library.Web.Models;
using Library.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Library.Web.Controllers;
/// <summary>管理员读者管理入口，档案与登录账号由同一业务事务维护。</summary>
[Authorize(Roles = "Admin")]
public class ReadersController(LibraryDbContext db, LibraryService service) : Controller
{
    /// <summary>按读者姓名或邮箱查询并白名单排序，页面仅对管理员开放。</summary>
    public async Task<IActionResult> Index(string? q, string? sort)
    {
        var term = (q ?? "").Trim();
        var order = sort == "name_desc" ? "name_desc" : "name";
        var query = db.Readers.AsNoTracking().AsQueryable();
        if (term.Length > 0)
            query = query.Where(x => x.Name.Contains(term) || x.Email.Contains(term));
        query = order == "name_desc" ? query.OrderByDescending(x => x.Name).ThenBy(x => x.Id) : query.OrderBy(x => x.Name).ThenBy(x => x.Id);
        return View(new ReaderDirectory(await query.ToListAsync(), term, order));
    }

    /// <summary>展示或处理读者与关联登录账号的创建，固定分配Reader角色。</summary>
    [HttpGet]
    public IActionResult Create() => View(new ReaderInput());
    /// <summary>展示或处理读者与关联登录账号的创建，固定分配Reader角色。 写操作受防伪令牌过滤器保护。</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ReaderInput input)
    {
        if (!ModelState.IsValid)
            return View(input);
        var result = await service.CreateReaderAsync(input);
        if (!result.Success)
        {
            ModelState.AddModelError("", result.Error!);
            return View(input);
        }

        TempData["Success"] = "Reader and login account created.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>展示或保存读者档案更正，姓名邮箱与关联账号在同一事务中同步。</summary>
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var reader = await db.Readers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (reader is null)
            return NotFound();
        ViewData["Id"] = id;
        return View(new ReaderEditInput { Name = reader.Name, Email = reader.Email, Phone = reader.Phone });
    }

    /// <summary>展示或保存读者档案更正，姓名邮箱与关联账号在同一事务中同步。 写操作受防伪令牌过滤器保护。</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ReaderEditInput input)
    {
        if (!await db.Readers.AnyAsync(x => x.Id == id))
            return NotFound();
        ViewData["Id"] = id;
        if (ModelState.IsValid)
        {
            var result = await service.EditReaderAsync(id, input);
            if (result.Success)
            {
                TempData["Success"] = "Reader and login account updated.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError("", result.Error!);
        }

        return View(input);
    }

    /// <summary>加载读者档案及历史数量用于确认，页面读取不会删除账号。</summary>
    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var reader = await db.Readers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        return reader is null ? NotFound() : View(reader);
    }

    /// <summary>请求业务层原子删除无借阅历史的读者和账号，失败保留确认页面。 写操作受防伪令牌过滤器保护。</summary>
    [HttpPost, ValidateAntiForgeryToken, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var reader = await db.Readers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (reader is null)
            return NotFound();
        var result = await service.DeleteReaderAsync(id);
        if (!result.Success)
        {
            ModelState.AddModelError("", result.Error!);
            return View("Delete", reader);
        }

        TempData["Success"] = "Reader and login account deleted.";
        return RedirectToAction(nameof(Index));
    }
}
