using System.ComponentModel.DataAnnotations;
using System.Data;
using Library.Web.Data;
using Library.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Library.Web.Services;
/// <summary>封装馆藏、账号与借阅的事务规则；库存写操作统一先锁图书，防止并发破坏可借数。</summary>
public class LibraryService(LibraryDbContext db, UserManager<IdentityUser> users, BusinessClock clock)
{
    /// <summary>运行DataAnnotations验证并返回首条可处理错误，服务层不依赖浏览器校验。</summary>
    private static string? Invalid(object input)
    {
        var errors = new List<ValidationResult>();
        return Validator.TryValidateObject(input, new ValidationContext(input), errors, true) ? null : errors[0].ErrorMessage;
    }

    /// <summary>验证并规范化ISBN后新增图书，最终唯一索引处理并发重复值。</summary>
    public async Task<OperationResult> AddBookAsync(BookInput input)
    {
        if (Invalid(input)is { } error)
            return OperationResult.Fail(error);
        var isbn = LendingRules.NormalizeIsbn(input.Isbn);
        if (isbn.Length == 0)
            return OperationResult.Fail("ISBN is required.");
        if (await db.Books.AnyAsync(x => x.Isbn == isbn))
            return OperationResult.Fail("A book with this ISBN already exists.");
        var book = new Book
        {
            Isbn = isbn,
            Title = input.Title.Trim(),
            Author = input.Author.Trim(),
            Category = input.Category?.Trim(),
            TotalCopies = input.TotalCopies!.Value
        };
        db.Books.Add(book);
        try
        {
            await db.SaveChangesAsync();
            return OperationResult.Ok(book.Id);
        }
        catch (DbUpdateException ex)when (IsDuplicate(ex))
        {
            db.ChangeTracker.Clear();
            return OperationResult.Fail("A book with this ISBN already exists.");
        }
    }

    /// <summary>在同一事务中创建账号、固定Reader角色和档案，任一步失败都不提交。</summary>
    public async Task<OperationResult> CreateReaderAsync(ReaderInput input)
    {
        if (Invalid(input)is { } error)
            return OperationResult.Fail(error);
        var email = input.Email.Trim().ToLowerInvariant();
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };
            var created = await users.CreateAsync(user, input.Password);
            if (!created.Succeeded)
                return OperationResult.Fail(string.Join(" ", created.Errors.Select(x => x.Description)));
            // 角色固定为Reader，任何表单Role字段都不参与授权。
            var role = await users.AddToRoleAsync(user, "Reader");
            if (!role.Succeeded)
                return OperationResult.Fail("Reader account could not be provisioned.");
            var reader = new Reader
            {
                UserId = user.Id,
                Name = input.Name.Trim(),
                Email = email,
                Phone = input.Phone?.Trim()
            };
            db.Readers.Add(reader);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return OperationResult.Ok(reader.Id);
        }
        catch (DbUpdateException ex)when (IsDuplicate(ex))
        {
            return OperationResult.Fail("An account with this email already exists.");
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>先锁图书与读者，再验证库存和请求标识，避免最后一册被并发超额借出。</summary>
    public async Task<OperationResult> LendAsync(LoanInput input)
    {
        if (Invalid(input)is { } error)
            return OperationResult.Fail(error);
        if (!Guid.TryParseExact(input.RequestId, "D", out _))
            return OperationResult.Fail("Invalid submission token. Reload the form.");
        if (LendingRules.ValidateDates(input.BorrowDate!.Value, input.DueDate!.Value, clock.Today)is { } dateError)
            return OperationResult.Fail(dateError);
        // 所有借出操作先锁定父图书，再统计已提交的未归还记录。
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var book = await db.Books.FromSqlInterpolated($"SELECT * FROM Books WHERE Id = {input.BookId} FOR UPDATE").SingleOrDefaultAsync();
        if (book is null)
            return OperationResult.Fail("The selected book no longer exists.");
        // 相同请求仅允许重放完全相同的登记，返回旧记录而不再次占用库存。
        var previous = await db.Loans.SingleOrDefaultAsync(x => x.RequestId == input.RequestId);
        if (previous is not null)
            return Matches(previous, input) ? OperationResult.Ok(previous.Id) : OperationResult.Fail("This submission token has already been used.");
        if (await db.Readers.FromSqlInterpolated($"SELECT * FROM Readers WHERE Id = {input.ReaderId} FOR UPDATE").SingleOrDefaultAsync()is null)
            return OperationResult.Fail("The selected reader no longer exists.");
        var outstanding = await db.Loans.CountAsync(x => x.BookId == book.Id && x.ReturnDate == null);
        if (outstanding >= book.TotalCopies)
            return OperationResult.Fail("No copies are available for this book.");
        var loan = new Loan
        {
            BookId = book.Id,
            ReaderId = input.ReaderId!.Value,
            BorrowDate = input.BorrowDate.Value,
            DueDate = input.DueDate.Value,
            RequestId = input.RequestId
        };
        db.Loans.Add(loan);
        try
        {
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return OperationResult.Ok(loan.Id);
        }
        catch (DbUpdateException ex)when (IsDuplicate(ex))
        {
            db.ChangeTracker.Clear();
            return OperationResult.Fail("This submission token has already been used.");
        }
    }

    /// <summary>锁定图书后核对未归还数量和ISBN唯一性，仅保存合法的馆藏修改。</summary>
    public async Task<OperationResult> EditBookAsync(int id, BookInput input)
    {
        if (Invalid(input)is { } error)
            return OperationResult.Fail(error);
        var isbn = LendingRules.NormalizeIsbn(input.Isbn);
        if (isbn.Length == 0)
            return OperationResult.Fail("ISBN is required.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var book = await db.Books.FromSqlInterpolated($"SELECT * FROM Books WHERE Id = {id} FOR UPDATE").SingleOrDefaultAsync();
        if (book is null)
            return OperationResult.Fail("Book not found.");
        if (await db.Books.AnyAsync(x => x.Id != id && x.Isbn == isbn))
            return OperationResult.Fail("A book with this ISBN already exists.");
        var outstanding = await db.Loans.CountAsync(x => x.BookId == id && x.ReturnDate == null);
        if (input.TotalCopies < outstanding)
            return OperationResult.Fail("Total copies cannot be less than outstanding loans.");
        book.Isbn = isbn;
        book.Title = input.Title.Trim();
        book.Author = input.Author.Trim();
        book.Category = input.Category?.Trim();
        book.TotalCopies = input.TotalCopies!.Value;
        try
        {
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return OperationResult.Ok(id);
        }
        catch (DbUpdateException ex)when (IsDuplicate(ex))
        {
            db.ChangeTracker.Clear();
            return OperationResult.Fail("A book with this ISBN already exists.");
        }
    }

    /// <summary>锁定图书并检查全部借阅历史，无历史才允许删除。</summary>
    public async Task<OperationResult> DeleteBookAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var book = await db.Books.FromSqlInterpolated($"SELECT * FROM Books WHERE Id = {id} FOR UPDATE").SingleOrDefaultAsync();
        if (book is null)
            return OperationResult.Fail("Book not found.");
        if (await db.Loans.AnyAsync(x => x.BookId == id))
            return OperationResult.Fail("Books with loan history cannot be deleted.");
        db.Books.Remove(book);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return OperationResult.Ok(id);
    }

    /// <summary>在同一事务内更新档案及Identity邮箱，并更新安全戳立即使旧会话失效。</summary>
    public async Task<OperationResult> EditReaderAsync(int id, ReaderEditInput input)
    {
        if (Invalid(input)is { } error)
            return OperationResult.Fail(error);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var reader = await db.Readers.FromSqlInterpolated($"SELECT * FROM Readers WHERE Id = {id} FOR UPDATE").SingleOrDefaultAsync();
            if (reader is null)
                return OperationResult.Fail("Reader not found.");
            var user = await users.FindByIdAsync(reader.UserId);
            if (user is null)
                return OperationResult.Fail("The reader account is unavailable.");
            var email = input.Email.Trim().ToLowerInvariant();
            user.Email = email;
            user.UserName = email;
            user.EmailConfirmed = true;
            var updated = await users.UpdateAsync(user);
            if (!updated.Succeeded)
                return OperationResult.Fail(string.Join(" ", updated.Errors.Select(x => x.Description)));
            // 更新安全戳使既有Cookie立即失效，身份更正后需重新登录。
            var stamped = await users.UpdateSecurityStampAsync(user);
            if (!stamped.Succeeded)
                return OperationResult.Fail("The reader account could not be updated.");
            reader.Name = input.Name.Trim();
            reader.Email = email;
            reader.Phone = input.Phone?.Trim();
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return OperationResult.Ok(id);
        }
        catch (DbUpdateException ex)when (IsDuplicate(ex))
        {
            return OperationResult.Fail("An account with this email already exists.");
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>先锁定读者并阻止删除任何有历史的档案，再同步移除登录账号。</summary>
    public async Task<OperationResult> DeleteReaderAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var reader = await db.Readers.FromSqlInterpolated($"SELECT * FROM Readers WHERE Id = {id} FOR UPDATE").SingleOrDefaultAsync();
            if (reader is null)
                return OperationResult.Fail("Reader not found.");
            if (await db.Loans.AnyAsync(x => x.ReaderId == id))
                return OperationResult.Fail("Readers with loan history cannot be deleted.");
            var user = await users.FindByIdAsync(reader.UserId);
            if (user is null)
                return OperationResult.Fail("The reader account is unavailable.");
            db.Readers.Remove(reader);
            await db.SaveChangesAsync();
            var deleted = await users.DeleteAsync(user);
            if (!deleted.Succeeded)
                return OperationResult.Fail("The reader account could not be deleted.");
            await tx.CommitAsync();
            return OperationResult.Ok(id);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>按图书、借阅顺序取得行锁，检查最新归还状态及日期，只释放一次可借库存。</summary>
    public async Task<OperationResult> ReturnAsync(int id, ReturnInput input)
    {
        if (Invalid(input)is { } error)
            return OperationResult.Fail(error);
        var bookId = await db.Loans.Where(x => x.Id == id).Select(x => (int? )x.BookId).SingleOrDefaultAsync();
        if (!bookId.HasValue)
            return OperationResult.Fail("Loan not found.");
        // 借出、库存调整、删除和归还遵守相同的父图书优先加锁顺序。
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        if (await db.Books.FromSqlInterpolated($"SELECT * FROM Books WHERE Id = {bookId.Value} FOR UPDATE").SingleOrDefaultAsync()is null)
            return OperationResult.Fail("Loan not found.");
        var loan = await db.Loans.FromSqlInterpolated($"SELECT * FROM Loans WHERE Id = {id} FOR UPDATE").SingleOrDefaultAsync();
        if (loan is null)
            return OperationResult.Fail("Loan not found.");
        if (loan.ReturnDate.HasValue)
            return OperationResult.Fail("This loan has already been returned.");
        if (LendingRules.ValidateReturn(loan.BorrowDate, input.ReturnDate!.Value, clock.Today)is { } dateError)
            return OperationResult.Fail(dateError);
        loan.ReturnDate = input.ReturnDate;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return OperationResult.Ok(id);
    }

    // 先锁图书再锁借阅，与借出/归还/总册数调整采用统一顺序，避免丢失更新。
    /// <summary>锁定图书和最新借阅后校验两个日期，保护关联、RequestId及实际归还日期。</summary>
    public async Task<OperationResult> EditLoanAsync(int id, LoanEditInput input)
    {
        if (Invalid(input)is { } error)
            return OperationResult.Fail(error);
        var bookId = await db.Loans.Where(x => x.Id == id).Select(x => (int? )x.BookId).SingleOrDefaultAsync();
        if (!bookId.HasValue)
            return OperationResult.Fail("Loan not found.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var book = await db.Books.FromSqlInterpolated($"SELECT * FROM Books WHERE Id = {bookId.Value} FOR UPDATE").SingleOrDefaultAsync();
        if (book is null)
            return OperationResult.Fail("Loan not found.");
        var loan = await db.Loans.FromSqlInterpolated($"SELECT * FROM Loans WHERE Id = {id} FOR UPDATE").SingleOrDefaultAsync();
        if (loan is null)
            return OperationResult.Fail("Loan not found.");
        if (LendingRules.ValidateCorrection(input.BorrowDate!.Value, input.DueDate!.Value, loan.ReturnDate, clock.Today)is { } dateError)
            return OperationResult.Fail(dateError);
        loan.BorrowDate = input.BorrowDate.Value;
        loan.DueDate = input.DueDate.Value;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return OperationResult.Ok(id);
    }

    // 删除确认页面不具备授权效力；提交事务内重新检查最新归还状态。
    /// <summary>提交时重新锁定并检查未归还状态，仅删除误录记录，可借数由剩余记录推导。</summary>
    public async Task<OperationResult> DeleteLoanAsync(int id)
    {
        var bookId = await db.Loans.Where(x => x.Id == id).Select(x => (int? )x.BookId).SingleOrDefaultAsync();
        if (!bookId.HasValue)
            return OperationResult.Fail("Loan not found.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var book = await db.Books.FromSqlInterpolated($"SELECT * FROM Books WHERE Id = {bookId.Value} FOR UPDATE").SingleOrDefaultAsync();
        if (book is null)
            return OperationResult.Fail("Loan not found.");
        var loan = await db.Loans.FromSqlInterpolated($"SELECT * FROM Loans WHERE Id = {id} FOR UPDATE").SingleOrDefaultAsync();
        if (loan is null)
            return OperationResult.Fail("Loan not found.");
        if (loan.ReturnDate.HasValue)
            return OperationResult.Fail("Returned loan records cannot be deleted.");
        db.Loans.Remove(loan);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        // 可借数按剩余未归还记录推导，不单独增加库存字段，避免重复释放。
        return OperationResult.Ok(id);
    }

    /// <summary>比较重放请求与已有借阅的关联和日期，避免同一标识被用于不同借出。</summary>
    private static bool Matches(Loan l, LoanInput i) => l.BookId == i.BookId && l.ReaderId == i.ReaderId && l.BorrowDate == i.BorrowDate && l.DueDate == i.DueDate;
    /// <summary>只识别MySQL唯一约束冲突编号，其他数据库故障继续抛出以便排查。</summary>
    private static bool IsDuplicate(DbUpdateException e) => e.InnerException is MySqlException { Number: 1062 };
}
