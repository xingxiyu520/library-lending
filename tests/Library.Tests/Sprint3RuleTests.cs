using System.ComponentModel.DataAnnotations;
using Library.Web.Models;
using Library.Web.Services;

namespace Library.Tests;
// 日期更正和筛选白名单的纯规则边界，不需要启动浏览器。
/// <summary>更正日期以及查询白名单的边界测试。</summary>
[TestClass, TestCategory("Unit")]
public class Sprint3RuleTests
{
    /// <summary>覆盖更正日期的当天、未来、应还顺序及既有归还边界。</summary>
    [DataTestMethod]
    [DataRow(-3, -2, -1, true)]
    [DataRow(-1, 0, -1, true)]
    [DataRow(0, 1, -1, false)]
    [DataRow(1, 2, -1, false)]
    [DataRow(-1, -2, -1, false)]
    public void Correction_protects_due_today_and_recorded_return(int borrow, int due, int returned, bool valid)
    {
        var today = new DateOnly(2026, 11, 2);
        Assert.AreEqual(valid, LendingRules.ValidateCorrection(today.AddDays(borrow), today.AddDays(due), today.AddDays(returned), today)is null);
    }

    /// <summary>验证全部、未还、已还与逾期四种状态正确保留。</summary>
    [DataTestMethod]
    [DataRow("all")]
    [DataRow("outstanding")]
    [DataRow("returned")]
    [DataRow("overdue")]
    public void Filters_keep_supported_statuses(string status) => Assert.AreEqual(status, LoanFilters.Normalize("  title  ", status, "due").Status);
    /// <summary>验证四种允许的日期排序在规范化后不被错误重置。</summary>
    [DataTestMethod]
    [DataRow("borrow")]
    [DataRow("borrow_desc")]
    [DataRow("due")]
    [DataRow("due_desc")]
    public void Filters_keep_supported_sorts(string sort) => Assert.AreEqual(sort, LoanFilters.Normalize("", "all", sort).Sort);
    /// <summary>验证非法状态和排序回退白名单默认值，关键词仅作为数据。</summary>
    [TestMethod]
    public void Invalid_filters_fall_back_without_interpreting_SQL()
    {
        var filters = LoanFilters.Normalize("  ' OR 1=1 --  ", "<script>", "DROP TABLE Loans");
        Assert.AreEqual("' OR 1=1 --", filters.Query);
        Assert.AreEqual("all", filters.Status);
        Assert.AreEqual("borrow_desc", filters.Sort);
    }

    /// <summary>使用同一输入对象和ValidationContext核对更正日期必填。</summary>
    [TestMethod]
    public void Missing_correction_dates_are_rejected()
    {
        var input = new LoanEditInput();
        Assert.IsFalse(Validator.TryValidateObject(input, new(input), [], true));
    }
}
