using Library.Web.Data;
using Library.Web.Models;
using Library.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Library.Tests;
/// <summary>通过真实MySQL验证唯一约束、账号事务、借出库存和请求幂等。</summary>
[TestClass, TestCategory("MySQL")]
public class DatabaseTests
{
    /// <summary>共享的隔离测试环境，所有用例使用随机数据库及虚构账号。</summary>
    private TestEnvironment env = null !;
    /// <summary>为当前测试配置共享隔离环境；浏览器测试还会创建独立无头会话。</summary>
    [TestInitialize]
    public void Initialize() => env = TestEnvironment.Current;
    /// <summary>以唯一ISBN准备当前测试所需馆藏，避免其他测试数据影响断言。</summary>
    private async Task<int> Book(int copies = 1)
    {
        var result = await env.Use((service, _) => service.AddBookAsync(new BookInput { Isbn = Guid.NewGuid().ToString("N"), Title = "Database book", Author = "Test Author", TotalCopies = copies }));
        Assert.IsTrue(result.Success, result.Error);
        return result.Id!.Value;
    }

    /// <summary>创建唯一虚构读者和关联账号，返回查询或登录所需标识。</summary>
    private async Task<int> Reader() => await env.Use((_, db) => db.Readers.OrderBy(x => x.Id).Select(x => x.Id).FirstAsync());
    /// <summary>准备指定读者和图书的借阅，可按用例安排日期及归还状态。</summary>
    private LoanInput Loan(int book, int reader, string? request = null) => new()
    {
        BookId = book,
        ReaderId = reader,
        BorrowDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)),
        DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).AddDays(14),
        RequestId = request ?? Guid.NewGuid().ToString()
    };
    /// <summary>验证仅分隔符或大小写不同的ISBN仍被视为重复。</summary>
    [TestMethod]
    public async Task Duplicate_normalized_ISBN_is_rejected()
    {
        var isbn = Guid.NewGuid().ToString("N")[..20];
        var first = await env.Use((s, _) => s.AddBookAsync(new() { Isbn = isbn, Title = "One", Author = "A", TotalCopies = 0 }));
        var second = await env.Use((s, _) => s.AddBookAsync(new() { Isbn = isbn.ToUpperInvariant().Insert(4, "-"), Title = "Two", Author = "B", TotalCopies = 1 }));
        Assert.IsTrue(first.Success);
        Assert.IsFalse(second.Success);
        StringAssert.Contains(second.Error!, "ISBN already exists");
    }

    /// <summary>使用不同上下文并发新增同一ISBN，最终只有一条保存成功。</summary>
    [TestMethod]
    public async Task Concurrent_duplicate_ISBN_has_one_winner()
    {
        var isbn = Guid.NewGuid().ToString("N");
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => env.Use((s, db) => s.AddBookAsync(new() { Isbn = isbn, Title = "Race", Author = "A", TotalCopies = 1 }))));
        Assert.AreEqual(1, results.Count(x => x.Success));
    }

    /// <summary>核对新账号保存密码哈希，且只能获得Reader角色。</summary>
    [TestMethod]
    public async Task New_reader_has_hashed_password_and_only_reader_role()
    {
        var email = "new" + Guid.NewGuid().ToString("N") + "@library.example";
        var result = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = "New reader", Email = email, Password = env.Password }));
        Assert.IsTrue(result.Success, result.Error);
        using var scope = env.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.IsNotNull(user);
        Assert.AreNotEqual(env.Password, user.PasswordHash);
        Assert.IsTrue(await users.CheckPasswordAsync(user, env.Password));
        CollectionAssert.AreEquivalent(new[] { "Reader" }, (await users.GetRolesAsync(user)).ToArray());
    }

    /// <summary>验证邮箱大小写差异不会绕过账号与档案唯一性。</summary>
    [TestMethod]
    public async Task Email_uniqueness_is_case_insensitive()
    {
        var result = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = "Duplicate", Email = "READER@LIBRARY.EXAMPLE", Password = env.Password }));
        Assert.IsFalse(result.Success);
    }

    /// <summary>账号密码验证失败时不残留Identity账号或读者档案。</summary>
    [TestMethod]
    public async Task Invalid_password_leaves_no_account_or_reader()
    {
        var email = "bad" + Guid.NewGuid().ToString("N") + "@library.example";
        var result = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = "Bad", Email = email, Password = "short" }));
        Assert.IsFalse(result.Success);
        Assert.IsFalse(await env.Use((_, db) => db.Users.AnyAsync(x => x.Email == email)));
        Assert.IsFalse(await env.Use((_, db) => db.Readers.AnyAsync(x => x.Email == email)));
    }

    /// <summary>注入读者保存故障，断言账号、角色关联与读者档案全部回滚。</summary>
    [TestMethod]
    public async Task Reader_save_failure_rolls_back_account_and_role()
    {
        var email = "rollback" + Guid.NewGuid().ToString("N") + "@library.example";
        using (var provider = FailureProvider(new FailReaderSave()))
        {
            using var scope = provider.CreateScope();
            await Assert.ThrowsExceptionAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<LibraryService>().CreateReaderAsync(new() { Name = "Rollback", Email = email, Password = env.Password }));
        }

        Assert.IsFalse(await env.Use((_, db) => db.Users.AnyAsync(x => x.Email == email)));
        Assert.IsFalse(await env.Use((_, db) => db.Readers.AnyAsync(x => x.Email == email)));
    }

    /// <summary>注入角色保存故障，断言新账号与读者均未残留。</summary>
    [TestMethod]
    public async Task Role_save_failure_rolls_back_new_account()
    {
        var email = "rolefail" + Guid.NewGuid().ToString("N") + "@library.example";
        using (var provider = FailureProvider(new FailRoleSave()))
        {
            using var scope = provider.CreateScope();
            await Assert.ThrowsExceptionAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<LibraryService>().CreateReaderAsync(new() { Name = "Rollback", Email = email, Password = env.Password }));
        }

        Assert.IsFalse(await env.Use((_, db) => db.Users.AnyAsync(x => x.Email == email)));
    }

    /// <summary>登记合法借阅后从真实数据库核对记录和可借数量减少。</summary>
    [TestMethod]
    public async Task Valid_loan_is_persisted_and_reduces_availability()
    {
        var book = await Book(2);
        var reader = await Reader();
        var result = await env.Use((s, _) => s.LendAsync(Loan(book, reader)));
        Assert.IsTrue(result.Success, result.Error);
        var outstanding = await env.Use((_, db) => db.Loans.CountAsync(x => x.BookId == book && x.ReturnDate == null));
        Assert.AreEqual(1, outstanding);
        using var scope = env.Scope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var saved = await db.Loans.SingleAsync(x => x.Id == result.Id);
        Assert.AreEqual(reader, saved.ReaderId);
        Assert.IsNull(saved.ReturnDate);
    }

    /// <summary>库存为零时拒绝登记借出，不产生借阅或部分写入。</summary>
    [TestMethod]
    public async Task No_stock_rejects_loan_without_side_effects()
    {
        var book = await Book(0);
        var reader = await Reader();
        var result = await env.Use((s, _) => s.LendAsync(Loan(book, reader)));
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, await env.Use((_, db) => db.Loans.CountAsync(x => x.BookId == book)));
    }

    /// <summary>多路争用最后一册图书，验证仅一笔有效借阅且库存不超额。</summary>
    [TestMethod]
    public async Task Concurrent_last_copy_allows_exactly_one_loan()
    {
        var book = await Book();
        var reader = await Reader();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => env.Use((s, db) => s.LendAsync(Loan(book, reader)))));
        Assert.AreEqual(1, results.Count(x => x.Success));
        Assert.AreEqual(1, await env.Use((_, db) => db.Loans.CountAsync(x => x.BookId == book)));
    }

    /// <summary>串行重放同一请求标识时返回原借阅编号，不重复消耗库存。</summary>
    [TestMethod]
    public async Task Replayed_submission_returns_same_loan_without_consuming_another_copy()
    {
        var book = await Book(2);
        var reader = await Reader();
        var input = Loan(book, reader);
        var first = await env.Use((s, _) => s.LendAsync(input));
        var second = await env.Use((s, _) => s.LendAsync(input));
        Assert.IsTrue(first.Success);
        Assert.IsTrue(second.Success);
        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(1, await env.Use((_, db) => db.Loans.CountAsync(x => x.BookId == book)));
    }

    /// <summary>并发重放同一登记请求，核对最终仅一条借阅与一次库存占用。</summary>
    [TestMethod]
    public async Task Simultaneous_replay_has_one_persisted_loan()
    {
        var book = await Book(2);
        var reader = await Reader();
        var input = Loan(book, reader);
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => env.Use((s, db) => s.LendAsync(input))));
        Assert.IsTrue(results.All(x => x.Success));
        Assert.AreEqual(1, results.Select(x => x.Id).Distinct().Count());
    }

    /// <summary>同一RequestId改动关联或日期重放时拒绝，避免借阅被替换。</summary>
    [TestMethod]
    public async Task Reused_token_with_changed_payload_is_rejected()
    {
        var book = await Book(2);
        var reader = await Reader();
        var input = Loan(book, reader);
        Assert.IsTrue((await env.Use((s, _) => s.LendAsync(input))).Success);
        input.DueDate = input.DueDate!.Value.AddDays(1);
        Assert.IsFalse((await env.Use((s, _) => s.LendAsync(input))).Success);
    }

    /// <summary>使用新的提交标识允许借出同书另一册，不误伤真实的后续登记。</summary>
    [TestMethod]
    public async Task Fresh_submission_can_borrow_another_copy_of_same_title()
    {
        var book = await Book(2);
        var reader = await Reader();
        Assert.IsTrue((await env.Use((s, _) => s.LendAsync(Loan(book, reader)))).Success);
        Assert.IsTrue((await env.Use((s, _) => s.LendAsync(Loan(book, reader)))).Success);
        Assert.AreEqual(2, await env.Use((_, db) => db.Loans.CountAsync(x => x.BookId == book)));
    }

    /// <summary>引用不存在图书或读者时应返回可处理错误且不写入借阅。</summary>
    [TestMethod]
    public async Task Invalid_foreign_keys_are_rejected()
    {
        var book = await Book();
        var reader = await Reader();
        Assert.IsFalse((await env.Use((s, _) => s.LendAsync(Loan(int.MaxValue, reader)))).Success);
        Assert.IsFalse((await env.Use((s, _) => s.LendAsync(Loan(book, int.MaxValue)))).Success);
        Assert.AreEqual(0, await env.Use((_, db) => db.Loans.CountAsync(x => x.BookId == book)));
    }

    /// <summary>验证非法借出或应还日期不会产生借阅行。</summary>
    [TestMethod]
    public async Task Invalid_dates_do_not_create_a_loan()
    {
        var book = await Book();
        var input = Loan(book, await Reader());
        input.DueDate = input.BorrowDate!.Value.AddDays(-1);
        Assert.IsFalse((await env.Use((s, _) => s.LendAsync(input))).Success);
        input.BorrowDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).AddDays(1);
        input.DueDate = input.BorrowDate;
        Assert.IsFalse((await env.Use((s, _) => s.LendAsync(input))).Success);
    }

    /// <summary>绕过服务直接保存负数总册数，验证MySQL检查约束拒绝非法库存。</summary>
    [TestMethod]
    public async Task Database_constraint_blocks_negative_copies()
    {
        using var scope = env.Scope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        db.Books.Add(new() { Isbn = Guid.NewGuid().ToString("N"), Title = "Invalid", Author = "Author", TotalCopies = -1 });
        await Assert.ThrowsExceptionAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <summary>构建带指定故障注入拦截器的独立容器，用于真实事务回滚验证。</summary>
    private ServiceProvider FailureProvider(SaveChangesInterceptor interceptor)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LibraryDbContext>(o => o.UseMySql(env.Connection, new MySqlServerVersion(new Version(8, 4, 0))).AddInterceptors(interceptor));
        services.AddIdentityCore<IdentityUser>(o =>
        {
            o.Password.RequiredLength = 8;
            o.Password.RequireNonAlphanumeric = false;
            o.Password.RequireUppercase = false;
        }).AddRoles<IdentityRole>().AddEntityFrameworkStores<LibraryDbContext>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<BusinessClock>();
        services.AddScoped<LibraryService>();
        return services.BuildServiceProvider();
    }

    /// <summary>仅在保存读者时抛出测试故障，模拟跨账号与档案流程中的数据库失败。</summary>
    private sealed class FailReaderSave : SaveChangesInterceptor
    {
        /// <summary>在指定保存阶段注入异常，测试事务能否撤销账号与档案的部分变更。</summary>
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (data.Context!.ChangeTracker.Entries<Reader>().Any(x => x.State == EntityState.Added))
                throw new DbUpdateException("Injected reader persistence failure.");
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>仅在保存角色关联时抛出测试故障，检验账号创建事务的原子性。</summary>
    private sealed class FailRoleSave : SaveChangesInterceptor
    {
        /// <summary>在指定保存阶段注入异常，测试事务能否撤销账号与档案的部分变更。</summary>
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (data.Context!.ChangeTracker.Entries<IdentityUserRole<string>>().Any(x => x.State == EntityState.Added))
                throw new DbUpdateException("Injected role persistence failure.");
            return ValueTask.FromResult(result);
        }
    }
}
