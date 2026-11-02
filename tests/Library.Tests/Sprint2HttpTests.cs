using System.Net;
using System.Text.RegularExpressions;
using Library.Web.Data;
using Library.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Library.Tests;
[TestClass, TestCategory("Integration")]
public class Sprint2HttpTests {
 private TestEnvironment env = null!;
 private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
 [TestInitialize] public void Initialize() => env = TestEnvironment.Current;
 private HttpClient Client() => env.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
 private async Task<HttpClient> Admin() { var c = Client(); await HttpTests.Login(c, "admin@library.example", env.Password); return c; }
 private async Task<(int Id, string Email)> Reader() { var email = Guid.NewGuid().ToString("N") + "@library.example"; var result = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = "HTTP reader", Email = email, Password = env.Password })); Assert.IsTrue(result.Success, result.Error); return (result.Id!.Value, email); }
 private async Task<int> Book(string? title = null, int copies = 1, string author = "HTTP Author") { var result = await env.Use((s, _) => s.AddBookAsync(new() { Isbn = Guid.NewGuid().ToString("N"), Title = title ?? "HTTP book", Author = author, TotalCopies = copies })); Assert.IsTrue(result.Success, result.Error); return result.Id!.Value; }
 private async Task<int> Loan(int book, int reader, int due = 0) { var result = await env.Use((s, _) => s.LendAsync(new() { BookId = book, ReaderId = reader, BorrowDate = Today.AddDays(-3), DueDate = Today.AddDays(due), RequestId = Guid.NewGuid().ToString() })); Assert.IsTrue(result.Success, result.Error); return result.Id!.Value; }
 private static int[] BookIds(string html) => Regex.Matches(html, "data-book-id=\"([0-9]+)\"").Select(x => int.Parse(x.Groups[1].Value)).ToArray();
 [DataTestMethod][DataRow("/Books")][DataRow("/Books/Edit/1")][DataRow("/Readers/Edit/1")][DataRow("/Loans/Return/1")][DataRow("/MyLoans")]
 public async Task New_protected_routes_require_login(string path) { using var c = Client(); var response = await c.GetAsync(path); Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode); StringAssert.Contains(response.Headers.Location!.ToString(), "/Account/Login"); }
 [DataTestMethod][DataRow("/Books/Edit/1")][DataRow("/Books/Delete/1")][DataRow("/Readers/Edit/1")][DataRow("/Readers/Delete/1")][DataRow("/Loans/Return/1")]
 public async Task Reader_cannot_get_or_post_new_management_routes(string path) {
  using var c = Client(); await HttpTests.Login(c, "reader@library.example", env.Password); var token = await HttpTests.Token(c, "/");
  var before = await env.Use(async (_, db) => (await db.Books.CountAsync(), await db.Readers.CountAsync(), await db.Loans.CountAsync(x => x.ReturnDate != null)));
  foreach(var response in new[] { await c.GetAsync(path), await c.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = token, ["ReturnDate"] = Today.ToString("yyyy-MM-dd"), ["Role"] = "Admin" })) }) { Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode); StringAssert.Contains(response.Headers.Location!.ToString(), "/Account/Denied"); }
  Assert.AreEqual(before, await env.Use(async (_, db) => (await db.Books.CountAsync(), await db.Readers.CountAsync(), await db.Loans.CountAsync(x => x.ReturnDate != null))));
 }
 [DataTestMethod][DataRow("/Books/Edit/1")][DataRow("/Books/Delete/1")][DataRow("/Readers/Edit/1")][DataRow("/Readers/Delete/1")][DataRow("/Loans/Return/1")]
 public async Task New_mutations_require_antiforgery(string path) { using var c = await Admin(); var response = await c.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string,string>())); Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode); }
 [TestMethod] public async Task Reader_catalogue_is_read_only_and_searches_title_author_ISBN() {
  var key = Guid.NewGuid().ToString("N"); var id = await Book(key + " title", author: key + " author"); var isbn = await env.Use((_, db) => db.Books.Where(x => x.Id == id).Select(x => x.Isbn).SingleAsync());
  using var c = Client(); await HttpTests.Login(c, "reader@library.example", env.Password);
  foreach(var term in new[] { "  " + key + " title  ", key + " author", isbn.Insert(4, "-") }) { var html = await c.GetStringAsync("/Books?q=" + Uri.EscapeDataString(term)); CollectionAssert.AreEqual(new[] { id }, BookIds(html)); Assert.IsFalse(html.Contains("/Books/Edit") || html.Contains("/Books/Delete") || html.Contains("/Books/Create")); }
  StringAssert.Contains(await c.GetStringAsync("/Books?q=missing-" + key), "No books match your search.");
  Assert.IsTrue(BookIds(await c.GetStringAsync("/Books?q=%20%20")).Contains(id));
 }
 [DataTestMethod][DataRow("title")][DataRow("title_desc")][DataRow("available")][DataRow("available_desc")]
 public async Task Catalogue_combines_search_sort_and_stable_ties(string sort) {
  var key = Guid.NewGuid().ToString("N"); var a = await Book(key + " A", 1); var b = await Book(key + " B", 3); var cId = await Book(key + " A", 1);
  using var c = await Admin(); var ids = BookIds(await c.GetStringAsync($"/Books?q={key}&sort={sort}")); var expected = sort is "title_desc" or "available_desc" ? new[] { b, a, cId } : new[] { a, cId, b };
  CollectionAssert.AreEqual(expected, ids); CollectionAssert.AreEqual(ids, BookIds(await c.GetStringAsync($"/Books?q={key}&sort={sort}")));
  CollectionAssert.AreEqual(new[] { a, cId, b }, BookIds(await c.GetStringAsync($"/Books?q={key}&sort=Title%3BDROP%20TABLE%20Books")));
 }
 [TestMethod] public async Task Delete_confirmation_GET_does_not_mutate_and_POST_removes_book() {
  using var c = await Admin(); var id = await Book(); StringAssert.Contains(await c.GetStringAsync($"/Books/Delete/{id}"), "Confirm delete"); Assert.IsTrue(await env.Use((_, db) => db.Books.AnyAsync(x => x.Id == id)));
  Assert.AreEqual(HttpStatusCode.Redirect, (await HttpTests.Post(c, $"/Books/Delete/{id}", new())).StatusCode); Assert.IsFalse(await env.Use((_, db) => db.Books.AnyAsync(x => x.Id == id)));
 }
 [DataTestMethod][DataRow("-1")][DataRow("1.5")][DataRow("abc")][DataRow("")]
 public async Task Book_edit_invalid_copies_are_rejected_by_server(string copies) {
  var id = await Book(); using var c = await Admin(); var result = await HttpTests.Post(c, $"/Books/Edit/{id}", new() { ["Isbn"] = Guid.NewGuid().ToString("N"), ["Title"] = "Invalid", ["Author"] = "Author", ["TotalCopies"] = copies }); Assert.AreEqual(HttpStatusCode.OK, result.StatusCode); Assert.AreEqual(1, await env.Use((_, db) => db.Books.Where(x => x.Id == id).Select(x => x.TotalCopies).SingleAsync()));
 }
 [TestMethod] public async Task Edited_reader_login_uses_new_email_old_session_is_revoked_and_role_injection_fails() {
  var reader = await Reader(); using var c = await Admin(); using var old = Client(); await HttpTests.Login(old, reader.Email, env.Password); var email = Guid.NewGuid().ToString("N") + "@library.example";
  var result = await HttpTests.Post(c, $"/Readers/Edit/{reader.Id}", new() { ["Name"] = "Updated", ["Email"] = email, ["Phone"] = "12345", ["Role"] = "Admin" }); Assert.AreEqual(HttpStatusCode.Redirect, result.StatusCode);
  StringAssert.Contains((await old.GetAsync("/MyLoans")).Headers.Location!.ToString(), "/Account/Login");
  using var updated = Client(); await HttpTests.Login(updated, email, env.Password); StringAssert.Contains(await updated.GetStringAsync("/MyLoans"), "You have no loan records yet."); StringAssert.Contains((await updated.GetAsync("/Readers")).Headers.Location!.ToString(), "/Account/Denied");
  using var stale = Client(); var login = await HttpTests.Post(stale, "/Account/Login", new() { ["Email"] = reader.Email, ["Password"] = env.Password }); Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
 }
 [TestMethod] public async Task Deleted_reader_cannot_reuse_existing_cookie_or_log_in() {
  var reader = await Reader(); using var c = await Admin(); using var active = Client(); await HttpTests.Login(active, reader.Email, env.Password); Assert.AreEqual(HttpStatusCode.OK, (await active.GetAsync("/MyLoans")).StatusCode);
  StringAssert.Contains(await c.GetStringAsync($"/Readers/Delete/{reader.Id}"), "Confirm delete"); Assert.AreEqual(HttpStatusCode.Redirect, (await HttpTests.Post(c, $"/Readers/Delete/{reader.Id}", new())).StatusCode);
  StringAssert.Contains((await active.GetAsync("/Books")).Headers.Location!.ToString(), "/Account/Login"); using var fresh = Client(); Assert.AreEqual(HttpStatusCode.OK, (await HttpTests.Post(fresh, "/Account/Login", new() { ["Email"] = reader.Email, ["Password"] = env.Password })).StatusCode);
 }
 [TestMethod] public async Task Reader_search_sorts_and_clears_keywords() {
  var key = Guid.NewGuid().ToString("N"); var first = await Reader(); var second = await Reader();
  await env.Use((s, _) => s.EditReaderAsync(first.Id, new() { Name = key + " A", Email = first.Email })); await env.Use((s, _) => s.EditReaderAsync(second.Id, new() { Name = key + " B", Email = second.Email }));
  using var c = await Admin(); var html = await c.GetStringAsync($"/Readers?q={key}&sort=name_desc"); Assert.IsTrue(html.IndexOf(key + " B", StringComparison.Ordinal) < html.IndexOf(key + " A", StringComparison.Ordinal));
  html = await c.GetStringAsync("/Readers?q=" + first.Email); StringAssert.Contains(html, key + " A"); Assert.IsFalse(html.Contains(key + " B")); StringAssert.Contains(await c.GetStringAsync("/Readers?q=missing-" + key), "No readers match your search."); StringAssert.Contains(await c.GetStringAsync("/Readers"), key + " B");
 }
 [DataTestMethod][DataRow("")][DataRow("bad-date")][DataRow("2999-01-01")]
 public async Task Return_invalid_or_missing_date_does_not_mutate(string date) {
  var reader = await Reader(); var loan = await Loan(await Book(), reader.Id); using var c = await Admin(); var result = await HttpTests.Post(c, $"/Loans/Return/{loan}", new() { ["ReturnDate"] = date }); Assert.AreEqual(HttpStatusCode.OK, result.StatusCode); Assert.IsNull(await env.Use((_, db) => db.Loans.Where(x => x.Id == loan).Select(x => x.ReturnDate).SingleAsync()));
 }
 [TestMethod] public async Task Return_success_and_repeated_request_are_visible_in_UI() {
  var reader = await Reader(); var book = await Book(); var loan = await Loan(book, reader.Id); using var c = await Admin(); var path = $"/Loans/Return/{loan}";
  Assert.AreEqual(HttpStatusCode.Redirect, (await HttpTests.Post(c, path, new() { ["ReturnDate"] = Today.ToString("yyyy-MM-dd") })).StatusCode);
  var repeat = await HttpTests.Post(c, path, new() { ["ReturnDate"] = Today.AddDays(-1).ToString("yyyy-MM-dd") }); Assert.AreEqual(HttpStatusCode.OK, repeat.StatusCode); StringAssert.Contains(await repeat.Content.ReadAsStringAsync(), "already been returned"); Assert.AreEqual(Today, await env.Use((_, db) => db.Loans.Where(x => x.Id == loan).Select(x => x.ReturnDate).SingleAsync()));
 }
 [TestMethod] public async Task My_loans_ignores_other_reader_IDs_and_hides_other_records() {
  var a = await Reader(); var b = await Reader(); var own = Guid.NewGuid().ToString("N"); var other = Guid.NewGuid().ToString("N"); var ownLoan = await Loan(await Book(own), a.Id, -1); var foreignLoan = await Loan(await Book(other), b.Id);
  using var c = Client(); await HttpTests.Login(c, a.Email, env.Password);
  foreach(var path in new[] { "/MyLoans", $"/MyLoans?readerId={b.Id}&userId={b.Id}&id={foreignLoan}", $"/MyLoans/Index/{foreignLoan}" }) { var html = await c.GetStringAsync(path); StringAssert.Contains(html, own); StringAssert.Contains(html, "Overdue"); Assert.IsFalse(html.Contains(other)); }
  Assert.AreEqual(HttpStatusCode.NotFound, (await c.GetAsync($"/MyLoans/Details/{foreignLoan}")).StatusCode);
  await env.Use((s, _) => s.ReturnAsync(ownLoan, new() { ReturnDate = Today })); var returned = await c.GetStringAsync("/MyLoans"); StringAssert.Contains(returned, "Returned"); Assert.IsFalse(returned.Contains("badge overdue"));
  using var second = Client(); await HttpTests.Login(second, b.Email, env.Password); var theirs = await second.GetStringAsync("/MyLoans"); StringAssert.Contains(theirs, other); Assert.IsFalse(theirs.Contains(own));
 }
 [TestMethod] public async Task Reader_with_no_profile_gets_safe_actionable_empty_state() {
  var email = Guid.NewGuid().ToString("N") + "@library.example"; using(var scope = env.Scope()) { var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>(); var user = new IdentityUser { UserName = email, Email = email }; Assert.IsTrue((await users.CreateAsync(user, env.Password)).Succeeded); Assert.IsTrue((await users.AddToRoleAsync(user, "Reader")).Succeeded); }
  using var c = Client(); await HttpTests.Login(c, email, env.Password); var result = await c.GetAsync("/MyLoans"); Assert.AreEqual(HttpStatusCode.Conflict, result.StatusCode); StringAssert.Contains(await result.Content.ReadAsStringAsync(), "not linked to an active reader profile"); Assert.IsFalse((await result.Content.ReadAsStringAsync()).Contains("data-loan-id"));
 }
 [DataTestMethod][DataRow("/Books/Edit/2147483647")][DataRow("/Books/Delete/2147483647")][DataRow("/Readers/Edit/2147483647")][DataRow("/Readers/Delete/2147483647")][DataRow("/Loans/Return/2147483647")]
 public async Task Invalid_management_ids_return_not_found_without_mutation(string path) {
  using var c = await Admin(); Assert.AreEqual(HttpStatusCode.NotFound, (await c.GetAsync(path)).StatusCode); var token = await HttpTests.Token(c, "/"); var result = await c.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = token })); Assert.AreEqual(HttpStatusCode.NotFound, result.StatusCode);
 }
 [TestMethod] public async Task Search_terms_are_encoded_and_security_headers_are_present() {
  using var c = await Admin(); var response = await c.GetAsync("/Books?q=%3Cscript%3Ealert(1)%3C%2Fscript%3E"); var html = await response.Content.ReadAsStringAsync(); Assert.IsFalse(html.Contains("<script>alert(1)</script>")); Assert.IsTrue(response.Headers.Contains("Content-Security-Policy")); Assert.IsTrue(response.Headers.CacheControl!.NoStore);
 }
}
