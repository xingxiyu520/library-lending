using Library.Web.Data;
using Library.Web.Models;
using Library.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
namespace Library.Tests;
[TestClass, TestCategory("MySQL")]
public class DatabaseTests {
 private TestEnvironment env=null!;
 [TestInitialize] public void Initialize()=>env=TestEnvironment.Current;
 private async Task<int> Book(int copies=1) {
  var result=await env.Use((service,_)=>service.AddBookAsync(new BookInput{Isbn=Guid.NewGuid().ToString("N"),Title="Database book",Author="Test Author",TotalCopies=copies})); Assert.IsTrue(result.Success,result.Error);return result.Id!.Value;
 }
 private async Task<int> Reader()=>await env.Use((_,db)=>db.Readers.OrderBy(x=>x.Id).Select(x=>x.Id).FirstAsync());
 private LoanInput Loan(int book,int reader,string? request=null)=>new(){BookId=book,ReaderId=reader,BorrowDate=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)),DueDate=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).AddDays(14),RequestId=request??Guid.NewGuid().ToString()};
 [TestMethod] public async Task Duplicate_normalized_ISBN_is_rejected() {
  var isbn=Guid.NewGuid().ToString("N")[..20];var first=await env.Use((s,_)=>s.AddBookAsync(new(){Isbn=isbn,Title="One",Author="A",TotalCopies=0}));
  var second=await env.Use((s,_)=>s.AddBookAsync(new(){Isbn=isbn.ToUpperInvariant().Insert(4,"-"),Title="Two",Author="B",TotalCopies=1}));Assert.IsTrue(first.Success);Assert.IsFalse(second.Success);StringAssert.Contains(second.Error!,"ISBN already exists");
 }
 [TestMethod] public async Task Concurrent_duplicate_ISBN_has_one_winner() {
  var isbn=Guid.NewGuid().ToString("N");var results=await Task.WhenAll(Enumerable.Range(0,2).Select(_=>env.Use((s,db)=>s.AddBookAsync(new(){Isbn=isbn,Title="Race",Author="A",TotalCopies=1}))));Assert.AreEqual(1,results.Count(x=>x.Success));
 }
 [TestMethod] public async Task New_reader_has_hashed_password_and_only_reader_role() {
  var email="new"+Guid.NewGuid().ToString("N")+"@library.example";var result=await env.Use((s,_)=>s.CreateReaderAsync(new(){Name="New reader",Email=email,Password=env.Password}));Assert.IsTrue(result.Success,result.Error);
  using var scope=env.Scope();var users=scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();var user=await users.FindByEmailAsync(email);Assert.IsNotNull(user);Assert.AreNotEqual(env.Password,user.PasswordHash);Assert.IsTrue(await users.CheckPasswordAsync(user,env.Password));CollectionAssert.AreEquivalent(new[]{"Reader"},(await users.GetRolesAsync(user)).ToArray());
 }
 [TestMethod] public async Task Email_uniqueness_is_case_insensitive() {
  var result=await env.Use((s,_)=>s.CreateReaderAsync(new(){Name="Duplicate",Email="READER@LIBRARY.EXAMPLE",Password=env.Password}));Assert.IsFalse(result.Success);
 }
 [TestMethod] public async Task Invalid_password_leaves_no_account_or_reader() {
  var email="bad"+Guid.NewGuid().ToString("N")+"@library.example";var result=await env.Use((s,_)=>s.CreateReaderAsync(new(){Name="Bad",Email=email,Password="short"}));Assert.IsFalse(result.Success);
  Assert.IsFalse(await env.Use((_,db)=>db.Users.AnyAsync(x=>x.Email==email)));Assert.IsFalse(await env.Use((_,db)=>db.Readers.AnyAsync(x=>x.Email==email)));
 }
 [TestMethod] public async Task Reader_save_failure_rolls_back_account_and_role() {
  var email="rollback"+Guid.NewGuid().ToString("N")+"@library.example";
  using(var provider=FailureProvider(new FailReaderSave())) {using var scope=provider.CreateScope();await Assert.ThrowsExceptionAsync<DbUpdateException>(()=>scope.ServiceProvider.GetRequiredService<LibraryService>().CreateReaderAsync(new(){Name="Rollback",Email=email,Password=env.Password}));}
  Assert.IsFalse(await env.Use((_,db)=>db.Users.AnyAsync(x=>x.Email==email)));Assert.IsFalse(await env.Use((_,db)=>db.Readers.AnyAsync(x=>x.Email==email)));
 }
 [TestMethod] public async Task Role_save_failure_rolls_back_new_account() {
  var email="rolefail"+Guid.NewGuid().ToString("N")+"@library.example";
  using(var provider=FailureProvider(new FailRoleSave())) {using var scope=provider.CreateScope();await Assert.ThrowsExceptionAsync<DbUpdateException>(()=>scope.ServiceProvider.GetRequiredService<LibraryService>().CreateReaderAsync(new(){Name="Rollback",Email=email,Password=env.Password}));}
  Assert.IsFalse(await env.Use((_,db)=>db.Users.AnyAsync(x=>x.Email==email)));
 }
 [TestMethod] public async Task Valid_loan_is_persisted_and_reduces_availability() {
  var book=await Book(2);var reader=await Reader();var result=await env.Use((s,_)=>s.LendAsync(Loan(book,reader)));Assert.IsTrue(result.Success,result.Error);
  var outstanding=await env.Use((_,db)=>db.Loans.CountAsync(x=>x.BookId==book&&x.ReturnDate==null));Assert.AreEqual(1,outstanding);
  using var scope=env.Scope();var db=scope.ServiceProvider.GetRequiredService<LibraryDbContext>();var saved=await db.Loans.SingleAsync(x=>x.Id==result.Id);Assert.AreEqual(reader,saved.ReaderId);Assert.IsNull(saved.ReturnDate);
 }
 [TestMethod] public async Task No_stock_rejects_loan_without_side_effects() {
  var book=await Book(0);var reader=await Reader();var result=await env.Use((s,_)=>s.LendAsync(Loan(book,reader)));Assert.IsFalse(result.Success);Assert.AreEqual(0,await env.Use((_,db)=>db.Loans.CountAsync(x=>x.BookId==book)));
 }
 [TestMethod] public async Task Concurrent_last_copy_allows_exactly_one_loan() {
  var book=await Book();var reader=await Reader();var results=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>env.Use((s,db)=>s.LendAsync(Loan(book,reader)))));Assert.AreEqual(1,results.Count(x=>x.Success));Assert.AreEqual(1,await env.Use((_,db)=>db.Loans.CountAsync(x=>x.BookId==book)));
 }
 [TestMethod] public async Task Replayed_submission_returns_same_loan_without_consuming_another_copy() {
  var book=await Book(2);var reader=await Reader();var input=Loan(book,reader);var first=await env.Use((s,_)=>s.LendAsync(input));var second=await env.Use((s,_)=>s.LendAsync(input));Assert.IsTrue(first.Success);Assert.IsTrue(second.Success);Assert.AreEqual(first.Id,second.Id);Assert.AreEqual(1,await env.Use((_,db)=>db.Loans.CountAsync(x=>x.BookId==book)));
 }
 [TestMethod] public async Task Simultaneous_replay_has_one_persisted_loan() {
  var book=await Book(2);var reader=await Reader();var input=Loan(book,reader);var results=await Task.WhenAll(Enumerable.Range(0,3).Select(_=>env.Use((s,db)=>s.LendAsync(input))));Assert.IsTrue(results.All(x=>x.Success));Assert.AreEqual(1,results.Select(x=>x.Id).Distinct().Count());
 }
 [TestMethod] public async Task Reused_token_with_changed_payload_is_rejected() {
  var book=await Book(2);var reader=await Reader();var input=Loan(book,reader);Assert.IsTrue((await env.Use((s,_)=>s.LendAsync(input))).Success);input.DueDate=input.DueDate!.Value.AddDays(1);Assert.IsFalse((await env.Use((s,_)=>s.LendAsync(input))).Success);
 }
 [TestMethod] public async Task Fresh_submission_can_borrow_another_copy_of_same_title() {
  var book=await Book(2);var reader=await Reader();Assert.IsTrue((await env.Use((s,_)=>s.LendAsync(Loan(book,reader)))).Success);Assert.IsTrue((await env.Use((s,_)=>s.LendAsync(Loan(book,reader)))).Success);Assert.AreEqual(2,await env.Use((_,db)=>db.Loans.CountAsync(x=>x.BookId==book)));
 }
 [TestMethod] public async Task Invalid_foreign_keys_are_rejected() {
  var book=await Book();var reader=await Reader();Assert.IsFalse((await env.Use((s,_)=>s.LendAsync(Loan(int.MaxValue,reader)))).Success);Assert.IsFalse((await env.Use((s,_)=>s.LendAsync(Loan(book,int.MaxValue)))).Success);Assert.AreEqual(0,await env.Use((_,db)=>db.Loans.CountAsync(x=>x.BookId==book)));
 }
 [TestMethod] public async Task Invalid_dates_do_not_create_a_loan() {
  var book=await Book();var input=Loan(book,await Reader());input.DueDate=input.BorrowDate!.Value.AddDays(-1);Assert.IsFalse((await env.Use((s,_)=>s.LendAsync(input))).Success);input.BorrowDate=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).AddDays(1);input.DueDate=input.BorrowDate;Assert.IsFalse((await env.Use((s,_)=>s.LendAsync(input))).Success);
 }
 [TestMethod] public async Task Database_constraint_blocks_negative_copies() {
  using var scope=env.Scope();var db=scope.ServiceProvider.GetRequiredService<LibraryDbContext>();db.Books.Add(new(){Isbn=Guid.NewGuid().ToString("N"),Title="Invalid",Author="Author",TotalCopies=-1});await Assert.ThrowsExceptionAsync<DbUpdateException>(()=>db.SaveChangesAsync());
 }
 private ServiceProvider FailureProvider(SaveChangesInterceptor interceptor) {
  var services=new ServiceCollection();services.AddLogging();services.AddDbContext<LibraryDbContext>(o=>o.UseMySql(env.Connection,new MySqlServerVersion(new Version(8,4,0))).AddInterceptors(interceptor));
  services.AddIdentityCore<IdentityUser>(o=>{o.Password.RequiredLength=8;o.Password.RequireNonAlphanumeric=false;o.Password.RequireUppercase=false;}).AddRoles<IdentityRole>().AddEntityFrameworkStores<LibraryDbContext>();services.AddSingleton(TimeProvider.System);services.AddScoped<BusinessClock>();services.AddScoped<LibraryService>();return services.BuildServiceProvider();
 }
 private sealed class FailReaderSave:SaveChangesInterceptor {
  public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken cancellationToken=default) {
   if(data.Context!.ChangeTracker.Entries<Reader>().Any(x=>x.State==EntityState.Added))throw new DbUpdateException("Injected reader persistence failure.");return ValueTask.FromResult(result);
  }
 }
 private sealed class FailRoleSave:SaveChangesInterceptor {
  public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken cancellationToken=default) {
   if(data.Context!.ChangeTracker.Entries<IdentityUserRole<string>>().Any(x=>x.State==EntityState.Added))throw new DbUpdateException("Injected role persistence failure.");return ValueTask.FromResult(result);
  }
 }
}
