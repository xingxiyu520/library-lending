using System.ComponentModel.DataAnnotations;
using System.Data;
using Library.Web.Data;
using Library.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
namespace Library.Web.Services;
public class LibraryService(LibraryDbContext db, UserManager<IdentityUser> users, BusinessClock clock) {
 private static string? Invalid(object input) {
  var errors = new List<ValidationResult>();
  return Validator.TryValidateObject(input, new ValidationContext(input), errors, true) ? null : errors[0].ErrorMessage;
 }
 public async Task<OperationResult> AddBookAsync(BookInput input) {
  if (Invalid(input) is { } error) return OperationResult.Fail(error);
  var isbn = LendingRules.NormalizeIsbn(input.Isbn);
  if (isbn.Length == 0) return OperationResult.Fail("ISBN is required.");
  if (await db.Books.AnyAsync(x => x.Isbn == isbn)) return OperationResult.Fail("A book with this ISBN already exists.");
  var book = new Book { Isbn = isbn, Title = input.Title.Trim(), Author = input.Author.Trim(), Category = input.Category?.Trim(), TotalCopies = input.TotalCopies!.Value };
  db.Books.Add(book);
  try { await db.SaveChangesAsync(); return OperationResult.Ok(book.Id); }
  catch (DbUpdateException ex) when (IsDuplicate(ex)) { db.ChangeTracker.Clear(); return OperationResult.Fail("A book with this ISBN already exists."); }
 }
 public async Task<OperationResult> CreateReaderAsync(ReaderInput input) {
  if (Invalid(input) is { } error) return OperationResult.Fail(error);
  var email = input.Email.Trim().ToLowerInvariant();
  await using var tx = await db.Database.BeginTransactionAsync();
  try {
   var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
   var created = await users.CreateAsync(user, input.Password);
   if (!created.Succeeded) return OperationResult.Fail(string.Join(" ", created.Errors.Select(x => x.Description)));
   var role = await users.AddToRoleAsync(user, "Reader");
   if (!role.Succeeded) return OperationResult.Fail("Reader account could not be provisioned.");
   var reader = new Reader { UserId = user.Id, Name = input.Name.Trim(), Email = email, Phone = input.Phone?.Trim() };
   db.Readers.Add(reader); await db.SaveChangesAsync(); await tx.CommitAsync();
   return OperationResult.Ok(reader.Id);
  } catch (DbUpdateException ex) when (IsDuplicate(ex)) { return OperationResult.Fail("An account with this email already exists."); }
  finally { db.ChangeTracker.Clear(); }
 }
 public async Task<OperationResult> LendAsync(LoanInput input) {
  if (Invalid(input) is { } error) return OperationResult.Fail(error);
  if (!Guid.TryParseExact(input.RequestId, "D", out _)) return OperationResult.Fail("Invalid submission token. Reload the form.");
  if (LendingRules.ValidateDates(input.BorrowDate!.Value, input.DueDate!.Value, clock.Today) is { } dateError) return OperationResult.Fail(dateError);
  // All lending operations lock the parent book before counting committed loans.
  await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
  var book = await db.Books.FromSqlInterpolated($"SELECT * FROM Books WHERE Id = {input.BookId} FOR UPDATE").SingleOrDefaultAsync();
  if (book is null) return OperationResult.Fail("The selected book no longer exists.");
  var previous = await db.Loans.SingleOrDefaultAsync(x => x.RequestId == input.RequestId);
  if (previous is not null) return Matches(previous, input) ? OperationResult.Ok(previous.Id) : OperationResult.Fail("This submission token has already been used.");
  if (!await db.Readers.AnyAsync(x => x.Id == input.ReaderId)) return OperationResult.Fail("The selected reader no longer exists.");
  var outstanding = await db.Loans.CountAsync(x => x.BookId == book.Id && x.ReturnDate == null);
  if (outstanding >= book.TotalCopies) return OperationResult.Fail("No copies are available for this book.");
  var loan = new Loan { BookId = book.Id, ReaderId = input.ReaderId!.Value, BorrowDate = input.BorrowDate.Value, DueDate = input.DueDate.Value, RequestId = input.RequestId };
  db.Loans.Add(loan);
  try { await db.SaveChangesAsync(); await tx.CommitAsync(); return OperationResult.Ok(loan.Id); }
  catch (DbUpdateException ex) when (IsDuplicate(ex)) { db.ChangeTracker.Clear(); return OperationResult.Fail("This submission token has already been used."); }
 }
 private static bool Matches(Loan l, LoanInput i) => l.BookId == i.BookId && l.ReaderId == i.ReaderId && l.BorrowDate == i.BorrowDate && l.DueDate == i.DueDate;
 private static bool IsDuplicate(DbUpdateException e) => e.InnerException is MySqlException { Number: 1062 };
}
