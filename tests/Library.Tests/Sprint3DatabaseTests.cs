using Library.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Library.Tests;
// 直接使用真实MySQL事务验证更正、删除、库存和并发，不以模拟库替代行锁。
/// <summary>借阅更正和误录删除的事务与竞争回归，核对历史和库存不变量。</summary>
[TestClass, TestCategory("MySQL")]
public class Sprint3DatabaseTests
{
    /// <summary>共享的隔离测试环境，所有用例使用随机数据库及虚构账号。</summary>
    private TestEnvironment env = null !;
    /// <summary>按UTC+8计算的当前业务日期，作为日期校验和逾期状态基准。</summary>
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

    /// <summary>为当前测试配置共享隔离环境；浏览器测试还会创建独立无头会话。</summary>
    [TestInitialize]
    public void Initialize() => env = TestEnvironment.Current;
    /// <summary>创建独立一册图书、读者与借阅，为更正和删除并发用例准备基线。</summary>
    private async Task<(int Book, int Reader, int Loan)> Record()
    {
        var key = Guid.NewGuid().ToString("N");
        var book = await env.Use((s, _) => s.AddBookAsync(new() { Isbn = key, Title = "Correction " + key, Author = "Test", TotalCopies = 1 }));
        var reader = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = "Correction reader", Email = key + "@library.example", Password = env.Password }));
        var loan = await env.Use((s, _) => s.LendAsync(new() { BookId = book.Id, ReaderId = reader.Id, BorrowDate = Today.AddDays(-4), DueDate = Today.AddDays(3), RequestId = Guid.NewGuid().ToString() }));
        Assert.IsTrue(book.Success && reader.Success && loan.Success);
        return (book.Id!.Value, reader.Id!.Value, loan.Id!.Value);
    }

    /// <summary>分别更正未还和已还记录，核对关联、归还日和幂等标识保持一致。</summary>
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Date_correction_preserves_associations_return_and_submission_token(bool returned)
    {
        var row = await Record();
        if (returned)
            Assert.IsTrue((await env.Use((s, _) => s.ReturnAsync(row.Loan, new() { ReturnDate = Today.AddDays(-1) }))).Success);
        var old = await env.Use((_, db) => db.Loans.AsNoTracking().SingleAsync(x => x.Id == row.Loan));
        Assert.IsTrue((await env.Use((s, _) => s.EditLoanAsync(row.Loan, new() { BorrowDate = Today.AddDays(-2), DueDate = Today.AddDays(7) }))).Success);
        var saved = await env.Use((_, db) => db.Loans.AsNoTracking().SingleAsync(x => x.Id == row.Loan));
        Assert.AreEqual(Today.AddDays(-2), saved.BorrowDate);
        Assert.AreEqual(Today.AddDays(7), saved.DueDate);
        Assert.AreEqual(old.BookId, saved.BookId);
        Assert.AreEqual(old.ReaderId, saved.ReaderId);
        Assert.AreEqual(old.ReturnDate, saved.ReturnDate);
        Assert.AreEqual(old.RequestId, saved.RequestId);
    }

    /// <summary>验证已归还记录的非法日期更正不改变借出、应还或归还状态。</summary>
    [DataTestMethod]
    [DataRow(1, 2)]
    [DataRow(-2, -3)]
    [DataRow(0, 2)]
    public async Task Invalid_correction_does_not_modify_returned_record(int borrow, int due)
    {
        var row = await Record();
        Assert.IsTrue((await env.Use((s, _) => s.ReturnAsync(row.Loan, new() { ReturnDate = Today.AddDays(-1) }))).Success);
        Assert.IsFalse((await env.Use((s, _) => s.EditLoanAsync(row.Loan, new() { BorrowDate = Today.AddDays(borrow), DueDate = Today.AddDays(due) }))).Success);
        var saved = await env.Use((_, db) => db.Loans.SingleAsync(x => x.Id == row.Loan));
        Assert.AreEqual(Today.AddDays(-4), saved.BorrowDate);
        Assert.AreEqual(Today.AddDays(3), saved.DueDate);
    }

    /// <summary>删除未归还误录后核对可借数恢复一册，重复删除不能再次释放。</summary>
    [TestMethod]
    public async Task Deleting_outstanding_mistake_restores_one_available_copy()
    {
        var row = await Record();
        Assert.IsTrue((await env.Use((s, _) => s.DeleteLoanAsync(row.Loan))).Success);
        Assert.IsFalse(await env.Use((_, db) => db.Loans.AnyAsync(x => x.Id == row.Loan)));
        Assert.AreEqual(1, await env.Use((_, db) => db.Books.Where(x => x.Id == row.Book).Select(x => x.TotalCopies - x.Loans.Count(l => l.ReturnDate == null)).SingleAsync()));
        Assert.IsTrue(await env.Use((_, db) => db.Readers.AnyAsync(x => x.Id == row.Reader)));
        Assert.IsFalse((await env.Use((s, _) => s.DeleteLoanAsync(row.Loan))).Success);
    }

    /// <summary>验证已归还历史不能通过误录删除入口移除。</summary>
    [TestMethod]
    public async Task Returned_record_cannot_be_deleted()
    {
        var row = await Record();
        Assert.IsTrue((await env.Use((s, _) => s.ReturnAsync(row.Loan, new() { ReturnDate = Today }))).Success);
        var result = await env.Use((s, _) => s.DeleteLoanAsync(row.Loan));
        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error!, "cannot be deleted");
        Assert.IsTrue(await env.Use((_, db) => db.Loans.AnyAsync(x => x.Id == row.Loan)));
    }

    /// <summary>8路并发删除同一误录，断言仅一笔事务成功。</summary>
    [TestMethod]
    public async Task Eight_concurrent_deletions_remove_exactly_one_record()
    {
        var row = await Record();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => env.Use((s, _) => s.DeleteLoanAsync(row.Loan))));
        Assert.AreEqual(1, results.Count(x => x.Success));
    }

    /// <summary>归还与误录删除并发时只允许一方成功，最终可借数恢复一册。</summary>
    [TestMethod]
    public async Task Return_racing_with_delete_has_one_winner_and_consistent_stock()
    {
        var row = await Record();
        var results = await Task.WhenAll(env.Use((s, _) => s.ReturnAsync(row.Loan, new() { ReturnDate = Today })), env.Use((s, _) => s.DeleteLoanAsync(row.Loan)));
        Assert.AreEqual(1, results.Count(x => x.Success));
        var saved = await env.Use((_, db) => db.Loans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.Loan));
        Assert.IsTrue(saved is null || saved.ReturnDate == Today);
        Assert.AreEqual(1, await env.Use((_, db) => db.Books.Where(x => x.Id == row.Book).Select(x => x.TotalCopies - x.Loans.Count(l => l.ReturnDate == null)).SingleAsync()));
    }

    /// <summary>并发更正借出日与归还，验证不会保存归还早于借出的矛盾记录。</summary>
    [TestMethod]
    public async Task Correction_racing_with_return_never_inverts_recorded_dates()
    {
        var row = await Record();
        var results = await Task.WhenAll(env.Use((s, _) => s.EditLoanAsync(row.Loan, new() { BorrowDate = Today, DueDate = Today.AddDays(7) })), env.Use((s, _) => s.ReturnAsync(row.Loan, new() { ReturnDate = Today.AddDays(-1) })));
        Assert.AreEqual(1, results.Count(x => x.Success));
        var saved = await env.Use((_, db) => db.Loans.SingleAsync(x => x.Id == row.Loan));
        Assert.IsTrue(saved.ReturnDate is null || saved.ReturnDate >= saved.BorrowDate);
    }

    /// <summary>误录删除与新借出竞争时核对未归还数不超过馆藏总数。</summary>
    [TestMethod]
    public async Task Deletion_racing_with_lending_never_overcommits_last_copy()
    {
        var row = await Record();
        await Task.WhenAll(env.Use((s, _) => s.DeleteLoanAsync(row.Loan)), env.Use((s, _) => s.LendAsync(new() { BookId = row.Book, ReaderId = row.Reader, BorrowDate = Today, DueDate = Today.AddDays(7), RequestId = Guid.NewGuid().ToString() })));
        Assert.IsTrue(await env.Use((_, db) => db.Loans.CountAsync(x => x.BookId == row.Book && x.ReturnDate == null)) <= 1);
    }

    /// <summary>验证更正或删除缺失记录及不完整输入不被当作成功。</summary>
    [TestMethod]
    public async Task Missing_or_incomplete_correction_returns_business_error()
    {
        Assert.IsFalse((await env.Use((s, _) => s.EditLoanAsync(int.MaxValue, new() { BorrowDate = Today, DueDate = Today }))).Success);
        Assert.IsFalse((await env.Use((s, _) => s.DeleteLoanAsync(int.MaxValue))).Success);
        var row = await Record();
        Assert.IsFalse((await env.Use((s, _) => s.EditLoanAsync(row.Loan, new()))).Success);
    }
}
