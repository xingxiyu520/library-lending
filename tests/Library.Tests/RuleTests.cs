using System.ComponentModel.DataAnnotations;
using Library.Web.Models;
using Library.Web.Services;
namespace Library.Tests;
[TestClass, TestCategory("Unit")]
public class RuleTests {
 [DataTestMethod]
 [DataRow(" 978-0-13 235088-4 ", "9780132350884")]
 [DataRow(" abc-X ", "ABCX")]
 [DataRow("---   ", "")]
 public void Isbn_normalization_removes_separators(string input, string expected) => Assert.AreEqual(expected, LendingRules.NormalizeIsbn(input));
 [DataTestMethod]
 [DataRow(3, 0, 3)][DataRow(3, 2, 1)][DataRow(0, 0, 0)][DataRow(1, 2, 0)]
 public void Availability_obeys_boundaries(int total, int outstanding, int expected) => Assert.AreEqual(expected, LendingRules.AvailableCopies(total, outstanding));
 [DataTestMethod]
 [DataRow(0, 0, true)][DataRow(-10, -5, true)][DataRow(0, -1, false)][DataRow(1, 2, false)]
 public void Lending_dates_support_history_and_reject_future(int borrowOffset, int dueOffset, bool allowed) {
  var today = new DateOnly(2026, 10, 31); Assert.AreEqual(allowed, LendingRules.ValidateDates(today.AddDays(borrowOffset), today.AddDays(dueOffset), today) is null);
 }
 [DataTestMethod][DataRow(-1, false)][DataRow(0, true)][DataRow(1, true)]
 public void Copy_validation_has_nonnegative_boundary(int quantity, bool allowed) {
  var book = new BookInput { Isbn = "test", Title = "Title", Author = "Author", TotalCopies = quantity };
  Assert.AreEqual(allowed, Validator.TryValidateObject(book, new ValidationContext(book), [], true));
 }
 [TestMethod] public void Missing_copies_is_rejected() {
  var book = new BookInput { Isbn = "test", Title = "Title", Author = "Author" };
  Assert.IsFalse(Validator.TryValidateObject(book, new ValidationContext(book), [], true));
 }
 [TestMethod] public void Whitespace_title_is_rejected() {
  var book = new BookInput { Isbn = "test", Title = "  ", Author = "Author", TotalCopies = 1 };
  Assert.IsFalse(Validator.TryValidateObject(book, new ValidationContext(book), [], true));
 }
 [TestMethod] public void Business_date_uses_UTC_plus_eight_at_midnight() {
  var clock = new BusinessClock(new FixedTime(new DateTimeOffset(2026, 10, 30, 16, 0, 0, TimeSpan.Zero)));
  Assert.AreEqual(new DateOnly(2026, 10, 31), clock.Today);
 }
 [DataTestMethod][DataRow("12345678",false)][DataRow("ABCDEFGH",false)][DataRow("Abcdefg9",true)]
 public void Reader_password_requires_letters_and_digits(string password,bool allowed) {
  var input=new ReaderInput{Name="Reader",Email="reader@library.example",Password=password};
  Assert.AreEqual(allowed,Validator.TryValidateObject(input,new ValidationContext(input),[],true));
 }
 private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
