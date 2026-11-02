using System.Net;
using System.Text.RegularExpressions;
using Library.Web.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Library.Tests;
// 通过真实身份Cookie和防伪令牌验证入口边界、字段绑定以及组合查询的归属限制。
/// <summary>验证更正、删除、组合筛选、稳定排序及读者身份隔离的HTTP回归。</summary>
[TestClass, TestCategory("Integration")]
public class Sprint3HttpTests
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
    /// <summary>为指定虚构用户创建独立HTTP客户端，避免不同读者共用Cookie。</summary>
    private async Task<HttpClient> Login(string email = "admin@library.example")
    {
        var c = Client();
        await HttpTests.Login(c, email, env.Password);
        return c;
    }

    /// <summary>创建唯一虚构读者和关联账号，返回查询或登录所需标识。</summary>
    private async Task<(int Id, string Email, string Name)> Reader(string? name = null)
    {
        var key = Guid.NewGuid().ToString("N");
        var email = key + "@library.example";
        name ??= "Query reader " + key;
        var result = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = name, Email = email, Password = env.Password }));
        Assert.IsTrue(result.Success, result.Error);
        return (result.Id!.Value, email, name);
    }

    /// <summary>准备指定读者和图书的借阅，可按用例安排日期及归还状态。</summary>
    private async Task<int> Loan(int reader, string title, int borrow = -4, int due = 2, bool returned = false)
    {
        var book = await env.Use((s, _) => s.AddBookAsync(new() { Isbn = Guid.NewGuid().ToString("N"), Title = title, Author = "Query author", TotalCopies = 1 }));
        Assert.IsTrue(book.Success);
        var result = await env.Use((s, _) => s.LendAsync(new() { BookId = book.Id, ReaderId = reader, BorrowDate = Today.AddDays(borrow), DueDate = Today.AddDays(due), RequestId = Guid.NewGuid().ToString() }));
        Assert.IsTrue(result.Success, result.Error);
        if (returned)
            Assert.IsTrue((await env.Use((s, _) => s.ReturnAsync(result.Id!.Value, new() { ReturnDate = Today }))).Success);
        return result.Id!.Value;
    }

    // 从有权访问的页面取令牌，便于独立测试目标路径本身的鉴权和404行为。
    /// <summary>附带合法防伪令牌向目标入口提交表单，使权限与业务断言不被缺失令牌干扰。</summary>
    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, Dictionary<string, string> fields, string tokenPage = "/Loans")
    {
        fields["__RequestVerificationToken"] = await HttpTests.Token(client, tokenPage);
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    /// <summary>从借阅行的data-loan-id属性提取编号，验证筛选、排序和归属边界。</summary>
    private static int[] Ids(string html) => Regex.Matches(html, "data-loan-id=\"([0-9]+)\"").Select(x => int.Parse(x.Groups[1].Value)).ToArray();
    /// <summary>验证匿名无法访问借阅更正和误录删除入口。</summary>
    [DataTestMethod]
    [DataRow("/Loans/Edit/1")]
    [DataRow("/Loans/Delete/1")]
    public async Task Correction_and_deletion_require_login(string path)
    {
        using var c = Client();
        Assert.AreEqual(HttpStatusCode.Redirect, (await c.GetAsync(path)).StatusCode);
        StringAssert.Contains((await c.GetAsync(path)).Headers.Location!.ToString(), "/Account/Login");
    }

    /// <summary>读者即使拥有该借阅，也不能直接GET或POST更正和删除。</summary>
    [DataTestMethod]
    [DataRow("Edit")]
    [DataRow("Delete")]
    public async Task Reader_cannot_get_or_post_even_own_loan_mutations(string action)
    {
        var reader = await Reader();
        var loan = await Loan(reader.Id, "Own loan");
        using var c = await Login(reader.Email);
        var path = $"/Loans/{action}/{loan}";
        foreach (var response in new[]
        {
            await c.GetAsync(path),
            await Post(c, path, new() { ["BorrowDate"] = Today.ToString("yyyy-MM-dd"), ["DueDate"] = Today.ToString("yyyy-MM-dd") }, "/MyLoans")
        }

        )
        {
            Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
            StringAssert.Contains(response.Headers.Location!.ToString(), "/Account/Denied");
        }

        Assert.IsTrue(await env.Use((_, db) => db.Loans.AnyAsync(x => x.Id == loan)));
    }

    /// <summary>对借阅更正与删除发送无令牌POST，验证返回400。</summary>
    [DataTestMethod]
    [DataRow("Edit")]
    [DataRow("Delete")]
    public async Task Loan_mutations_reject_missing_antiforgery_token(string action)
    {
        using var c = await Login();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await c.PostAsync($"/Loans/{action}/1", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
    }

    /// <summary>对不存在的借阅编号执行更正或删除，GET与POST均返回404。</summary>
    [DataTestMethod]
    [DataRow("Edit")]
    [DataRow("Delete")]
    public async Task Invalid_loan_ids_return_404(string action)
    {
        using var c = await Login();
        var path = $"/Loans/{action}/{int.MaxValue}";
        Assert.AreEqual(HttpStatusCode.NotFound, (await c.GetAsync(path)).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Post(c, path, new(), "/Loans")).StatusCode);
    }

    /// <summary>在更正表单注入图书、读者、归还与角色字段，核对关联和状态不被篡改。</summary>
    [TestMethod]
    public async Task Correction_ignores_posted_book_reader_return_and_role_fields()
    {
        var reader = await Reader();
        var loan = await Loan(reader.Id, "Original association");
        var original = await env.Use((_, db) => db.Loans.AsNoTracking().SingleAsync(x => x.Id == loan));
        using var c = await Login();
        var response = await Post(c, $"/Loans/Edit/{loan}", new() { ["BorrowDate"] = Today.AddDays(-2).ToString("yyyy-MM-dd"), ["DueDate"] = Today.AddDays(5).ToString("yyyy-MM-dd"), ["BookId"] = "2147483647", ["ReaderId"] = "2147483647", ["ReturnDate"] = Today.ToString("yyyy-MM-dd"), ["Role"] = "Admin" });
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        var saved = await env.Use((_, db) => db.Loans.SingleAsync(x => x.Id == loan));
        Assert.AreEqual(Today.AddDays(-2), saved.BorrowDate);
        Assert.AreEqual(original.BookId, saved.BookId);
        Assert.AreEqual(original.ReaderId, saved.ReaderId);
        Assert.IsNull(saved.ReturnDate);
        Assert.AreEqual(original.RequestId, saved.RequestId);
    }

    /// <summary>通过HTTP提交空值、非日期或未来借出日，核对更正失败时原记录不变。</summary>
    [DataTestMethod]
    [DataRow("", "2026-11-02")]
    [DataRow("bad-date", "bad-date")]
    [DataRow("2999-01-01", "2999-01-02")]
    public async Task Invalid_correction_dates_preserve_existing_record(string borrow, string due)
    {
        var reader = await Reader();
        var loan = await Loan(reader.Id, "Invalid dates");
        using var c = await Login();
        Assert.AreEqual(HttpStatusCode.OK, (await Post(c, $"/Loans/Edit/{loan}", new() { ["BorrowDate"] = borrow, ["DueDate"] = due })).StatusCode);
        Assert.AreEqual(Today.AddDays(-4), await env.Use((_, db) => db.Loans.Where(x => x.Id == loan).Select(x => x.BorrowDate).SingleAsync()));
    }

    /// <summary>GET确认不修改数据，确认后被另一操作归还时POST必须重新检查并拒绝。</summary>
    [TestMethod]
    public async Task Delete_confirmation_does_not_mutate_and_return_after_confirmation_blocks_delete()
    {
        var reader = await Reader();
        var loan = await Loan(reader.Id, "Protected after confirmation");
        using var c = await Login();
        var path = $"/Loans/Delete/{loan}";
        StringAssert.Contains(await c.GetStringAsync(path), "Confirm delete");
        Assert.IsTrue(await env.Use((_, db) => db.Loans.AnyAsync(x => x.Id == loan)));
        Assert.IsTrue((await env.Use((s, _) => s.ReturnAsync(loan, new() { ReturnDate = Today }))).Success);
        var response = await Post(c, path, new(), "/Loans");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Returned loan records cannot be deleted.");
        Assert.IsTrue(await env.Use((_, db) => db.Loans.AnyAsync(x => x.Id == loan)));
    }

    /// <summary>删除未还误录后，管理员与所属读者列表都不再显示该记录。</summary>
    [TestMethod]
    public async Task Outstanding_mistake_delete_disappears_from_admin_and_reader_lists()
    {
        var reader = await Reader();
        var title = "Delete " + Guid.NewGuid().ToString("N");
        var loan = await Loan(reader.Id, title);
        using var c = await Login();
        Assert.AreEqual(HttpStatusCode.Redirect, (await Post(c, $"/Loans/Delete/{loan}", new(), "/Loans")).StatusCode);
        Assert.AreEqual(0, Ids(await c.GetStringAsync("/Loans?q=" + title)).Length);
        using var own = await Login(reader.Email);
        Assert.AreEqual(0, Ids(await own.GetStringAsync("/MyLoans?q=" + title)).Length);
    }

    /// <summary>验证管理员的首尾空白关键词、书名或读者姓名搜索与四种状态组合。</summary>
    [DataTestMethod]
    [DataRow("all", 4)]
    [DataRow("outstanding", 3)]
    [DataRow("returned", 1)]
    [DataRow("overdue", 1)]
    public async Task Admin_combines_trimmed_search_with_status(string status, int expected)
    {
        var key = Guid.NewGuid().ToString("N");
        var reader = await Reader("Match " + key);
        await Loan(reader.Id, "Unrelated title", due: -1);
        await Loan(reader.Id, key + " returned", due: -1, returned: true);
        await Loan(reader.Id, key + " due today", due: 0);
        await Loan(reader.Id, key + " upcoming", due: 3);
        using var c = await Login();
        Assert.AreEqual(expected, Ids(await c.GetStringAsync($"/Loans?q=%20{key}%20&status={status}&sort=due")).Length);
    }

    /// <summary>验证管理员借出日和应还日双向排序，相同日期用唯一编号稳定排序。</summary>
    [DataTestMethod]
    [DataRow("borrow")]
    [DataRow("borrow_desc")]
    [DataRow("due")]
    [DataRow("due_desc")]
    public async Task Admin_sorts_dates_and_stable_ties(string sort)
    {
        var key = Guid.NewGuid().ToString("N");
        var reader = await Reader();
        var a = await Loan(reader.Id, key, borrow: -4, due: 4);
        var b = await Loan(reader.Id, key, borrow: -2, due: 2);
        var tie = await Loan(reader.Id, key, borrow: -2, due: 2);
        var expected = sort switch
        {
            "borrow" => new[]
            {
                a,
                b,
                tie
            },
            "due" => new[]
            {
                b,
                tie,
                a
            },
            "due_desc" => new[]
            {
                a,
                tie,
                b
            },
            _ => new[]
            {
                tie,
                b,
                a
            }
        };
        using var c = await Login();
        CollectionAssert.AreEqual(expected, Ids(await c.GetStringAsync($"/Loans?q={key}&sort={sort}")));
    }

    /// <summary>组合四种状态与书名搜索，验证读者只能得到自己的结果且不能按姓名搜他人。</summary>
    [DataTestMethod]
    [DataRow("all")]
    [DataRow("outstanding")]
    [DataRow("returned")]
    [DataRow("overdue")]
    public async Task Reader_combined_filters_never_include_other_readers(string status)
    {
        var key = Guid.NewGuid().ToString("N");
        var a = await Reader("NameOnly " + key);
        var b = await Reader();
        var own = await Loan(a.Id, key, due: -1);
        await Loan(a.Id, key + " Returned", due: -1, returned: true);
        var foreign = await Loan(b.Id, key + " Foreign", due: -1);
        using var c = await Login(a.Email);
        var html = await c.GetStringAsync($"/MyLoans?q=%20{key}%20&status={status}&sort=due&readerId={b.Id}&id={foreign}");
        Assert.AreEqual(status == "all" ? 2 : 1, Ids(html).Length);
        Assert.IsFalse(Ids(html).Contains(foreign));
        Assert.IsFalse(html.Contains("Foreign"));
        Assert.AreEqual(0, Ids(await c.GetStringAsync("/MyLoans?q=NameOnly%20" + key)).Length);
    }

    /// <summary>验证个人借阅四种日期排序和日期相同的稳定次序，保持身份限制。</summary>
    [DataTestMethod]
    [DataRow("borrow")]
    [DataRow("borrow_desc")]
    [DataRow("due")]
    [DataRow("due_desc")]
    public async Task Reader_sorting_is_stable_inside_identity_scope(string sort)
    {
        var key = Guid.NewGuid().ToString("N");
        var reader = await Reader();
        var a = await Loan(reader.Id, key, borrow: -4, due: 4);
        var b = await Loan(reader.Id, key, borrow: -2, due: 2);
        var tie = await Loan(reader.Id, key, borrow: -2, due: 2);
        var other = await Reader();
        await Loan(other.Id, key, due: 1);
        var expected = sort switch
        {
            "borrow" => new[]
            {
                a,
                b,
                tie
            },
            "due" => new[]
            {
                b,
                tie,
                a
            },
            "due_desc" => new[]
            {
                a,
                tie,
                b
            },
            _ => new[]
            {
                tie,
                b,
                a
            }
        };
        using var c = await Login(reader.Email);
        CollectionAssert.AreEqual(expected, Ids(await c.GetStringAsync($"/MyLoans?q={key}&sort={sort}&readerId={other.Id}")));
    }

    /// <summary>验证非法排序、SQL样式关键词和脚本文本既不扩大数据范围也不执行脚本。</summary>
    [TestMethod]
    public async Task Invalid_filters_and_injection_terms_do_not_expand_reader_scope()
    {
        var a = await Reader();
        var b = await Reader();
        var own = await Loan(a.Id, "Safe own");
        var foreign = await Loan(b.Id, "Secret foreign");
        using var c = await Login(a.Email);
        var html = await c.GetStringAsync($"/MyLoans?status=bad&sort=DROP%20TABLE%20Loans&readerId={b.Id}");
        CollectionAssert.AreEqual(new[] { own }, Ids(html));
        Assert.IsFalse(html.Contains("Secret foreign"));
        Assert.AreEqual(0, Ids(await c.GetStringAsync("/MyLoans?q=" + Uri.EscapeDataString("' OR 1=1 --"))).Length);
        var encoded = await c.GetStringAsync("/MyLoans?q=" + Uri.EscapeDataString("<script>alert(1)</script>"));
        Assert.IsFalse(encoded.Contains("<script>alert(1)</script>"));
        StringAssert.Contains(encoded, "&lt;script&gt;");
        Assert.IsTrue(await env.Use((_, db) => db.Loans.AnyAsync(x => x.Id == foreign)));
    }

    /// <summary>验证无匹配空态和清空后恢复结果，始终不扩展读者身份范围。</summary>
    [TestMethod]
    public async Task Clearing_filters_restores_only_own_records_and_empty_search_is_visible()
    {
        var a = await Reader();
        var b = await Reader();
        var own = await Loan(a.Id, "Own clear");
        await Loan(b.Id, "Foreign clear");
        using var c = await Login(a.Email);
        StringAssert.Contains(await c.GetStringAsync("/MyLoans?q=missing-" + Guid.NewGuid()), "No loans match your search.");
        CollectionAssert.AreEqual(new[] { own }, Ids(await c.GetStringAsync("/MyLoans?q=%20%20&status=all")));
    }
}
