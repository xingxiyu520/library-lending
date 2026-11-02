using System.Net;
using System.Text.RegularExpressions;
using Library.Web.Data;
using Library.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Library.Tests;
/// <summary>维护、共享目录、个人借阅和会话撤销的HTTP集成回归。</summary>
[TestClass, TestCategory("Integration")]
public class Sprint2HttpTests
{
    /// <summary>共享的隔离测试环境，所有用例使用随机数据库及虚构账号。</summary>
    private TestEnvironment env = null !;
    /// <summary>按UTC+8计算的当前业务日期，作为日期校验和逾期状态基准。</summary>
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

    /// <summary>为当前测试配置共享隔离环境；浏览器测试还会创建独立无头会话。</summary>
    [TestInitialize]
    public void Initialize() => env = TestEnvironment.Current;
    /// <summary>创建不自动跟随跳转的独立客户端，便于断言登录与拒绝访问响应。</summary>
    private HttpClient Client() => env.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    /// <summary>使用管理员虚构账号创建已认证的HTTP客户端，供维护功能测试复用。</summary>
    private async Task<HttpClient> Admin()
    {
        var c = Client();
        await HttpTests.Login(c, "admin@library.example", env.Password);
        return c;
    }

    /// <summary>创建唯一虚构读者和关联账号，返回查询或登录所需标识。</summary>
    private async Task<(int Id, string Email)> Reader()
    {
        var email = Guid.NewGuid().ToString("N") + "@library.example";
        var result = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = "HTTP reader", Email = email, Password = env.Password }));
        Assert.IsTrue(result.Success, result.Error);
        return (result.Id!.Value, email);
    }

    /// <summary>以唯一ISBN准备当前测试所需馆藏，避免其他测试数据影响断言。</summary>
    private async Task<int> Book(string? title = null, int copies = 1, string author = "HTTP Author")
    {
        var result = await env.Use((s, _) => s.AddBookAsync(new() { Isbn = Guid.NewGuid().ToString("N"), Title = title ?? "HTTP book", Author = author, TotalCopies = copies }));
        Assert.IsTrue(result.Success, result.Error);
        return result.Id!.Value;
    }

    /// <summary>准备指定读者和图书的借阅，可按用例安排日期及归还状态。</summary>
    private async Task<int> Loan(int book, int reader, int due = 0)
    {
        var result = await env.Use((s, _) => s.LendAsync(new() { BookId = book, ReaderId = reader, BorrowDate = Today.AddDays(-3), DueDate = Today.AddDays(due), RequestId = Guid.NewGuid().ToString() }));
        Assert.IsTrue(result.Success, result.Error);
        return result.Id!.Value;
    }

    /// <summary>从目录行的data-book-id属性提取编号，断言实际结果顺序而非搜索框回显。</summary>
    private static int[] BookIds(string html) => Regex.Matches(html, "data-book-id=\"([0-9]+)\"").Select(x => int.Parse(x.Groups[1].Value)).ToArray();
    /// <summary>验证目录、个人借阅与维护新增地址都不能被匿名访问。</summary>
    [DataTestMethod]
    [DataRow("/Books")]
    [DataRow("/Books/Edit/1")]
    [DataRow("/Readers/Edit/1")]
    [DataRow("/Loans/Return/1")]
    [DataRow("/MyLoans")]
    public async Task New_protected_routes_require_login(string path)
    {
        using var c = Client();
        var response = await c.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        StringAssert.Contains(response.Headers.Location!.ToString(), "/Account/Login");
    }

    /// <summary>使用有效读者Cookie和令牌直接调用维护入口，核对被拒且数据不变。</summary>
    [DataTestMethod]
    [DataRow("/Books/Edit/1")]
    [DataRow("/Books/Delete/1")]
    [DataRow("/Readers/Edit/1")]
    [DataRow("/Readers/Delete/1")]
    [DataRow("/Loans/Return/1")]
    public async Task Reader_cannot_get_or_post_new_management_routes(string path)
    {
        using var c = Client();
        await HttpTests.Login(c, "reader@library.example", env.Password);
        var token = await HttpTests.Token(c, "/");
        var before = await env.Use(async (_, db) => (await db.Books.CountAsync(), await db.Readers.CountAsync(), await db.Loans.CountAsync(x => x.ReturnDate != null)));
        foreach (var response in new[]
        {
            await c.GetAsync(path),
            await c.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["ReturnDate"] = Today.ToString("yyyy-MM-dd"), ["Role"] = "Admin" }))
        }

        )
        {
            Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
            StringAssert.Contains(response.Headers.Location!.ToString(), "/Account/Denied");
        }

        Assert.AreEqual(before, await env.Use(async (_, db) => (await db.Books.CountAsync(), await db.Readers.CountAsync(), await db.Loans.CountAsync(x => x.ReturnDate != null))));
    }

    /// <summary>验证维护与归还新增写入口均要求合法防伪令牌。</summary>
    [DataTestMethod]
    [DataRow("/Books/Edit/1")]
    [DataRow("/Books/Delete/1")]
    [DataRow("/Readers/Edit/1")]
    [DataRow("/Readers/Delete/1")]
    [DataRow("/Loans/Return/1")]
    public async Task New_mutations_require_antiforgery(string path)
    {
        using var c = await Admin();
        var response = await c.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>核对读者目录可按书名、作者和ISBN匹配，但不提供管理按钮。</summary>
    [TestMethod]
    public async Task Reader_catalogue_is_read_only_and_searches_title_author_ISBN()
    {
        var key = Guid.NewGuid().ToString("N");
        var id = await Book(key + " title", author: key + " author");
        var isbn = await env.Use((_, db) => db.Books.Where(x => x.Id == id).Select(x => x.Isbn).SingleAsync());
        using var c = Client();
        await HttpTests.Login(c, "reader@library.example", env.Password);
        foreach (var term in new[]
        {
            "  " + key + " title  ",
            key + " author",
            isbn.Insert(4, "-")
        }

        )
        {
            var html = await c.GetStringAsync("/Books?q=" + Uri.EscapeDataString(term));
            CollectionAssert.AreEqual(new[] { id }, BookIds(html));
            Assert.IsFalse(html.Contains("/Books/Edit") || html.Contains("/Books/Delete") || html.Contains("/Books/Create"));
        }

        StringAssert.Contains(await c.GetStringAsync("/Books?q=missing-" + key), "No books match your search.");
        Assert.IsTrue(BookIds(await c.GetStringAsync("/Books?q=%20%20")).Contains(id));
    }

    /// <summary>组合目录关键词和四种排序，核对可借数以及相同键的稳定次序。</summary>
    [DataTestMethod]
    [DataRow("title")]
    [DataRow("title_desc")]
    [DataRow("available")]
    [DataRow("available_desc")]
    public async Task Catalogue_combines_search_sort_and_stable_ties(string sort)
    {
        var key = Guid.NewGuid().ToString("N");
        var a = await Book(key + " A", 1);
        var b = await Book(key + " B", 3);
        var cId = await Book(key + " A", 1);
        using var c = await Admin();
        var ids = BookIds(await c.GetStringAsync($"/Books?q={key}&sort={sort}"));
        var expected = sort is "title_desc" or "available_desc" ? new[]
        {
            b,
            a,
            cId
        }

        : new[]
        {
            a,
            cId,
            b
        };
        CollectionAssert.AreEqual(expected, ids);
        CollectionAssert.AreEqual(ids, BookIds(await c.GetStringAsync($"/Books?q={key}&sort={sort}")));
        CollectionAssert.AreEqual(new[] { a, cId, b }, BookIds(await c.GetStringAsync($"/Books?q={key}&sort=Title%3BDROP%20TABLE%20Books")));
    }

    /// <summary>核对图书删除确认页无副作用，只有合法POST才移除无历史图书。</summary>
    [TestMethod]
    public async Task Delete_confirmation_GET_does_not_mutate_and_POST_removes_book()
    {
        using var c = await Admin();
        var id = await Book();
        StringAssert.Contains(await c.GetStringAsync($"/Books/Delete/{id}"), "Confirm delete");
        Assert.IsTrue(await env.Use((_, db) => db.Books.AnyAsync(x => x.Id == id)));
        Assert.AreEqual(HttpStatusCode.Redirect, (await HttpTests.Post(c, $"/Books/Delete/{id}", new())).StatusCode);
        Assert.IsFalse(await env.Use((_, db) => db.Books.AnyAsync(x => x.Id == id)));
    }

    /// <summary>绕过浏览器提交非法总册数，核对服务端拒绝且原数据不变。</summary>
    [DataTestMethod]
    [DataRow("-1")]
    [DataRow("1.5")]
    [DataRow("abc")]
    [DataRow("")]
    public async Task Book_edit_invalid_copies_are_rejected_by_server(string copies)
    {
        var id = await Book();
        using var c = await Admin();
        var result = await HttpTests.Post(c, $"/Books/Edit/{id}", new() { ["Isbn"] = Guid.NewGuid().ToString("N"), ["Title"] = "Invalid", ["Author"] = "Author", ["TotalCopies"] = copies });
        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        Assert.AreEqual(1, await env.Use((_, db) => db.Books.Where(x => x.Id == id).Select(x => x.TotalCopies).SingleAsync()));
    }

    /// <summary>修改读者邮箱后新邮箱可登录、旧会话撤销，Role字段注入不能提权。</summary>
    [TestMethod]
    public async Task Edited_reader_login_uses_new_email_old_session_is_revoked_and_role_injection_fails()
    {
        var reader = await Reader();
        using var c = await Admin();
        using var old = Client();
        await HttpTests.Login(old, reader.Email, env.Password);
        var email = Guid.NewGuid().ToString("N") + "@library.example";
        var result = await HttpTests.Post(c, $"/Readers/Edit/{reader.Id}", new() { ["Name"] = "Updated", ["Email"] = email, ["Phone"] = "12345", ["Role"] = "Admin" });
        Assert.AreEqual(HttpStatusCode.Redirect, result.StatusCode);
        StringAssert.Contains((await old.GetAsync("/MyLoans")).Headers.Location!.ToString(), "/Account/Login");
        using var updated = Client();
        await HttpTests.Login(updated, email, env.Password);
        StringAssert.Contains(await updated.GetStringAsync("/MyLoans"), "You have no loan records yet.");
        StringAssert.Contains((await updated.GetAsync("/Readers")).Headers.Location!.ToString(), "/Account/Denied");
        using var stale = Client();
        var login = await HttpTests.Post(stale, "/Account/Login", new() { ["Email"] = reader.Email, ["Password"] = env.Password });
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
    }

    /// <summary>验证删除读者后旧Cookie立即失效，原账号也不能再次登录。</summary>
    [TestMethod]
    public async Task Deleted_reader_cannot_reuse_existing_cookie_or_log_in()
    {
        var reader = await Reader();
        using var c = await Admin();
        using var active = Client();
        await HttpTests.Login(active, reader.Email, env.Password);
        Assert.AreEqual(HttpStatusCode.OK, (await active.GetAsync("/MyLoans")).StatusCode);
        StringAssert.Contains(await c.GetStringAsync($"/Readers/Delete/{reader.Id}"), "Confirm delete");
        Assert.AreEqual(HttpStatusCode.Redirect, (await HttpTests.Post(c, $"/Readers/Delete/{reader.Id}", new())).StatusCode);
        StringAssert.Contains((await active.GetAsync("/Books")).Headers.Location!.ToString(), "/Account/Login");
        using var fresh = Client();
        Assert.AreEqual(HttpStatusCode.OK, (await HttpTests.Post(fresh, "/Account/Login", new() { ["Email"] = reader.Email, ["Password"] = env.Password })).StatusCode);
    }

    /// <summary>验证管理员按读者姓名或邮箱搜索、姓名双向排序和清空。</summary>
    [TestMethod]
    public async Task Reader_search_sorts_and_clears_keywords()
    {
        var key = Guid.NewGuid().ToString("N");
        var first = await Reader();
        var second = await Reader();
        await env.Use((s, _) => s.EditReaderAsync(first.Id, new() { Name = key + " A", Email = first.Email }));
        await env.Use((s, _) => s.EditReaderAsync(second.Id, new() { Name = key + " B", Email = second.Email }));
        using var c = await Admin();
        var html = await c.GetStringAsync($"/Readers?q={key}&sort=name_desc");
        Assert.IsTrue(html.IndexOf(key + " B", StringComparison.Ordinal) < html.IndexOf(key + " A", StringComparison.Ordinal));
        html = await c.GetStringAsync("/Readers?q=" + first.Email);
        StringAssert.Contains(html, key + " A");
        Assert.IsFalse(html.Contains(key + " B"));
        StringAssert.Contains(await c.GetStringAsync("/Readers?q=missing-" + key), "No readers match your search.");
        StringAssert.Contains(await c.GetStringAsync("/Readers"), key + " B");
    }

    /// <summary>通过HTTP提交空、非法和未来归还日，核对状态与日期未修改。</summary>
    [DataTestMethod]
    [DataRow("")]
    [DataRow("bad-date")]
    [DataRow("2999-01-01")]
    public async Task Return_invalid_or_missing_date_does_not_mutate(string date)
    {
        var reader = await Reader();
        var loan = await Loan(await Book(), reader.Id);
        using var c = await Admin();
        var result = await HttpTests.Post(c, $"/Loans/Return/{loan}", new() { ["ReturnDate"] = date });
        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        Assert.IsNull(await env.Use((_, db) => db.Loans.Where(x => x.Id == loan).Select(x => x.ReturnDate).SingleAsync()));
    }

    /// <summary>HTTP归还成功后再次提交，验证页面提示重复且保存日期不被覆盖。</summary>
    [TestMethod]
    public async Task Return_success_and_repeated_request_are_visible_in_UI()
    {
        var reader = await Reader();
        var book = await Book();
        var loan = await Loan(book, reader.Id);
        using var c = await Admin();
        var path = $"/Loans/Return/{loan}";
        Assert.AreEqual(HttpStatusCode.Redirect, (await HttpTests.Post(c, path, new() { ["ReturnDate"] = Today.ToString("yyyy-MM-dd") })).StatusCode);
        var repeat = await HttpTests.Post(c, path, new() { ["ReturnDate"] = Today.AddDays(-1).ToString("yyyy-MM-dd") });
        Assert.AreEqual(HttpStatusCode.OK, repeat.StatusCode);
        StringAssert.Contains(await repeat.Content.ReadAsStringAsync(), "already been returned");
        Assert.AreEqual(Today, await env.Use((_, db) => db.Loans.Where(x => x.Id == loan).Select(x => x.ReturnDate).SingleAsync()));
    }

    /// <summary>以两个读者验证query与route编号都不能替换登录身份，只显示本人历史。</summary>
    [TestMethod]
    public async Task My_loans_ignores_other_reader_IDs_and_hides_other_records()
    {
        var a = await Reader();
        var b = await Reader();
        var own = Guid.NewGuid().ToString("N");
        var other = Guid.NewGuid().ToString("N");
        var ownLoan = await Loan(await Book(own), a.Id, -1);
        var foreignLoan = await Loan(await Book(other), b.Id);
        using var c = Client();
        await HttpTests.Login(c, a.Email, env.Password);
        foreach (var path in new[]
        {
            "/MyLoans",
            $"/MyLoans?readerId={b.Id}&userId={b.Id}&id={foreignLoan}",
            $"/MyLoans/Index/{foreignLoan}"}

        )
        {
            var html = await c.GetStringAsync(path);
            StringAssert.Contains(html, own);
            StringAssert.Contains(html, "Overdue");
            Assert.IsFalse(html.Contains(other));
        }

        Assert.AreEqual(HttpStatusCode.NotFound, (await c.GetAsync($"/MyLoans/Details/{foreignLoan}")).StatusCode);
        await env.Use((s, _) => s.ReturnAsync(ownLoan, new() { ReturnDate = Today }));
        var returned = await c.GetStringAsync("/MyLoans");
        StringAssert.Contains(returned, "Returned");
        Assert.IsFalse(returned.Contains("badge overdue"));
        using var second = Client();
        await HttpTests.Login(second, b.Email, env.Password);
        var theirs = await second.GetStringAsync("/MyLoans");
        StringAssert.Contains(theirs, other);
        Assert.IsFalse(theirs.Contains(own));
    }

    /// <summary>为无档案账号请求个人借阅，返回409和联系提示，不退回全量数据。</summary>
    [TestMethod]
    public async Task Reader_with_no_profile_gets_safe_actionable_empty_state()
    {
        var email = Guid.NewGuid().ToString("N") + "@library.example";
        using (var scope = env.Scope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser
            {
                UserName = email,
                Email = email
            };
            Assert.IsTrue((await users.CreateAsync(user, env.Password)).Succeeded);
            Assert.IsTrue((await users.AddToRoleAsync(user, "Reader")).Succeeded);
        }

        using var c = Client();
        await HttpTests.Login(c, email, env.Password);
        var result = await c.GetAsync("/MyLoans");
        Assert.AreEqual(HttpStatusCode.Conflict, result.StatusCode);
        StringAssert.Contains(await result.Content.ReadAsStringAsync(), "not linked to an active reader profile");
        Assert.IsFalse((await result.Content.ReadAsStringAsync()).Contains("data-loan-id"));
    }

    /// <summary>验证维护请求使用不存在编号时返回404且不产生修改。</summary>
    [DataTestMethod]
    [DataRow("/Books/Edit/2147483647")]
    [DataRow("/Books/Delete/2147483647")]
    [DataRow("/Readers/Edit/2147483647")]
    [DataRow("/Readers/Delete/2147483647")]
    [DataRow("/Loans/Return/2147483647")]
    public async Task Invalid_management_ids_return_not_found_without_mutation(string path)
    {
        using var c = await Admin();
        Assert.AreEqual(HttpStatusCode.NotFound, (await c.GetAsync(path)).StatusCode);
        var token = await HttpTests.Token(c, "/");
        var result = await c.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.AreEqual(HttpStatusCode.NotFound, result.StatusCode);
    }

    /// <summary>验证搜索框回显进行HTML编码，并检查缓存与内容安全响应头。</summary>
    [TestMethod]
    public async Task Search_terms_are_encoded_and_security_headers_are_present()
    {
        using var c = await Admin();
        var response = await c.GetAsync("/Books?q=%3Cscript%3Ealert(1)%3C%2Fscript%3E");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(html.Contains("<script>alert(1)</script>"));
        Assert.IsTrue(response.Headers.Contains("Content-Security-Policy"));
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
    }
}
