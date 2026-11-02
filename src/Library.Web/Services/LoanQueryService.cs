using Library.Web.Data;
using Library.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Library.Web.Services;
// 统一借阅检索口径；身份范围由明确的管理员/读者入口决定，永不接受客户端身份。
/// <summary>复用借阅的身份限制、关键词、状态与稳定排序，读者查询不输出其他读者资料。</summary>
public class LoanQueryService(LibraryDbContext db, BusinessClock clock)
{
    /// <summary>从明确的管理员入口检索全部授权记录，可搜索书名或读者姓名。</summary>
    public Task<LoanListPage> ForAdminAsync(LoanFilters filters) => QueryAsync(filters, null);
    /// <summary>从明确的读者入口按档案编号限制范围，再执行书名搜索与状态排序。</summary>
    public Task<LoanListPage> ForReaderAsync(int readerId, LoanFilters filters) => QueryAsync(filters, readerId);
    /// <summary>在数据库内先限制身份，再组合关键词与状态，最后按日期和唯一编号稳定排序。</summary>
    private async Task<LoanListPage> QueryAsync(LoanFilters filters, int? readerId)
    {
        var today = clock.Today;
        var query = db.Loans.AsNoTracking().AsQueryable();
        // 在搜索、筛选及排序之前应用归属限制，空关键词和非法排序都无法解除它。
        if (readerId.HasValue)
            query = query.Where(x => x.ReaderId == readerId.Value);
        if (filters.Query.Length > 0)
        {
            var term = filters.Query;
            query = readerId.HasValue ? query.Where(x => x.Book.Title.Contains(term)) : query.Where(x => x.Book.Title.Contains(term) || x.Reader.Name.Contains(term));
        }

        query = filters.Status switch
        {
            "outstanding" => query.Where(x => x.ReturnDate == null),
            "returned" => query.Where(x => x.ReturnDate != null),
            "overdue" => query.Where(x => x.ReturnDate == null && x.DueDate < today),
            _ => query
        };
        // 所有排序都有唯一编号作为次级顺序，日期相同仍可重复得到稳定结果。
        query = filters.Sort switch
        {
            "borrow" => query.OrderBy(x => x.BorrowDate).ThenBy(x => x.Id),
            "due" => query.OrderBy(x => x.DueDate).ThenBy(x => x.Id),
            "due_desc" => query.OrderByDescending(x => x.DueDate).ThenByDescending(x => x.Id),
            _ => query.OrderByDescending(x => x.BorrowDate).ThenByDescending(x => x.Id)};
        // 先让数据库完成过滤排序，状态文案在内存按同一个业务日期生成。
        var records = await query.Select(x => new { x.Id, x.Book.Title, x.BorrowDate, x.DueDate, x.ReturnDate, ReaderName = readerId.HasValue ? null : x.Reader.Name }).ToListAsync();
        var rows = records.Select(x => new LoanRow(x.Id, x.Title, x.BorrowDate, x.DueDate, x.ReturnDate, LendingRules.LoanStatus(x.DueDate, x.ReturnDate, today), x.ReaderName)).ToList();
        return new LoanListPage(rows, filters, readerId.HasValue);
    }
}
