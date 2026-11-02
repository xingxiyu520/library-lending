using Library.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Library.Web.Data;
/// <summary>幂等初始化虚构角色、演示账号及图书，密码只从外部配置读取。</summary>
public static class SeedData
{
    /// <summary>按配置幂等建立演示角色、虚构账号和初始书目，避免重启重复初始化。</summary>
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
    {
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var db = services.GetRequiredService<LibraryDbContext>();
        // 先补齐两个固定角色，重复启动不会重复创建。
        foreach (var role in new[]
        {
            "Admin",
            "Reader"
        }

        )
            if (!await roles.RoleExistsAsync(role))
                Check(await roles.CreateAsync(new IdentityRole(role)));
        foreach (var role in new[]
        {
            "Admin",
            "Reader"
        }

        )
        {
            var email = config[$"Seed:{role}Email"] ?? throw new InvalidOperationException("Seed email must be configured.");
            var password = config[$"Seed:{role}Password"] ?? throw new InvalidOperationException("Seed password must be configured.");
            if (await users.FindByEmailAsync(email)is not null)
                continue;
            // 一个演示账号与其角色、读者档案必须同时提交。
            await using var tx = await db.Database.BeginTransactionAsync();
            var user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };
            Check(await users.CreateAsync(user, password));
            Check(await users.AddToRoleAsync(user, role));
            if (role == "Reader")
                db.Readers.Add(new Reader { UserId = user.Id, Name = "Demo Reader", Email = email });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // 只在空馆藏中创建示例图书，保留已有数据。
        if (!await db.Books.AnyAsync())
        {
            db.Books.AddRange(new Book { Isbn = "9780132350884", Title = "Clean Code", Author = "Robert C. Martin", Category = "Software", TotalCopies = 3 }, new Book { Isbn = "9780201616224", Title = "The Pragmatic Programmer", Author = "Andrew Hunt and David Thomas", Category = "Software", TotalCopies = 1 }, new Book { Isbn = "9780321125217", Title = "Domain-Driven Design", Author = "Eric Evans", Category = "Software", TotalCopies = 0 });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>检查Identity操作结果，初始化失败时终止并仅报告错误代码。</summary>
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("Demo provisioning failed: " + string.Join(" ", result.Errors.Select(x => x.Code)));
    }
}
