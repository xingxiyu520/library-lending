using Library.Web.Data;
using Library.Web.Models;
using Library.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Library.Tests;
/// <summary>真实MySQL上的维护、归还、行锁竞争及账号事务回滚测试。</summary>
[TestClass, TestCategory("MySQL")]
public class Sprint2DatabaseTests
{
    /// <summary>共享的隔离测试环境，所有用例使用随机数据库及虚构账号。</summary>
    private TestEnvironment env = null !;
    /// <summary>按UTC+8计算的当前业务日期，作为日期校验和逾期状态基准。</summary>
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

    /// <summary>为当前测试配置共享隔离环境；浏览器测试还会创建独立无头会话。</summary>
    [TestInitialize]
    public void Initialize() => env = TestEnvironment.Current;
    /// <summary>生成具有唯一ISBN的合法输入，使单个数据库用例控制总册数边界。</summary>
    private BookInput Data(int copies = 1) => new()
    {
        Isbn = Guid.NewGuid().ToString("N"),
        Title = "Maintenance book",
        Author = "Author",
        TotalCopies = copies
    };
    /// <summary>以唯一ISBN准备当前测试所需馆藏，避免其他测试数据影响断言。</summary>
    private async Task<int> Book(int copies = 1)
    {
        var result = await env.Use((s, _) => s.AddBookAsync(Data(copies)));
        Assert.IsTrue(result.Success, result.Error);
        return result.Id!.Value;
    }

    /// <summary>创建唯一虚构读者和关联账号，返回查询或登录所需标识。</summary>
    private async Task<int> Reader()
    {
        var result = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = "Maintenance reader", Email = Guid.NewGuid().ToString("N") + "@library.example", Password = env.Password }));
        Assert.IsTrue(result.Success, result.Error);
        return result.Id!.Value;
    }

    /// <summary>准备指定读者和图书的借阅，可按用例安排日期及归还状态。</summary>
    private async Task<int> Loan(int book, int reader)
    {
        var result = await env.Use((s, _) => s.LendAsync(new() { BookId = book, ReaderId = reader, BorrowDate = Today.AddDays(-2), DueDate = Today.AddDays(12), RequestId = Guid.NewGuid().ToString() }));
        Assert.IsTrue(result.Success, result.Error);
        return result.Id!.Value;
    }

    /// <summary>验证图书编辑持久化并按统一规则规范化ISBN。</summary>
    [TestMethod]
    public async Task Book_edit_persists_and_normalizes_ISBN()
    {
        var id = await Book();
        var input = Data(2);
        input.Isbn = " ab-" + input.Isbn[..12];
        input.Title = " Updated ";
        Assert.IsTrue((await env.Use((s, _) => s.EditBookAsync(id, input))).Success);
        var saved = await env.Use((_, db) => db.Books.SingleAsync(x => x.Id == id));
        Assert.AreEqual("Updated", saved.Title);
        Assert.AreEqual(LendingRules.NormalizeIsbn(input.Isbn), saved.Isbn);
        Assert.AreEqual(2, saved.TotalCopies);
    }

    /// <summary>将ISBN改为重复值时应拒绝，原有图书字段保持不变。</summary>
    [TestMethod]
    public async Task Book_edit_rejects_duplicate_without_changing_original()
    {
        var a = await Book();
        var b = await Book();
        var other = await env.Use((_, db) => db.Books.SingleAsync(x => x.Id == b));
        var input = Data();
        input.Isbn = other.Isbn;
        Assert.IsFalse((await env.Use((s, _) => s.EditBookAsync(a, input))).Success);
        Assert.AreNotEqual(other.Isbn, await env.Use((_, db) => db.Books.Where(x => x.Id == a).Select(x => x.Isbn).SingleAsync()));
    }

    /// <summary>验证总册数不得少于当前未归还数量。</summary>
    [TestMethod]
    public async Task Book_quantity_cannot_drop_below_outstanding_loans()
    {
        var book = await Book(2);
        await Loan(book, await Reader());
        var input = Data(0);
        Assert.IsFalse((await env.Use((s, _) => s.EditBookAsync(book, input))).Success);
        Assert.AreEqual(2, await env.Use((_, db) => db.Books.Where(x => x.Id == book).Select(x => x.TotalCopies).SingleAsync()));
        input.TotalCopies = 1;
        Assert.IsTrue((await env.Use((s, _) => s.EditBookAsync(book, input))).Success);
    }

    /// <summary>并发调整馆藏总数与借出最后一册，核对最终库存边界。</summary>
    [TestMethod]
    public async Task Quantity_change_and_last_copy_loan_preserve_stock()
    {
        var book = await Book();
        var reader = await Reader();
        var results = await Task.WhenAll(env.Use((s, _) => s.EditBookAsync(book, Data(0))), env.Use((s, _) => s.LendAsync(new() { BookId = book, ReaderId = reader, BorrowDate = Today, DueDate = Today, RequestId = Guid.NewGuid().ToString() })));
        Assert.AreEqual(1, results.Count(x => x.Success));
        Assert.IsTrue(await env.Use(async (_, db) => await db.Loans.CountAsync(x => x.BookId == book && x.ReturnDate == null) <= await db.Books.Where(x => x.Id == book).Select(x => x.TotalCopies).SingleAsync()));
    }

    /// <summary>验证无借阅历史的图书可以正常删除。</summary>
    [TestMethod]
    public async Task Book_without_history_can_be_deleted()
    {
        var book = await Book();
        Assert.IsTrue((await env.Use((s, _) => s.DeleteBookAsync(book))).Success);
        Assert.IsFalse(await env.Use((_, db) => db.Books.AnyAsync(x => x.Id == book)));
    }

    /// <summary>分别以未归还及已归还历史验证图书禁止删除。</summary>
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Book_with_any_history_is_protected(bool returned)
    {
        var book = await Book();
        var loan = await Loan(book, await Reader());
        if (returned)
            Assert.IsTrue((await env.Use((s, _) => s.ReturnAsync(loan, new() { ReturnDate = Today }))).Success);
        var result = await env.Use((s, _) => s.DeleteBookAsync(book));
        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error!, "loan history");
    }

    /// <summary>并发删除图书与借出，只允许一个操作成功，保护历史和外键。</summary>
    [TestMethod]
    public async Task Book_delete_racing_with_loan_has_one_winner()
    {
        var book = await Book();
        var reader = await Reader();
        var results = await Task.WhenAll(env.Use((s, _) => s.DeleteBookAsync(book)), env.Use((s, _) => s.LendAsync(new() { BookId = book, ReaderId = reader, BorrowDate = Today, DueDate = Today, RequestId = Guid.NewGuid().ToString() })));
        Assert.AreEqual(1, results.Count(x => x.Success));
    }

    /// <summary>核对档案更新同步账号邮箱，密码和Reader角色不被编辑改变。</summary>
    [TestMethod]
    public async Task Reader_edit_updates_profile_account_and_preserves_password_role()
    {
        var id = await Reader();
        var email = Guid.NewGuid().ToString("N") + "@library.example";
        Assert.IsTrue((await env.Use((s, _) => s.EditReaderAsync(id, new() { Name = " Updated reader ", Email = email.ToUpperInvariant(), Phone = "12345" }))).Success);
        var saved = await env.Use((_, db) => db.Readers.SingleAsync(x => x.Id == id));
        Assert.AreEqual("Updated reader", saved.Name);
        Assert.AreEqual(email, saved.Email);
        Assert.AreEqual("12345", saved.Phone);
        using var scope = env.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.FindByIdAsync(saved.UserId);
        Assert.IsNotNull(user);
        Assert.AreEqual(email, user.UserName);
        Assert.IsTrue(await users.CheckPasswordAsync(user, env.Password));
        CollectionAssert.AreEquivalent(new[] { "Reader" }, (await users.GetRolesAsync(user)).ToArray());
    }

    /// <summary>修改为已占用邮箱时拒绝，并核对档案和账号都保留旧值。</summary>
    [TestMethod]
    public async Task Duplicate_reader_email_keeps_original_profile_and_account()
    {
        var id = await Reader();
        var old = await env.Use((_, db) => db.Readers.SingleAsync(x => x.Id == id));
        Assert.IsFalse((await env.Use((s, _) => s.EditReaderAsync(id, new() { Name = "Should rollback", Email = "ADMIN@LIBRARY.EXAMPLE" }))).Success);
        var saved = await env.Use((_, db) => db.Readers.Include(x => x.User).SingleAsync(x => x.Id == id));
        Assert.AreEqual(old.Email, saved.Email);
        Assert.AreEqual(old.Name, saved.Name);
        Assert.AreEqual(old.Email, saved.User.UserName);
    }

    /// <summary>验证无历史读者的档案和登录账号一同删除。</summary>
    [TestMethod]
    public async Task Deleting_unborrowed_reader_removes_login_account()
    {
        var id = await Reader();
        var userId = await env.Use((_, db) => db.Readers.Where(x => x.Id == id).Select(x => x.UserId).SingleAsync());
        Assert.IsTrue((await env.Use((s, _) => s.DeleteReaderAsync(id))).Success);
        Assert.IsFalse(await env.Use((_, db) => db.Readers.AnyAsync(x => x.Id == id)));
        Assert.IsFalse(await env.Use((_, db) => db.Users.AnyAsync(x => x.Id == userId)));
    }

    /// <summary>未归还或已归还历史均阻止删除读者及其账号。</summary>
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Reader_with_any_history_and_account_is_protected(bool returned)
    {
        var reader = await Reader();
        var loan = await Loan(await Book(), reader);
        if (returned)
            Assert.IsTrue((await env.Use((s, _) => s.ReturnAsync(loan, new() { ReturnDate = Today }))).Success);
        Assert.IsFalse((await env.Use((s, _) => s.DeleteReaderAsync(reader))).Success);
        Assert.IsTrue(await env.Use((_, db) => db.Readers.Include(x => x.User).AnyAsync(x => x.Id == reader && x.User != null)));
    }

    /// <summary>并发删除读者与借出，只允许一致的一个结果，不残留孤立借阅。</summary>
    [TestMethod]
    public async Task Reader_delete_racing_with_lending_has_one_winner()
    {
        var reader = await Reader();
        var book = await Book();
        var results = await Task.WhenAll(env.Use((s, _) => s.DeleteReaderAsync(reader)), env.Use((s, _) => s.LendAsync(new() { BookId = book, ReaderId = reader, BorrowDate = Today, DueDate = Today, RequestId = Guid.NewGuid().ToString() })));
        Assert.AreEqual(1, results.Count(x => x.Success));
    }

    /// <summary>成功归还后核对日期已保存，未还数减少且可借数恢复。</summary>
    [TestMethod]
    public async Task Return_restores_one_copy_and_persists_date()
    {
        var book = await Book();
        var loan = await Loan(book, await Reader());
        Assert.IsTrue((await env.Use((s, _) => s.ReturnAsync(loan, new() { ReturnDate = Today }))).Success);
        Assert.AreEqual(Today, await env.Use((_, db) => db.Loans.Where(x => x.Id == loan).Select(x => x.ReturnDate).SingleAsync()));
        Assert.AreEqual(0, await env.Use((_, db) => db.Loans.CountAsync(x => x.BookId == book && x.ReturnDate == null)));
    }

    /// <summary>再次归还同一借阅时拒绝，并保留第一次真实归还日期。</summary>
    [TestMethod]
    public async Task Repeated_return_does_not_change_original_date()
    {
        var loan = await Loan(await Book(), await Reader());
        Assert.IsTrue((await env.Use((s, _) => s.ReturnAsync(loan, new() { ReturnDate = Today.AddDays(-1) }))).Success);
        var result = await env.Use((s, _) => s.ReturnAsync(loan, new() { ReturnDate = Today }));
        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error!, "already been returned");
        Assert.AreEqual(Today.AddDays(-1), await env.Use((_, db) => db.Loans.Where(x => x.Id == loan).Select(x => x.ReturnDate).SingleAsync()));
    }

    /// <summary>多路归还同一借阅，断言仅一次更新成功且库存只释放一次。</summary>
    [TestMethod]
    public async Task Concurrent_return_updates_exactly_once()
    {
        var loan = await Loan(await Book(), await Reader());
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => env.Use((s, _) => s.ReturnAsync(loan, new() { ReturnDate = Today }))));
        Assert.AreEqual(1, results.Count(x => x.Success));
    }

    /// <summary>验证归还日早于借出或晚于今天时，借阅仍保持未归还。</summary>
    [DataTestMethod]
    [DataRow(-3)]
    [DataRow(1)]
    public async Task Invalid_return_dates_leave_loan_outstanding(int offset)
    {
        var loan = await Loan(await Book(), await Reader());
        Assert.IsFalse((await env.Use((s, _) => s.ReturnAsync(loan, new() { ReturnDate = Today.AddDays(offset) }))).Success);
        Assert.IsNull(await env.Use((_, db) => db.Loans.Where(x => x.Id == loan).Select(x => x.ReturnDate).SingleAsync()));
    }

    /// <summary>验证业务层处理不存在图书、读者和借阅时返回明确错误。</summary>
    [TestMethod]
    public async Task Missing_entities_return_business_errors()
    {
        Assert.IsFalse((await env.Use((s, _) => s.EditBookAsync(int.MaxValue, Data()))).Success);
        Assert.IsFalse((await env.Use((s, _) => s.DeleteBookAsync(int.MaxValue))).Success);
        Assert.IsFalse((await env.Use((s, _) => s.EditReaderAsync(int.MaxValue, new() { Name = "Missing", Email = "missing@library.example" }))).Success);
        Assert.IsFalse((await env.Use((s, _) => s.DeleteReaderAsync(int.MaxValue))).Success);
        Assert.IsFalse((await env.Use((s, _) => s.ReturnAsync(int.MaxValue, new() { ReturnDate = Today }))).Success);
    }

    /// <summary>注入档案保存或Identity删除异常，核对邮箱、安全戳与档案完整回滚。</summary>
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Reader_edit_or_delete_failure_rolls_back_identity_changes(bool deleting)
    {
        var reader = await Reader();
        var original = await env.Use((_, db) => db.Readers.Include(x => x.User).SingleAsync(x => x.Id == reader));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LibraryDbContext>(o => o.UseMySql(env.Connection, new MySqlServerVersion(new Version(8, 4, 0))).AddInterceptors(new FailMaintenance(deleting)));
        services.AddIdentityCore<IdentityUser>(o =>
        {
            o.Password.RequireNonAlphanumeric = false;
        }).AddRoles<IdentityRole>().AddEntityFrameworkStores<LibraryDbContext>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<BusinessClock>();
        services.AddScoped<LibraryService>();
        using (var provider = services.BuildServiceProvider())
        {
            using var scope = provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<LibraryService>();
            await Assert.ThrowsExceptionAsync<DbUpdateException>(() => deleting ? service.DeleteReaderAsync(reader) : service.EditReaderAsync(reader, new() { Name = "Rollback", Email = Guid.NewGuid().ToString("N") + "@library.example" }));
        }

        var saved = await env.Use((_, db) => db.Readers.Include(x => x.User).SingleAsync(x => x.Id == reader));
        Assert.AreEqual(original.Email, saved.Email);
        Assert.AreEqual(original.Email, saved.User.Email);
        Assert.AreEqual(original.User.SecurityStamp, saved.User.SecurityStamp);
    }

    /// <summary>仅供测试注入保存失败，验证档案与Identity变更能完整回滚。</summary>
    private sealed class FailMaintenance(bool deleting) : SaveChangesInterceptor
    {
        /// <summary>在指定保存阶段注入异常，测试事务能否撤销账号与档案的部分变更。</summary>
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if ((!deleting && data.Context!.ChangeTracker.Entries<Reader>().Any(x => x.State == EntityState.Modified)) || (deleting && data.Context!.ChangeTracker.Entries<IdentityUser>().Any(x => x.State == EntityState.Deleted)))
                throw new DbUpdateException("Injected maintenance failure.");
            return ValueTask.FromResult(result);
        }
    }
}
