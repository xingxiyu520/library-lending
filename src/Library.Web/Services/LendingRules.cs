namespace Library.Web.Services;
public static class LendingRules {
 public static string NormalizeIsbn(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c) && c != '-')).ToUpperInvariant();
 public static string? ValidateDates(DateOnly borrow, DateOnly due, DateOnly today) => borrow > today ? "Borrow date cannot be in the future." : due < borrow ? "Due date must be on or after borrow date." : null;
 public static int AvailableCopies(int total, int outstanding) => Math.Max(0, total - outstanding);
}
public class BusinessClock(TimeProvider time) {
 // Fixed UTC+8 business time is independent of the web server's local timezone.
 public DateOnly Today => DateOnly.FromDateTime(time.GetUtcNow().ToOffset(TimeSpan.FromHours(8)).DateTime);
}
public record OperationResult(bool Success, string? Error = null, int? Id = null) {
 public static OperationResult Fail(string error) => new(false, error);
 public static OperationResult Ok(int id) => new(true, Id: id);
}
