using System.Net;
using System.Text.RegularExpressions;
using Library.Web.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Library.Tests;
[TestClass, TestCategory("Integration")]
public class HttpTests {
 private TestEnvironment env = null!;
 [TestInitialize] public void Initialize() => env = TestEnvironment.Current;
 private HttpClient Client() => env.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
 internal static async Task<string> Token(HttpClient client, string page) {
  var html = await client.GetStringAsync(page);
  var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
  Assert.IsTrue(match.Success, "Antiforgery token must be present."); return WebUtility.HtmlDecode(match.Groups[1].Value);
 }
 internal static async Task<HttpResponseMessage> Post(HttpClient client, string path, Dictionary<string,string> fields) {
  fields["__RequestVerificationToken"] = await Token(client, path); return await client.PostAsync(path, new FormUrlEncodedContent(fields));
 }
 internal static async Task Login(HttpClient client, string email, string password) {
  var response = await Post(client, "/Account/Login", new() { ["Email"] = email, ["Password"] = password });
  Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
 }
 [DataTestMethod][DataRow("/")][DataRow("/Books")][DataRow("/Readers")][DataRow("/Loans/Create")]
 public async Task Anonymous_requests_require_login(string path) {
  using var client = Client(); var response = await client.GetAsync(path); Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode); StringAssert.Contains(response.Headers.Location!.ToString(), "/Account/Login");
 }
 [DataTestMethod][DataRow("admin@library.example", "ADMIN")][DataRow("reader@library.example", "READER")]
 public async Task Roles_get_their_own_homepage(string email, string role) {
  using var client = Client(); await Login(client, email, env.Password); var html = await client.GetStringAsync("/"); StringAssert.Contains(html, role); Assert.IsFalse(html.Contains(env.Password));
 }
 [TestMethod] public async Task Invalid_login_uses_generic_message_and_hides_password() {
  using var client = Client(); var response = await Post(client, "/Account/Login", new() { ["Email"] = "missing@library.example", ["Password"] = "WrongSecret9" });
  Assert.AreEqual(HttpStatusCode.OK, response.StatusCode); var html = await response.Content.ReadAsStringAsync(); StringAssert.Contains(html, "Unable to sign in with these credentials."); Assert.IsFalse(html.Contains("WrongSecret9"));
 }
 [TestMethod] public async Task Empty_login_is_rejected() {
  using var client = Client(); var response = await Post(client, "/Account/Login", new() { ["Email"] = "", ["Password"] = "" });
  StringAssert.Contains(await response.Content.ReadAsStringAsync(), "field is required"); Assert.AreEqual(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
 }
 [TestMethod] public async Task Logout_invalidates_session_and_requires_post() {
  using var client = Client(); await Login(client, "admin@library.example", env.Password);
  var get = await client.GetAsync("/Account/Logout"); Assert.AreEqual(HttpStatusCode.MethodNotAllowed, get.StatusCode);
  var token = await Token(client, "/"); var response = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token}}));
  Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode); StringAssert.Contains((await client.GetAsync("/Books")).Headers.Location!.ToString(), "/Account/Login");
 }
 [DataTestMethod][DataRow("/Books")][DataRow("/Readers")][DataRow("/Loans")][DataRow("/Books/Create")][DataRow("/Readers/Create")][DataRow("/Loans/Create")]
 public async Task Reader_cannot_open_admin_pages(string path) {
  using var client = Client(); await Login(client, "reader@library.example", env.Password); var response = await client.GetAsync(path);
  Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode); StringAssert.Contains(response.Headers.Location!.ToString(), "/Account/Denied"); var denied=await client.GetAsync(response.Headers.Location); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
 }
 [DataTestMethod][DataRow("/Books/Create")][DataRow("/Readers/Create")][DataRow("/Loans/Create")]
 public async Task Reader_direct_post_is_denied_before_mutation(string path) {
  using var client = Client(); await Login(client, "reader@library.example", env.Password);
  var token = await Token(client, "/");
  var before = await env.Use(async (_,db) => (await db.Books.CountAsync(), await db.Readers.CountAsync(), await db.Loans.CountAsync()));
  var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token},{"Title","Forbidden"},{"Role","Admin"}}));
  Assert.AreEqual(HttpStatusCode.Redirect,response.StatusCode); StringAssert.Contains(response.Headers.Location!.ToString(), "/Account/Denied");
  var after = await env.Use(async (_,db) => (await db.Books.CountAsync(), await db.Readers.CountAsync(), await db.Loans.CountAsync())); Assert.AreEqual(before,after);
 }
 [TestMethod] public async Task Missing_antiforgery_token_is_rejected() {
  using var client = Client(); await Login(client, "admin@library.example", env.Password);
  var response = await client.PostAsync("/Books/Create", new FormUrlEncodedContent(new Dictionary<string,string>{{"Isbn","csrf"},{"Title","Bad"},{"Author","Bad"},{"TotalCopies","1"}})); Assert.AreEqual(HttpStatusCode.BadRequest,response.StatusCode);
 }
 [TestMethod] public async Task External_return_url_cannot_redirect_off_site() {
  using var client=Client(); var response=await Post(client,"/Account/Login",new(){["Email"]="admin@library.example",["Password"]=env.Password,["ReturnUrl"]="https://example.org/phishing"}); Assert.AreEqual("/",response.Headers.Location!.ToString());
 }
 [DataTestMethod][DataRow("-1")][DataRow("1.5")][DataRow("abc")][DataRow("")]
 public async Task Invalid_copy_inputs_are_rejected_by_server(string copies) {
  using var client=Client(); await Login(client,"admin@library.example",env.Password);
  var isbn=Guid.NewGuid().ToString("N"); var response=await Post(client,"/Books/Create",new(){["Isbn"]=isbn,["Title"]="Boundary",["Author"]="Author",["TotalCopies"]=copies}); Assert.AreEqual(HttpStatusCode.OK,response.StatusCode);
  Assert.IsFalse(await env.Use((_,db)=>db.Books.AnyAsync(x=>x.Isbn==isbn)));
 }
 [TestMethod] public async Task Reader_role_injection_is_ignored_and_new_reader_can_login() {
  using var client=Client(); await Login(client,"admin@library.example",env.Password); var email="reader"+Guid.NewGuid().ToString("N")+"@library.example";
  var response=await Post(client,"/Readers/Create",new(){["Name"]="New Reader",["Email"]=email,["Password"]=env.Password,["Role"]="Admin"}); Assert.AreEqual(HttpStatusCode.Redirect,response.StatusCode);
  using var reader=Client(); await Login(reader,email,env.Password); StringAssert.Contains(await reader.GetStringAsync("/"),"READER WORKSPACE");
  StringAssert.Contains((await reader.GetAsync("/Books")).Headers.Location!.ToString(),"/Account/Denied");
 }
 [TestMethod] public async Task Missing_loan_submission_token_is_rejected() {
  using var client=Client();await Login(client,"admin@library.example",env.Password);
  var book=await env.Use((s,_)=>s.AddBookAsync(new(){Isbn=Guid.NewGuid().ToString("N"),Title="Token test",Author="Author",TotalCopies=1}));
  var reader=await env.Use((_,db)=>db.Readers.Select(x=>x.Id).FirstAsync());var today=DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd");
  var response=await Post(client,"/Loans/Create",new(){["BookId"]=book.Id!.Value.ToString(),["ReaderId"]=reader.ToString(),["BorrowDate"]=today,["DueDate"]=today});
  Assert.AreEqual(HttpStatusCode.OK,response.StatusCode);Assert.IsFalse(await env.Use((_,db)=>db.Loans.AnyAsync(x=>x.BookId==book.Id)));
 }
 [TestMethod] public async Task Book_title_is_HTML_encoded() {
  using var client=Client(); await Login(client,"admin@library.example",env.Password);
  var response=await Post(client,"/Books/Create",new(){["Isbn"]=Guid.NewGuid().ToString("N"),["Title"]="<script>alert(1)</script>",["Author"]="Author",["TotalCopies"]="1"}); Assert.AreEqual(HttpStatusCode.Redirect,response.StatusCode);
  var html=await client.GetStringAsync("/Books"); StringAssert.Contains(html,"&lt;script&gt;"); Assert.IsFalse(html.Contains("<script>alert(1)</script>"));
 }
}
