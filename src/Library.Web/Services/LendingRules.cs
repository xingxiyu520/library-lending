namespace Library.Web.Services;
/// <summary>可独立测试的纯业务规则，集中处理ISBN、业务日期、库存和借阅状态。</summary>
public static class LendingRules
{
    /// <summary>移除空白和连字符并统一大小写，使展示输入具有一致的唯一性口径。</summary>
    public static string NormalizeIsbn(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c) && c != '-')).ToUpperInvariant();
    /// <summary>借出日不得晚于业务当天，应还日不得早于借出日。</summary>
    public static string? ValidateDates(DateOnly borrow, DateOnly due, DateOnly today) => borrow > today ? "Borrow date cannot be in the future." : due < borrow ? "Due date must be on or after borrow date." : null;
    /// <summary>实际归还日必须位于借出日与当前业务日期的闭区间内。</summary>
    public static string? ValidateReturn(DateOnly borrow, DateOnly returned, DateOnly today) => returned < borrow ? "Return date cannot be before borrow date." : returned > today ? "Return date cannot be in the future." : null;
    // 更正仍遵守借出/到期规则；已归还记录额外保护实际归还日期。
    /// <summary>复用借出日期规则，并保证更正后的借出日不晚于已有归还日。</summary>
    public static string? ValidateCorrection(DateOnly borrow, DateOnly due, DateOnly? returned, DateOnly today) => ValidateDates(borrow, due, today) ?? (returned.HasValue && borrow > returned.Value ? "Borrow date cannot be after the recorded return date." : null);
    /// <summary>已归还优先显示Returned；未归还且应还日早于当天才是Overdue。</summary>
    public static string LoanStatus(DateOnly due, DateOnly? returned, DateOnly today) => returned.HasValue ? "Returned" : due < today ? "Overdue" : "On loan";
    /// <summary>按总册数减未归还数计算可借数量，展示值不小于零。</summary>
    public static int AvailableCopies(int total, int outstanding) => Math.Max(0, total - outstanding);
}

/// <summary>将系统时间转换为统一UTC+8业务日期，避免服务器时区造成到期判断偏差。</summary>
public class BusinessClock(TimeProvider time)
{
    // 业务日期固定为UTC+8，不依赖服务器所在时区。
    /// <summary>按UTC+8计算的当前业务日期，作为日期校验和逾期状态基准。</summary>
    public DateOnly Today => DateOnly.FromDateTime(time.GetUtcNow().ToOffset(TimeSpan.FromHours(8)).DateTime);
}

/// <summary>业务结果载体；校验失败返回提示，成功时可携带已保存实体编号。</summary>
public record OperationResult(bool Success, string? Error = null, int? Id = null)
{
    /// <summary>构造明确的业务失败提示，不把校验失败伪装成保存成功。</summary>
    public static OperationResult Fail(string error) => new(false, error);
    /// <summary>构造成功业务结果，必要时返回已创建或更新的记录编号。</summary>
    public static OperationResult Ok(int id) => new(true, Id: id);
}
