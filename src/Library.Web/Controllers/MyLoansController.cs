using System.Security.Claims;
using Library.Web.Data;
using Library.Web.Models;
using Library.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Library.Web.Controllers;
[Authorize(Roles = "Reader")]
public class MyLoansController(LibraryDbContext db, BusinessClock clock) : Controller {
 public async Task<IActionResult> Index() {
  var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
  var readerId = await db.Readers.AsNoTracking().Where(x => x.UserId == userId).Select(x => (int?)x.Id).SingleOrDefaultAsync();
  if (!readerId.HasValue) { Response.StatusCode = StatusCodes.Status409Conflict; return View(new MyLoansPage([], true)); }
  var records = await db.Loans.AsNoTracking().Where(x => x.ReaderId == readerId.Value).OrderByDescending(x => x.BorrowDate).ThenByDescending(x => x.Id)
   .Select(x => new { x.Id, x.Book.Title, x.BorrowDate, x.DueDate, x.ReturnDate }).ToListAsync();
  var today = clock.Today;
  return View(new MyLoansPage(records.Select(x => new LoanRow(x.Id, x.Title, x.BorrowDate, x.DueDate, x.ReturnDate, LendingRules.LoanStatus(x.DueDate, x.ReturnDate, today))).ToList()));
 }
}
