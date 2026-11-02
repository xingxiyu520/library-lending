using Library.Web.Data;
using Library.Web.Models;
using Library.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Library.Web.Controllers;
/// <summary>提供双角色只读目录和管理员维护入口，所有写操作由业务服务校验。</summary>
[Authorize(Roles = "Admin,Reader")]
public class BooksController(LibraryDbContext db, LibraryService service) : Controller
{
    /// <summary>按规范化关键词检索书名并应用白名单排序，投影实时可借数供两种角色浏览。</summary>
    public async Task<IActionResult> Index(string? q, string? sort)
    {
        var term = (q ?? "").Trim();
        var order = sort is "title_desc" or "available" or "available_desc" ? sort : "title";
        var query = db.Books.AsNoTracking().AsQueryable();
        if (term.Length > 0)
        {
            var isbn = LendingRules.NormalizeIsbn(term);
            query = query.Where(x => x.Title.Contains(term) || x.Author.Contains(term) || x.Isbn.Contains(term) || (isbn != "" && x.Isbn.Contains(isbn)));
        }

        query = order switch
        {
            "title_desc" => query.OrderByDescending(x => x.Title).ThenBy(x => x.Id),
            "available" => query.OrderBy(x => x.TotalCopies - x.Loans.Count(l => l.ReturnDate == null)).ThenBy(x => x.Title).ThenBy(x => x.Id),
            "available_desc" => query.OrderByDescending(x => x.TotalCopies - x.Loans.Count(l => l.ReturnDate == null)).ThenBy(x => x.Title).ThenBy(x => x.Id),
            _ => query.OrderBy(x => x.Title).ThenBy(x => x.Id)};
        var rows = await query.Select(x => new BookRow(x.Id, x.Isbn, x.Title, x.Author, x.Category, x.TotalCopies, x.TotalCopies - x.Loans.Count(l => l.ReturnDate == null))).ToListAsync();
        return View(new BookCatalog(rows, term, order));
    }

    /// <summary>展示或处理图书新增表单，服务端验证必填信息、ISBN唯一性及非负总册数。</summary>
    [Authorize(Roles = "Admin"), HttpGet]
    public IActionResult Create() => View(new BookInput());
    /// <summary>展示或处理图书新增表单，服务端验证必填信息、ISBN唯一性及非负总册数。 写操作受防伪令牌过滤器保护。</summary>
    [Authorize(Roles = "Admin"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BookInput input)
    {
        if (!ModelState.IsValid)
            return View(input);
        var result = await service.AddBookAsync(input);
        if (!result.Success)
        {
            ModelState.AddModelError("", result.Error!);
            return View(input);
        }

        TempData["Success"] = "Book added to the catalog.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>读取指定馆藏并处理修改；由业务事务保证总册数不能小于未归还数量。</summary>
    [Authorize(Roles = "Admin"), HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var book = await db.Books.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (book is null)
            return NotFound();
        ViewData["Id"] = id;
        return View(new BookInput { Isbn = book.Isbn, Title = book.Title, Author = book.Author, Category = book.Category, TotalCopies = book.TotalCopies });
    }

    /// <summary>读取指定馆藏并处理修改；由业务事务保证总册数不能小于未归还数量。 写操作受防伪令牌过滤器保护。</summary>
    [Authorize(Roles = "Admin"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, BookInput input)
    {
        if (!await db.Books.AnyAsync(x => x.Id == id))
            return NotFound();
        ViewData["Id"] = id;
        if (ModelState.IsValid)
        {
            var result = await service.EditBookAsync(id, input);
            if (result.Success)
            {
                TempData["Success"] = "Book updated.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError("", result.Error!);
        }

        return View(input);
    }

    /// <summary>读取图书及历史数量供删除确认，GET请求只展示信息。</summary>
    [Authorize(Roles = "Admin"), HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var book = await db.Books.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        return book is null ? NotFound() : View(book);
    }

    /// <summary>提交删除请求，业务层重新检查借阅历史，成功后返回馆藏列表。 写操作受防伪令牌过滤器保护。</summary>
    [Authorize(Roles = "Admin"), HttpPost, ValidateAntiForgeryToken, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var book = await db.Books.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (book is null)
            return NotFound();
        var result = await service.DeleteBookAsync(id);
        if (!result.Success)
        {
            ModelState.AddModelError("", result.Error!);
            return View("Delete", book);
        }

        TempData["Success"] = "Book deleted.";
        return RedirectToAction(nameof(Index));
    }
}
