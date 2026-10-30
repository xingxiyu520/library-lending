using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
namespace Library.Web.Models;
public class LoginInput {
 [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
 [Required, DataType(DataType.Password), StringLength(256)] public string Password { get; set; } = "";
 public string? ReturnUrl { get; set; }
}
public class BookInput {
 [Required, StringLength(32), Display(Name = "ISBN")] public string Isbn { get; set; } = "";
 [Required, StringLength(200)] public string Title { get; set; } = "";
 [Required, StringLength(100)] public string Author { get; set; } = "";
 [StringLength(60)] public string? Category { get; set; }
 [Required, Range(0, int.MaxValue), Display(Name = "Total copies")] public int? TotalCopies { get; set; }
}
public class ReaderInput {
 [Required, StringLength(100)] public string Name { get; set; } = "";
 [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
 [StringLength(30)] public string? Phone { get; set; }
 [Required, StringLength(256, MinimumLength = 8), RegularExpression(@"(?=.*[A-Za-z])(?=.*[0-9]).+", ErrorMessage = "Password must contain a letter and a number."), DataType(DataType.Password), Display(Name = "Initial password")] public string Password { get; set; } = "";
}
public class LoanInput {
 [Required, Range(1, int.MaxValue), Display(Name = "Book")] public int? BookId { get; set; }
 [Required, Range(1, int.MaxValue), Display(Name = "Reader")] public int? ReaderId { get; set; }
 [Required, DataType(DataType.Date), Display(Name = "Borrow date")] public DateOnly? BorrowDate { get; set; }
 [Required, DataType(DataType.Date), Display(Name = "Due date")] public DateOnly? DueDate { get; set; }
 [Required, StringLength(36)] public string RequestId { get; set; } = "";
 [ValidateNever] public List<SelectListItem> Books { get; set; } = [];
 [ValidateNever] public List<SelectListItem> Readers { get; set; } = [];
}
public record BookRow(int Id, string Isbn, string Title, string Author, string? Category, int TotalCopies, int AvailableCopies);
