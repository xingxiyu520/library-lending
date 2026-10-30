using Library.Web.Data;
using Library.Web.Models;
using Library.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Library.Web.Controllers;
[Authorize(Roles = "Admin")] public class BooksController(LibraryDbContext db, LibraryService service) : Controller {
 public async Task<IActionResult> Index() => View(await db.Books.AsNoTracking().OrderBy(x => x.Title).Select(x => new BookRow(x.Id, x.Isbn, x.Title, x.Author, x.Category, x.TotalCopies, x.TotalCopies - x.Loans.Count(l => l.ReturnDate == null))).ToListAsync());
 [HttpGet] public IActionResult Create() => View(new BookInput());
 [HttpPost] public async Task<IActionResult> Create(BookInput input) {
  if (!ModelState.IsValid) return View(input); var result = await service.AddBookAsync(input);
  if (!result.Success) { ModelState.AddModelError("", result.Error!); return View(input); }
  TempData["Success"] = "Book added to the catalog."; return RedirectToAction(nameof(Index));
 }
}
