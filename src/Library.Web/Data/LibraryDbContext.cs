using Library.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace Library.Web.Data;
public class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : IdentityDbContext<IdentityUser>(options) {
 public DbSet<Book> Books => Set<Book>(); public DbSet<Reader> Readers => Set<Reader>(); public DbSet<Loan> Loans => Set<Loan>();
 protected override void OnModelCreating(ModelBuilder b) {
  base.OnModelCreating(b);
  b.Entity<Book>().HasIndex(x => x.Isbn).IsUnique();
  b.Entity<Book>().ToTable("Books", t => t.HasCheckConstraint("CK_Books_Copies", "`TotalCopies` >= 0"));
  b.Entity<IdentityUser>().HasIndex(x => x.NormalizedEmail).IsUnique();
  b.Entity<Reader>().HasIndex(x => x.UserId).IsUnique(); b.Entity<Reader>().HasIndex(x => x.Email).IsUnique();
  b.Entity<Reader>().HasOne(x => x.User).WithOne().HasForeignKey<Reader>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Loan>().HasOne(x => x.Book).WithMany(x => x.Loans).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Loan>().HasOne(x => x.Reader).WithMany().HasForeignKey(x => x.ReaderId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Loan>().HasIndex(x => x.RequestId).IsUnique();
  b.Entity<Loan>().ToTable("Loans", t => t.HasCheckConstraint("CK_Loans_Dates", "`DueDate` >= `BorrowDate` AND (`ReturnDate` IS NULL OR `ReturnDate` >= `BorrowDate`)"));
 }
}
