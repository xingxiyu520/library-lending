using System.ComponentModel.DataAnnotations;
using Library.Web.Models;
using Library.Web.Services;

namespace Library.Tests;
/// <summary>借出规则、ISBN、库存和输入验证的纯单元回归。</summary>
[TestClass, TestCategory("Unit")]
public class RuleTests
{
    /// <summary>验证ISBN规范化移除空白和连字符并统一大小写。</summary>
    [DataTestMethod]
    [DataRow(" 978-0-13 235088-4 ", "9780132350884")]
    [DataRow(" abc-X ", "ABCX")]
    [DataRow("---   ", "")]
    public void Isbn_normalization_removes_separators(string input, string expected) => Assert.AreEqual(expected, LendingRules.NormalizeIsbn(input));
    /// <summary>验证可借数的正常、零库存和异常边界不产生负展示值。</summary>
    [DataTestMethod]
    [DataRow(3, 0, 3)]
    [DataRow(3, 2, 1)]
    [DataRow(0, 0, 0)]
    [DataRow(1, 2, 0)]
    public void Availability_obeys_boundaries(int total, int outstanding, int expected) => Assert.AreEqual(expected, LendingRules.AvailableCopies(total, outstanding));
    /// <summary>覆盖历史借出、当天及未来借出和应还日期顺序。</summary>
    [DataTestMethod]
    [DataRow(0, 0, true)]
    [DataRow(-10, -5, true)]
    [DataRow(0, -1, false)]
    [DataRow(1, 2, false)]
    public void Lending_dates_support_history_and_reject_future(int borrowOffset, int dueOffset, bool allowed)
    {
        var today = new DateOnly(2026, 10, 31);
        Assert.AreEqual(allowed, LendingRules.ValidateDates(today.AddDays(borrowOffset), today.AddDays(dueOffset), today)is null);
    }

    /// <summary>验证总册数允许零与合法整数，拒绝负值。</summary>
    [DataTestMethod]
    [DataRow(-1, false)]
    [DataRow(0, true)]
    [DataRow(1, true)]
    public void Copy_validation_has_nonnegative_boundary(int quantity, bool allowed)
    {
        var book = new BookInput
        {
            Isbn = "test",
            Title = "Title",
            Author = "Author",
            TotalCopies = quantity
        };
        Assert.AreEqual(allowed, Validator.TryValidateObject(book, new ValidationContext(book), [], true));
    }

    /// <summary>验证缺失总册数不能被错误当作零册保存。</summary>
    [TestMethod]
    public void Missing_copies_is_rejected()
    {
        var book = new BookInput
        {
            Isbn = "test",
            Title = "Title",
            Author = "Author"
        };
        Assert.IsFalse(Validator.TryValidateObject(book, new ValidationContext(book), [], true));
    }

    /// <summary>提交全为空白的书名，验证服务端必填校验阻止保存。</summary>
    [TestMethod]
    public void Whitespace_title_is_rejected()
    {
        var book = new BookInput
        {
            Isbn = "test",
            Title = "  ",
            Author = "Author",
            TotalCopies = 1
        };
        Assert.IsFalse(Validator.TryValidateObject(book, new ValidationContext(book), [], true));
    }

    /// <summary>使用跨UTC日界时间核对统一UTC+8业务日期。</summary>
    [TestMethod]
    public void Business_date_uses_UTC_plus_eight_at_midnight()
    {
        var clock = new BusinessClock(new FixedTime(new DateTimeOffset(2026, 10, 30, 16, 0, 0, TimeSpan.Zero)));
        Assert.AreEqual(new DateOnly(2026, 10, 31), clock.Today);
    }

    /// <summary>覆盖仅字母、仅数字及混合密码，验证读者初始密码规则。</summary>
    [DataTestMethod]
    [DataRow("12345678", false)]
    [DataRow("ABCDEFGH", false)]
    [DataRow("Abcdefg9", true)]
    public void Reader_password_requires_letters_and_digits(string password, bool allowed)
    {
        var input = new ReaderInput
        {
            Name = "Reader",
            Email = "reader@library.example",
            Password = password
        };
        Assert.AreEqual(allowed, Validator.TryValidateObject(input, new ValidationContext(input), [], true));
    }

    /// <summary>提供固定时钟的测试替身，使UTC+8跨日边界能够确定性复现。</summary>
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        /// <summary>返回固定UTC时刻供业务时钟测试复用，避免测试依赖运行当天。</summary>
        public override DateTimeOffset GetUtcNow() => now;
    }
}
