using System.ComponentModel.DataAnnotations;
using Library.Web.Models;
using Library.Web.Services;

namespace Library.Tests;
/// <summary>归还日期、状态和读者编辑输入的纯规则边界测试。</summary>
[TestClass, TestCategory("Unit")]
public class Sprint2RuleTests
{
    /// <summary>验证归还日期的闭区间允许借出当天和当前业务当天。</summary>
    [DataTestMethod]
    [DataRow(-3, -3, true)]
    [DataRow(-3, 0, true)]
    [DataRow(-3, -4, false)]
    [DataRow(-3, 1, false)]
    public void Return_dates_include_borrow_day_and_today(int borrow, int returned, bool allowed)
    {
        var today = new DateOnly(2026, 11, 2);
        Assert.AreEqual(allowed, LendingRules.ValidateReturn(today.AddDays(borrow), today.AddDays(returned), today)is null);
    }

    /// <summary>验证应还日当天仍为未逾期，实际归还优先于应还日显示。</summary>
    [DataTestMethod]
    [DataRow(-1, false, "Overdue")]
    [DataRow(0, false, "On loan")]
    [DataRow(1, false, "On loan")]
    [DataRow(-1, true, "Returned")]
    public void Loan_status_distinguishes_due_today_and_returned(int due, bool returned, string status)
    {
        var today = new DateOnly(2026, 11, 2);
        Assert.AreEqual(status, LendingRules.LoanStatus(today.AddDays(due), returned ? today : null, today));
    }

    /// <summary>验证读者编辑必须有姓名和合法邮箱，但不要求重新提供密码。</summary>
    [DataTestMethod]
    [DataRow("", "ok@library.example", false)]
    [DataRow("Reader", "invalid", false)]
    [DataRow("Reader", "ok@library.example", true)]
    public void Editing_reader_requires_valid_name_and_email_without_new_password(string name, string email, bool valid)
    {
        var input = new ReaderEditInput
        {
            Name = name,
            Email = email
        };
        Assert.AreEqual(valid, Validator.TryValidateObject(input, new ValidationContext(input), [], true));
    }

    /// <summary>验证归还表单的空日期被DataAnnotations拒绝。</summary>
    [TestMethod]
    public void Missing_return_date_is_rejected()
    {
        var input = new ReturnInput();
        Assert.IsFalse(Validator.TryValidateObject(input, new ValidationContext(input), [], true));
    }
}
