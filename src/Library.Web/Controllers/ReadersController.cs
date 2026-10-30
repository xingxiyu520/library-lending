using Library.Web.Data;
using Library.Web.Models;
using Library.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Library.Web.Controllers;
[Authorize(Roles = "Admin")] public class ReadersController(LibraryDbContext db, LibraryService service) : Controller {
 public async Task<IActionResult> Index() => View(await db.Readers.AsNoTracking().OrderBy(x => x.Name).ToListAsync());
 [HttpGet] public IActionResult Create() => View(new ReaderInput());
 [HttpPost] public async Task<IActionResult> Create(ReaderInput input) {
  if (!ModelState.IsValid) return View(input); var result = await service.CreateReaderAsync(input);
  if (!result.Success) { ModelState.AddModelError("", result.Error!); return View(input); }
  TempData["Success"] = "Reader and login account created."; return RedirectToAction(nameof(Index));
 }
}
