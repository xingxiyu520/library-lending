using Library.Web.Data;
using Library.Web.Models;
using Library.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
namespace Library.Tests;
[TestClass, TestCategory("MySQL")]
public class Sprint2DatabaseTests {
 private TestEnvironment env = null!;
 private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
 [TestInitialize] public void Initialize() => env = TestEnvironment.Current;
 private BookInput Data(int copies = 1) => new() { Isbn = Guid.NewGuid().ToString("N"), Title = "Maintenance book", Author = "Author", TotalCopies = copies };
 private async Task<int> Book(int copies = 1) { var result = await env.Use((s, _) => s.AddBookAsync(Data(copies))); Assert.IsTrue(result.Success, result.Error); return result.Id!.Value; }
 private async Task<int> Reader() { var result = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = "Maintenance reader", Email = Guid.NewGuid().ToString("N") + "@library.example", Password = env.Password })); Assert.IsTrue(result.Success, result.Error); return result.Id!.Value; }
 private async Task<int> Loan(int book, int reader) {
  var result = await env.Use((s, _) => s.LendAsync(new() { BookId = book, ReaderId = reader, BorrowDate = Today.AddDays(-2), DueDate = Today.AddDays(12), RequestId = Guid.NewGuid().ToString() })); Assert.IsTrue(result.Success, result.Error); return result.Id!.Value;
 }
 [TestMethod] public async Task Book_edit_persists_and_normalizes_ISBN() {
  var id = await Book(); var input = Data(2); input.Isbn = " ab-" + input.Isbn[..12]; input.Title = " Updated ";
  Assert.IsTrue((await env.Use((s, _) => s.EditBookAsync(id, input))).Success);
  var saved = await env.Use((_, db) => db.Books.SingleAsync(x => x.Id == id));
  Assert.AreEqual("Updated", saved.Title); Assert.AreEqual(LendingRules.NormalizeIsbn(input.Isbn), saved.Isbn); Assert.AreEqual(2, saved.TotalCopies);
 }
 [TestMethod] public async Task Book_edit_rejects_duplicate_without_changing_original() {
  var a = await Book(); var b = await Book(); var other = await env.Use((_, db) => db.Books.SingleAsync(x => x.Id == b)); var input = Data(); input.Isbn = other.Isbn;
  Assert.IsFalse((await env.Use((s, _) => s.EditBookAsync(a, input))).Success);
  Assert.AreNotEqual(other.Isbn, await env.Use((_, db) => db.Books.Where(x => x.Id == a).Select(x => x.Isbn).SingleAsync()));
 }
 [TestMethod] public async Task Book_quantity_cannot_drop_below_outstanding_loans() {
  var book = await Book(2); await Loan(book, await Reader());
  var input = Data(0); Assert.IsFalse((await env.Use((s, _) => s.EditBookAsync(book, input))).Success);
  Assert.AreEqual(2, await env.Use((_, db) => db.Books.Where(x => x.Id == book).Select(x => x.TotalCopies).SingleAsync()));
  input.TotalCopies = 1; Assert.IsTrue((await env.Use((s, _) => s.EditBookAsync(book, input))).Success);
 }
 [TestMethod] public async Task Quantity_change_and_last_copy_loan_preserve_stock() {
  var book = await Book(); var reader = await Reader();
  var results = await Task.WhenAll(env.Use((s, _) => s.EditBookAsync(book, Data(0))), env.Use((s, _) => s.LendAsync(new() { BookId = book, ReaderId = reader, BorrowDate = Today, DueDate = Today, RequestId = Guid.NewGuid().ToString() })));
  Assert.AreEqual(1, results.Count(x => x.Success));
  Assert.IsTrue(await env.Use(async (_, db) => await db.Loans.CountAsync(x => x.BookId == book && x.ReturnDate == null) <= await db.Books.Where(x => x.Id == book).Select(x => x.TotalCopies).SingleAsync()));
 }
 [TestMethod] public async Task Book_without_history_can_be_deleted() {
  var book = await Book(); Assert.IsTrue((await env.Use((s, _) => s.DeleteBookAsync(book))).Success); Assert.IsFalse(await env.Use((_, db) => db.Books.AnyAsync(x => x.Id == book)));
 }
 [DataTestMethod][DataRow(false)][DataRow(true)] public async Task Book_with_any_history_is_protected(bool returned) {
  var book = await Book(); var loan = await Loan(book, await Reader()); if (returned) Assert.IsTrue((await env.Use((s, _) => s.ReturnAsync(loan, new() { ReturnDate = Today }))).Success);
  var result = await env.Use((s, _) => s.DeleteBookAsync(book)); Assert.IsFalse(result.Success); StringAssert.Contains(result.Error!, "loan history");
 }
 [TestMethod] public async Task Book_delete_racing_with_loan_has_one_winner() {
  var book = await Book(); var reader = await Reader(); var results = await Task.WhenAll(env.Use((s, _) => s.DeleteBookAsync(book)), env.Use((s, _) => s.LendAsync(new() { BookId = book, ReaderId = reader, BorrowDate = Today, DueDate = Today, RequestId = Guid.NewGuid().ToString() })));
  Assert.AreEqual(1, results.Count(x => x.Success));
 }
 [TestMethod] public async Task Reader_edit_updates_profile_account_and_preserves_password_role() {
  var id = await Reader(); var email = Guid.NewGuid().ToString("N") + "@library.example";
  Assert.IsTrue((await env.Use((s, _) => s.EditReaderAsync(id, new() { Name = " Updated reader ", Email = email.ToUpperInvariant(), Phone = "12345" }))).Success);
  var saved = await env.Use((_, db) => db.Readers.SingleAsync(x => x.Id == id)); Assert.AreEqual("Updated reader", saved.Name); Assert.AreEqual(email, saved.Email); Assert.AreEqual("12345", saved.Phone);
  using var scope = env.Scope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>(); var user = await users.FindByIdAsync(saved.UserId); Assert.IsNotNull(user); Assert.AreEqual(email, user.UserName); Assert.IsTrue(await users.CheckPasswordAsync(user, env.Password)); CollectionAssert.AreEquivalent(new[] { "Reader" }, (await users.GetRolesAsync(user)).ToArray());
 }
 [TestMethod] public async Task Duplicate_reader_email_keeps_original_profile_and_account() {
  var id = await Reader(); var old = await env.Use((_, db) => db.Readers.SingleAsync(x => x.Id == id));
  Assert.IsFalse((await env.Use((s, _) => s.EditReaderAsync(id, new() { Name = "Should rollback", Email = "ADMIN@LIBRARY.EXAMPLE" }))).Success);
  var saved = await env.Use((_, db) => db.Readers.Include(x => x.User).SingleAsync(x => x.Id == id)); Assert.AreEqual(old.Email, saved.Email); Assert.AreEqual(old.Name, saved.Name); Assert.AreEqual(old.Email, saved.User.UserName);
 }
 [TestMethod] public async Task Deleting_unborrowed_reader_removes_login_account() {
  var id = await Reader(); var userId = await env.Use((_, db) => db.Readers.Where(x => x.Id == id).Select(x => x.UserId).SingleAsync()); Assert.IsTrue((await env.Use((s, _) => s.DeleteReaderAsync(id))).Success);
  Assert.IsFalse(await env.Use((_, db) => db.Readers.AnyAsync(x => x.Id == id))); Assert.IsFalse(await env.Use((_, db) => db.Users.AnyAsync(x => x.Id == userId)));
 }
 [DataTestMethod][DataRow(false)][DataRow(true)] public async Task Reader_with_any_history_and_account_is_protected(bool returned) {
  var reader = await Reader(); var loan = await Loan(await Book(), reader); if (returned) Assert.IsTrue((await env.Use((s, _) => s.ReturnAsync(loan, new() { ReturnDate = Today }))).Success);
  Assert.IsFalse((await env.Use((s, _) => s.DeleteReaderAsync(reader))).Success); Assert.IsTrue(await env.Use((_, db) => db.Readers.Include(x => x.User).AnyAsync(x => x.Id == reader && x.User != null)));
 }
 [TestMethod] public async Task Reader_delete_racing_with_lending_has_one_winner() {
  var reader = await Reader(); var book = await Book(); var results = await Task.WhenAll(env.Use((s, _) => s.DeleteReaderAsync(reader)), env.Use((s, _) => s.LendAsync(new() { BookId = book, ReaderId = reader, BorrowDate = Today, DueDate = Today, RequestId = Guid.NewGuid().ToString() })));
  Assert.AreEqual(1, results.Count(x => x.Success));
 }
 [TestMethod] public async Task Return_restores_one_copy_and_persists_date() {
  var book = await Book(); var loan = await Loan(book, await Reader()); Assert.IsTrue((await env.Use((s, _) => s.ReturnAsync(loan, new() { ReturnDate = Today }))).Success);
  Assert.AreEqual(Today, await env.Use((_, db) => db.Loans.Where(x => x.Id == loan).Select(x => x.ReturnDate).SingleAsync())); Assert.AreEqual(0, await env.Use((_, db) => db.Loans.CountAsync(x => x.BookId == book && x.ReturnDate == null)));
 }
 [TestMethod] public async Task Repeated_return_does_not_change_original_date() {
  var loan = await Loan(await Book(), await Reader()); Assert.IsTrue((await env.Use((s, _) => s.ReturnAsync(loan, new() { ReturnDate = Today.AddDays(-1) }))).Success);
  var result = await env.Use((s, _) => s.ReturnAsync(loan, new() { ReturnDate = Today })); Assert.IsFalse(result.Success); StringAssert.Contains(result.Error!, "already been returned"); Assert.AreEqual(Today.AddDays(-1), await env.Use((_, db) => db.Loans.Where(x => x.Id == loan).Select(x => x.ReturnDate).SingleAsync()));
 }
 [TestMethod] public async Task Concurrent_return_updates_exactly_once() {
  var loan = await Loan(await Book(), await Reader()); var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => env.Use((s, _) => s.ReturnAsync(loan, new() { ReturnDate = Today })))); Assert.AreEqual(1, results.Count(x => x.Success));
 }
 [DataTestMethod][DataRow(-3)][DataRow(1)] public async Task Invalid_return_dates_leave_loan_outstanding(int offset) {
  var loan = await Loan(await Book(), await Reader()); Assert.IsFalse((await env.Use((s, _) => s.ReturnAsync(loan, new() { ReturnDate = Today.AddDays(offset) }))).Success); Assert.IsNull(await env.Use((_, db) => db.Loans.Where(x => x.Id == loan).Select(x => x.ReturnDate).SingleAsync()));
 }
 [TestMethod] public async Task Missing_entities_return_business_errors() {
  Assert.IsFalse((await env.Use((s, _) => s.EditBookAsync(int.MaxValue, Data()))).Success); Assert.IsFalse((await env.Use((s, _) => s.DeleteBookAsync(int.MaxValue))).Success);
  Assert.IsFalse((await env.Use((s, _) => s.EditReaderAsync(int.MaxValue, new() { Name = "Missing", Email = "missing@library.example" }))).Success); Assert.IsFalse((await env.Use((s, _) => s.DeleteReaderAsync(int.MaxValue))).Success); Assert.IsFalse((await env.Use((s, _) => s.ReturnAsync(int.MaxValue, new() { ReturnDate = Today }))).Success);
 }
 [DataTestMethod][DataRow(false)][DataRow(true)] public async Task Reader_edit_or_delete_failure_rolls_back_identity_changes(bool deleting) {
  var reader = await Reader(); var original = await env.Use((_, db) => db.Readers.Include(x => x.User).SingleAsync(x => x.Id == reader));
  var services = new ServiceCollection(); services.AddLogging(); services.AddDbContext<LibraryDbContext>(o => o.UseMySql(env.Connection, new MySqlServerVersion(new Version(8,4,0))).AddInterceptors(new FailMaintenance(deleting)));
  services.AddIdentityCore<IdentityUser>(o => { o.Password.RequireNonAlphanumeric = false; }).AddRoles<IdentityRole>().AddEntityFrameworkStores<LibraryDbContext>(); services.AddSingleton(TimeProvider.System); services.AddScoped<BusinessClock>(); services.AddScoped<LibraryService>();
  using(var provider = services.BuildServiceProvider()) { using var scope = provider.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<LibraryService>(); await Assert.ThrowsExceptionAsync<DbUpdateException>(() => deleting ? service.DeleteReaderAsync(reader) : service.EditReaderAsync(reader, new() { Name = "Rollback", Email = Guid.NewGuid().ToString("N") + "@library.example" })); }
  var saved = await env.Use((_, db) => db.Readers.Include(x => x.User).SingleAsync(x => x.Id == reader)); Assert.AreEqual(original.Email, saved.Email); Assert.AreEqual(original.Email, saved.User.Email); Assert.AreEqual(original.User.SecurityStamp, saved.User.SecurityStamp);
 }
 private sealed class FailMaintenance(bool deleting) : SaveChangesInterceptor {
  public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default) {
   if ((!deleting && data.Context!.ChangeTracker.Entries<Reader>().Any(x => x.State == EntityState.Modified)) || (deleting && data.Context!.ChangeTracker.Entries<IdentityUser>().Any(x => x.State == EntityState.Deleted))) throw new DbUpdateException("Injected maintenance failure.");
   return ValueTask.FromResult(result);
  }
 }
}
