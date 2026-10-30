using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
namespace Library.Web.Models;
public class Book {
 public int Id { get; set; }
 [MaxLength(32)] public string Isbn { get; set; } = "";
 [MaxLength(200)] public string Title { get; set; } = "";
 [MaxLength(100)] public string Author { get; set; } = "";
 [MaxLength(60)] public string? Category { get; set; }
 public int TotalCopies { get; set; }
 public List<Loan> Loans { get; set; } = [];
}
public class Reader {
 public int Id { get; set; }
 [MaxLength(255)] public string UserId { get; set; } = "";
 public IdentityUser User { get; set; } = null!;
 [MaxLength(100)] public string Name { get; set; } = "";
 [MaxLength(256)] public string Email { get; set; } = "";
 [MaxLength(30)] public string? Phone { get; set; }
}
public class Loan {
 public int Id { get; set; } public int BookId { get; set; } public Book Book { get; set; } = null!;
 public int ReaderId { get; set; } public Reader Reader { get; set; } = null!;
 public DateOnly BorrowDate { get; set; } public DateOnly DueDate { get; set; } public DateOnly? ReturnDate { get; set; }
 [MaxLength(36)] public string RequestId { get; set; } = "";
}
