using System.Net;

namespace Library.Tests;
// 测试真实Kestrel进程重启而非只重建DbContext，核对已提交更正仍可查询。
/// <summary>重启真实应用进程后重新登录，核对已提交数据仍保存在同一MySQL测试库。</summary>
[TestClass, TestCategory("Integration")]
public class PersistenceTests
{
    /// <summary>提交借阅日期修改后重启真实Kestrel，再登录核对数据与更正日期仍存在。</summary>
    [TestMethod]
    public async Task Committed_loan_correction_survives_application_process_restart()
    {
        var env = TestEnvironment.Current;
        var key = Guid.NewGuid().ToString("N");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var book = await env.Use((s, _) => s.AddBookAsync(new() { Isbn = key, Title = "Persist " + key, Author = "Restart", TotalCopies = 1 }));
        var reader = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = "Persistence reader", Email = key + "@library.example", Password = env.Password }));
        var loan = await env.Use((s, _) => s.LendAsync(new() { BookId = book.Id, ReaderId = reader.Id, BorrowDate = today, DueDate = today.AddDays(3), RequestId = Guid.NewGuid().ToString() }));
        Assert.IsTrue(loan.Success);
        Assert.IsTrue((await env.Use((s, _) => s.EditLoanAsync(loan.Id!.Value, new() { BorrowDate = today, DueDate = today.AddDays(9) }))).Success);
        await env.StartBrowserHost();
        await env.RestartBrowserHost();
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = new CookieContainer()
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(env.BrowserUrl)
        };
        await HttpTests.Login(client, key + "@library.example", env.Password);
        var html = await client.GetStringAsync("/MyLoans");
        StringAssert.Contains(html, "Persist " + key);
        StringAssert.Contains(html, today.AddDays(9).ToString("yyyy-MM-dd"));
    }
}
