using System.ComponentModel.DataAnnotations;
using Library.Web.Models;
using Library.Web.Services;
namespace Library.Tests;
[TestClass, TestCategory("Unit")]
public class Sprint2RuleTests {
 [DataTestMethod][DataRow(-3, -3, true)][DataRow(-3, 0, true)][DataRow(-3, -4, false)][DataRow(-3, 1, false)]
 public void Return_dates_include_borrow_day_and_today(int borrow, int returned, bool allowed) {
  var today = new DateOnly(2026,11,2);
  Assert.AreEqual(allowed, LendingRules.ValidateReturn(today.AddDays(borrow), today.AddDays(returned), today) is null);
 }
 [DataTestMethod][DataRow(-1, false, "Overdue")][DataRow(0, false, "On loan")][DataRow(1, false, "On loan")][DataRow(-1, true, "Returned")]
 public void Loan_status_distinguishes_due_today_and_returned(int due, bool returned, string status) {
  var today = new DateOnly(2026,11,2);
  Assert.AreEqual(status, LendingRules.LoanStatus(today.AddDays(due), returned ? today : null, today));
 }
 [DataTestMethod][DataRow("", "ok@library.example", false)][DataRow("Reader", "invalid", false)][DataRow("Reader", "ok@library.example", true)]
 public void Editing_reader_requires_valid_name_and_email_without_new_password(string name, string email, bool valid) {
  var input = new ReaderEditInput { Name = name, Email = email };
  Assert.AreEqual(valid, Validator.TryValidateObject(input, new ValidationContext(input), [], true));
 }
 [TestMethod] public void Missing_return_date_is_rejected() {
  var input = new ReturnInput(); Assert.IsFalse(Validator.TryValidateObject(input, new ValidationContext(input), [], true));
 }
}
