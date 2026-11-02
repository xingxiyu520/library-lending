using Library.Web.Data;
using Library.Web.Models;
using Library.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Library.Web.Controllers;
/// <summary>管理员借阅管理入口，登记、归还、更正及删除均验证角色与防伪令牌。</summary>
[Authorize(Roles = "Admin")]
public class LoansController(LibraryDbContext db, LibraryService service, BusinessClock clock, LoanQueryService queries) : Controller
{
    // 管理员允许检索全部记录，查询参数通过白名单归一化。
    /// <summary>管理员按书名或读者姓名组合查询全部借阅，状态和排序使用归一化后的白名单值。</summary>
    public async Task<IActionResult> Index(string? q, string? status, string? sort) => View(await queries.ForAdminAsync(LoanFilters.Normalize(q, status, sort)));
    /// <summary>展示或提交借阅日期更正，输入模型只允许两个日期；存在性及最终日期规则由服务再次核对。</summary>
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var loan = await db.Loans.AsNoTracking().Include(x => x.Book).Include(x => x.Reader).SingleOrDefaultAsync(x => x.Id == id);
        if (loan is null)
            return NotFound();
        ViewData["Loan"] = loan;
        return View(new LoanEditInput { BorrowDate = loan.BorrowDate, DueDate = loan.DueDate });
    }

    /// <summary>展示或提交借阅日期更正，输入模型只允许两个日期；存在性及最终日期规则由服务再次核对。 写操作受防伪令牌过滤器保护。</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, LoanEditInput input)
    {
        var loan = await db.Loans.AsNoTracking().Include(x => x.Book).Include(x => x.Reader).SingleOrDefaultAsync(x => x.Id == id);
        if (loan is null)
            return NotFound();
        ViewData["Loan"] = loan;
        if (ModelState.IsValid)
        {
            var result = await service.EditLoanAsync(id, input);
            if (result.Success)
            {
                TempData["Success"] = "Loan dates updated.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError("", result.Error!);
        }

        return View(input);
    }

    /// <summary>读取借阅用于误录删除确认，GET请求不改变数据库。</summary>
    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var loan = await db.Loans.AsNoTracking().Include(x => x.Book).Include(x => x.Reader).SingleOrDefaultAsync(x => x.Id == id);
        return loan is null ? NotFound() : View(loan);
    }

    /// <summary>请求删除未归还的误录借阅；事务内重新检查归还状态，避免旧确认页删除已归还历史。 写操作受防伪令牌过滤器保护。</summary>
    [HttpPost, ValidateAntiForgeryToken, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var loan = await db.Loans.AsNoTracking().Include(x => x.Book).Include(x => x.Reader).SingleOrDefaultAsync(x => x.Id == id);
        if (loan is null)
            return NotFound();
        var result = await service.DeleteLoanAsync(id);
        if (!result.Success)
        {
            ModelState.AddModelError("", result.Error!);
            return View("Delete", loan);
        }

        TempData["Success"] = "Mistaken loan deleted.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>准备图书读者选项或登记借出，页面生成唯一请求标识供服务端处理重复提交。</summary>
    [HttpGet]
    public async Task<IActionResult> Create() => View(await Choices(new LoanInput { RequestId = Guid.NewGuid().ToString(), BorrowDate = clock.Today, DueDate = clock.Today.AddDays(14) }));
    /// <summary>准备图书读者选项或登记借出，页面生成唯一请求标识供服务端处理重复提交。 写操作受防伪令牌过滤器保护。</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LoanInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await service.LendAsync(input);
            if (result.Success)
            {
                TempData["Success"] = "Loan recorded.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError("", result.Error!);
        }

        return View(await Choices(input));
    }

    /// <summary>展示或提交实际归还日期，事务内检查未归还状态及借出日到今天的日期边界。</summary>
    [HttpGet]
    public async Task<IActionResult> Return(int id)
    {
        var loan = await db.Loans.AsNoTracking().Include(x => x.Book).SingleOrDefaultAsync(x => x.Id == id);
        if (loan is null)
            return NotFound();
        ViewData["Loan"] = loan;
        return View(new ReturnInput { ReturnDate = clock.Today });
    }

    /// <summary>展示或提交实际归还日期，事务内检查未归还状态及借出日到今天的日期边界。 写操作受防伪令牌过滤器保护。</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(int id, ReturnInput input)
    {
        var loan = await db.Loans.AsNoTracking().Include(x => x.Book).SingleOrDefaultAsync(x => x.Id == id);
        if (loan is null)
            return NotFound();
        ViewData["Loan"] = loan;
        if (ModelState.IsValid)
        {
            var result = await service.ReturnAsync(id, input);
            if (result.Success)
            {
                TempData["Success"] = "Book returned.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError("", result.Error!);
        }

        return View(input);
    }

    /// <summary>读取图书与读者下拉选项，并计算当前可借数量用于登记页面提示。</summary>
    private async Task<LoanInput> Choices(LoanInput input)
    {
        input.Books = await db.Books.OrderBy(x => x.Title).Select(x => new SelectListItem(x.Title + " (" + (x.TotalCopies - x.Loans.Count(l => l.ReturnDate == null)) + " available)", x.Id.ToString())).ToListAsync();
        input.Readers = await db.Readers.OrderBy(x => x.Name).Select(x => new SelectListItem(x.Name + " — " + x.Email, x.Id.ToString())).ToListAsync();
        return input;
    }
}
