using Library.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Library.Web.Data;
/// <summary>同时维护Identity账号表和业务表，使账号与档案变更可以处于同一数据库事务。</summary>
public class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    /// <summary>图书查询集合或表单选项，实际权限由控制器和服务端查询保证。</summary>
    public DbSet<Book> Books => Set<Book>();
    /// <summary>读者查询集合或表单选项，读者端没有全量访问入口。</summary>
    public DbSet<Reader> Readers => Set<Reader>();
    /// <summary>当前实体关联的借阅集合，用于计算未归还数量。</summary>
    public DbSet<Loan> Loans => Set<Loan>();

    /// <summary>配置唯一索引、限制级外键与数据库检查约束，并保留Identity的基础模型。</summary>
    protected override void OnModelCreating(ModelBuilder b)
    {
        // 先保留Identity模型，再叠加业务唯一索引与数据库约束。
        base.OnModelCreating(b);
        b.Entity<Book>().HasIndex(x => x.Isbn).IsUnique();
        b.Entity<Book>().ToTable("Books", t => t.HasCheckConstraint("CK_Books_Copies", "`TotalCopies` >= 0"));
        b.Entity<IdentityUser>().HasIndex(x => x.NormalizedEmail).IsUnique();
        b.Entity<Reader>().HasIndex(x => x.UserId).IsUnique();
        b.Entity<Reader>().HasIndex(x => x.Email).IsUnique();
        // 限制级删除保留账号、图书、读者和历史借阅之间的引用完整性。
        b.Entity<Reader>().HasOne(x => x.User).WithOne().HasForeignKey<Reader>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Loan>().HasOne(x => x.Book).WithMany(x => x.Loans).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Loan>().HasOne(x => x.Reader).WithMany().HasForeignKey(x => x.ReaderId).OnDelete(DeleteBehavior.Restrict);
        // 唯一请求标识兜底并发幂等，日期检查约束兜底越过服务层的非法写入。
        b.Entity<Loan>().HasIndex(x => x.RequestId).IsUnique();
        b.Entity<Loan>().ToTable("Loans", t => t.HasCheckConstraint("CK_Loans_Dates", "`DueDate` >= `BorrowDate` AND (`ReturnDate` IS NULL OR `ReturnDate` >= `BorrowDate`)"));
    }
}
