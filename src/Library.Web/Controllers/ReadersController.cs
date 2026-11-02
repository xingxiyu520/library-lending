using Library.Web.Data;
using Library.Web.Models;
using Library.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Library.Web.Controllers;
[Authorize(Roles = "Admin")] public class ReadersController(LibraryDbContext db, LibraryService service) : Controller {
 public async Task<IActionResult> Index(string? q, string? sort) {
  var term = (q ?? "").Trim(); var order = sort == "name_desc" ? "name_desc" : "name";
  var query = db.Readers.AsNoTracking().AsQueryable();
  if (term.Length > 0) query = query.Where(x => x.Name.Contains(term) || x.Email.Contains(term));
  query = order == "name_desc" ? query.OrderByDescending(x => x.Name).ThenBy(x => x.Id) : query.OrderBy(x => x.Name).ThenBy(x => x.Id);
  return View(new ReaderDirectory(await query.ToListAsync(), term, order));
 }
 [HttpGet] public IActionResult Create() => View(new ReaderInput());
 [HttpPost] public async Task<IActionResult> Create(ReaderInput input) {
  if (!ModelState.IsValid) return View(input); var result = await service.CreateReaderAsync(input);
  if (!result.Success) { ModelState.AddModelError("", result.Error!); return View(input); }
  TempData["Success"] = "Reader and login account created."; return RedirectToAction(nameof(Index));
 }
 [HttpGet] public async Task<IActionResult> Edit(int id) {
  var reader = await db.Readers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
  if (reader is null) return NotFound(); ViewData["Id"] = id;
  return View(new ReaderEditInput { Name = reader.Name, Email = reader.Email, Phone = reader.Phone });
 }
 [HttpPost] public async Task<IActionResult> Edit(int id, ReaderEditInput input) {
  if (!await db.Readers.AnyAsync(x => x.Id == id)) return NotFound(); ViewData["Id"] = id;
  if (ModelState.IsValid) {
   var result = await service.EditReaderAsync(id, input);
   if (result.Success) { TempData["Success"] = "Reader and login account updated."; return RedirectToAction(nameof(Index)); }
   ModelState.AddModelError("", result.Error!);
  }
  return View(input);
 }
 [HttpGet] public async Task<IActionResult> Delete(int id) {
  var reader = await db.Readers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
  return reader is null ? NotFound() : View(reader);
 }
 [HttpPost, ActionName("Delete")] public async Task<IActionResult> DeleteConfirmed(int id) {
  var reader = await db.Readers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
  if (reader is null) return NotFound(); var result = await service.DeleteReaderAsync(id);
  if (!result.Success) { ModelState.AddModelError("", result.Error!); return View("Delete", reader); }
  TempData["Success"] = "Reader and login account deleted."; return RedirectToAction(nameof(Index));
 }
}
