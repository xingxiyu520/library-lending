using Library.Web.Data;
using Library.Web.Models;
using Library.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
namespace Library.Web.Controllers;
[Authorize(Roles = "Admin")] public class LoansController(LibraryDbContext db, LibraryService service, BusinessClock clock) : Controller {
 public async Task<IActionResult> Index() => View(await db.Loans.AsNoTracking().Include(x => x.Book).Include(x => x.Reader).OrderByDescending(x => x.Id).ToListAsync());
 [HttpGet] public async Task<IActionResult> Create() => View(await Choices(new LoanInput { RequestId = Guid.NewGuid().ToString(), BorrowDate = clock.Today, DueDate = clock.Today.AddDays(14) }));
 [HttpPost] public async Task<IActionResult> Create(LoanInput input) {
  if (ModelState.IsValid) { var result = await service.LendAsync(input);
   if (result.Success) { TempData["Success"] = "Loan recorded."; return RedirectToAction(nameof(Index)); }
   ModelState.AddModelError("", result.Error!);
  } return View(await Choices(input));
 }
 private async Task<LoanInput> Choices(LoanInput input) {
  input.Books = await db.Books.OrderBy(x => x.Title).Select(x => new SelectListItem(x.Title + " (" + (x.TotalCopies - x.Loans.Count(l => l.ReturnDate == null)) + " available)", x.Id.ToString())).ToListAsync();
  input.Readers = await db.Readers.OrderBy(x => x.Name).Select(x => new SelectListItem(x.Name + " — " + x.Email, x.Id.ToString())).ToListAsync(); return input;
 }
}
