using Library.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Library.Web.Data;
public static class SeedData {
 public static async Task InitializeAsync(IServiceProvider services, IConfiguration config) {
  var users = services.GetRequiredService<UserManager<IdentityUser>>(); var roles = services.GetRequiredService<RoleManager<IdentityRole>>(); var db = services.GetRequiredService<LibraryDbContext>();
  foreach (var role in new[] { "Admin", "Reader" }) if (!await roles.RoleExistsAsync(role)) Check(await roles.CreateAsync(new IdentityRole(role)));
  foreach (var role in new[] { "Admin", "Reader" }) {
   var email = config[$"Seed:{role}Email"] ?? throw new InvalidOperationException("Seed email must be configured.");
   var password = config[$"Seed:{role}Password"] ?? throw new InvalidOperationException("Seed password must be configured.");
   if (await users.FindByEmailAsync(email) is not null) continue;
   await using var tx = await db.Database.BeginTransactionAsync();
   var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
   Check(await users.CreateAsync(user, password)); Check(await users.AddToRoleAsync(user, role));
   if (role == "Reader") db.Readers.Add(new Reader { UserId = user.Id, Name = "Demo Reader", Email = email });
   await db.SaveChangesAsync(); await tx.CommitAsync();
  }
  if (!await db.Books.AnyAsync()) {
   db.Books.AddRange(new Book { Isbn = "9780132350884", Title = "Clean Code", Author = "Robert C. Martin", Category = "Software", TotalCopies = 3 },
    new Book { Isbn = "9780201616224", Title = "The Pragmatic Programmer", Author = "Andrew Hunt and David Thomas", Category = "Software", TotalCopies = 1 },
    new Book { Isbn = "9780321125217", Title = "Domain-Driven Design", Author = "Eric Evans", Category = "Software", TotalCopies = 0 });
   await db.SaveChangesAsync();
  }
 }
 private static void Check(IdentityResult result) { if (!result.Succeeded) throw new InvalidOperationException("Demo provisioning failed: " + string.Join(" ", result.Errors.Select(x => x.Code))); }
}
