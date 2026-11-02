using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Library.Web.Data.Migrations
{
    /// <summary>初始数据库迁移，建立Identity、馆藏、读者与借阅表及约束。</summary>
    public partial class InitialLibrary : Migration
    {
        /// <summary>按依赖顺序创建账号和业务表，建立唯一索引、外键与日期及库存约束。</summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase().Annotation("MySql:CharSet", "utf8mb4");
            // 建立AspNetRoles表及其主键、外键和必要检查约束。
            migrationBuilder.CreateTable(
                name: "AspNetRoles", columns: table => new { Id = table.Column<string>(type: "varchar(255)", nullable: false).Annotation("MySql:CharSet", "utf8mb4"), Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true).Annotation("MySql:CharSet", "utf8mb4"), NormalizedName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true).Annotation("MySql:CharSet", "utf8mb4"), ConcurrencyStamp = table.Column<string>(type: "longtext", nullable: true).Annotation("MySql:CharSet", "utf8mb4") }, constraints: table =>
            {
                table.PrimaryKey("PK_AspNetRoles", x => x.Id);
            }).Annotation("MySql:CharSet", "utf8mb4");
            // 建立AspNetUsers表及其主键、外键和必要检查约束。
            migrationBuilder.CreateTable(
                name: "AspNetUsers", columns: table => new { Id = table.Column<string>(type: "varchar(255)", nullable: false).Annotation("MySql:CharSet", "utf8mb4"), UserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true).Annotation("MySql:CharSet", "utf8mb4"), NormalizedUserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true).Annotation("MySql:CharSet", "utf8mb4"), Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true).Annotation("MySql:CharSet", "utf8mb4"), NormalizedEmail = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true).Annotation("MySql:CharSet", "utf8mb4"), EmailConfirmed = table.Column<bool>(type: "tinyint(1)", nullable: false), PasswordHash = table.Column<string>(type: "longtext", nullable: true).Annotation("MySql:CharSet", "utf8mb4"), SecurityStamp = table.Column<string>(type: "longtext", nullable: true).Annotation("MySql:CharSet", "utf8mb4"), ConcurrencyStamp = table.Column<string>(type: "longtext", nullable: true).Annotation("MySql:CharSet", "utf8mb4"), PhoneNumber = table.Column<string>(type: "longtext", nullable: true).Annotation("MySql:CharSet", "utf8mb4"), PhoneNumberConfirmed = table.Column<bool>(type: "tinyint(1)", nullable: false), TwoFactorEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false), LockoutEnd = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true), LockoutEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false), AccessFailedCount = table.Column<int>(type: "int", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUsers", x => x.Id);
            }).Annotation("MySql:CharSet", "utf8mb4");
            // 建立Books表及其主键、外键和必要检查约束。
            migrationBuilder.CreateTable(
                name: "Books", columns: table => new { Id = table.Column<int>(type: "int", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn), Isbn = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false).Annotation("MySql:CharSet", "utf8mb4"), Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false).Annotation("MySql:CharSet", "utf8mb4"), Author = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false).Annotation("MySql:CharSet", "utf8mb4"), Category = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true).Annotation("MySql:CharSet", "utf8mb4"), TotalCopies = table.Column<int>(type: "int", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_Books", x => x.Id);
                table.CheckConstraint("CK_Books_Copies", "`TotalCopies` >= 0");
            }).Annotation("MySql:CharSet", "utf8mb4");
            // 建立AspNetRoleClaims表及其主键、外键和必要检查约束。
            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims", columns: table => new { Id = table.Column<int>(type: "int", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn), RoleId = table.Column<string>(type: "varchar(255)", nullable: false).Annotation("MySql:CharSet", "utf8mb4"), ClaimType = table.Column<string>(type: "longtext", nullable: true).Annotation("MySql:CharSet", "utf8mb4"), ClaimValue = table.Column<string>(type: "longtext", nullable: true).Annotation("MySql:CharSet", "utf8mb4") }, constraints: table =>
            {
                table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                table.ForeignKey(name: "FK_AspNetRoleClaims_AspNetRoles_RoleId", column: x => x.RoleId, principalTable: "AspNetRoles", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            }).Annotation("MySql:CharSet", "utf8mb4");
            // 建立AspNetUserClaims表及其主键、外键和必要检查约束。
            migrationBuilder.CreateTable(
                name: "AspNetUserClaims", columns: table => new { Id = table.Column<int>(type: "int", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn), UserId = table.Column<string>(type: "varchar(255)", nullable: false).Annotation("MySql:CharSet", "utf8mb4"), ClaimType = table.Column<string>(type: "longtext", nullable: true).Annotation("MySql:CharSet", "utf8mb4"), ClaimValue = table.Column<string>(type: "longtext", nullable: true).Annotation("MySql:CharSet", "utf8mb4") }, constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                table.ForeignKey(name: "FK_AspNetUserClaims_AspNetUsers_UserId", column: x => x.UserId, principalTable: "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            }).Annotation("MySql:CharSet", "utf8mb4");
            // 建立AspNetUserLogins表及其主键、外键和必要检查约束。
            migrationBuilder.CreateTable(
                name: "AspNetUserLogins", columns: table => new { LoginProvider = table.Column<string>(type: "varchar(255)", nullable: false).Annotation("MySql:CharSet", "utf8mb4"), ProviderKey = table.Column<string>(type: "varchar(255)", nullable: false).Annotation("MySql:CharSet", "utf8mb4"), ProviderDisplayName = table.Column<string>(type: "longtext", nullable: true).Annotation("MySql:CharSet", "utf8mb4"), UserId = table.Column<string>(type: "varchar(255)", nullable: false).Annotation("MySql:CharSet", "utf8mb4") }, constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                table.ForeignKey(name: "FK_AspNetUserLogins_AspNetUsers_UserId", column: x => x.UserId, principalTable: "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            }).Annotation("MySql:CharSet", "utf8mb4");
            // 建立AspNetUserRoles表及其主键、外键和必要检查约束。
            migrationBuilder.CreateTable(
                name: "AspNetUserRoles", columns: table => new { UserId = table.Column<string>(type: "varchar(255)", nullable: false).Annotation("MySql:CharSet", "utf8mb4"), RoleId = table.Column<string>(type: "varchar(255)", nullable: false).Annotation("MySql:CharSet", "utf8mb4") }, constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                table.ForeignKey(name: "FK_AspNetUserRoles_AspNetRoles_RoleId", column: x => x.RoleId, principalTable: "AspNetRoles", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey(name: "FK_AspNetUserRoles_AspNetUsers_UserId", column: x => x.UserId, principalTable: "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            }).Annotation("MySql:CharSet", "utf8mb4");
            // 建立AspNetUserTokens表及其主键、外键和必要检查约束。
            migrationBuilder.CreateTable(
                name: "AspNetUserTokens", columns: table => new { UserId = table.Column<string>(type: "varchar(255)", nullable: false).Annotation("MySql:CharSet", "utf8mb4"), LoginProvider = table.Column<string>(type: "varchar(255)", nullable: false).Annotation("MySql:CharSet", "utf8mb4"), Name = table.Column<string>(type: "varchar(255)", nullable: false).Annotation("MySql:CharSet", "utf8mb4"), Value = table.Column<string>(type: "longtext", nullable: true).Annotation("MySql:CharSet", "utf8mb4") }, constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                table.ForeignKey(name: "FK_AspNetUserTokens_AspNetUsers_UserId", column: x => x.UserId, principalTable: "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            }).Annotation("MySql:CharSet", "utf8mb4");
            // 建立Readers表及其主键、外键和必要检查约束。
            migrationBuilder.CreateTable(
                name: "Readers", columns: table => new { Id = table.Column<int>(type: "int", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn), UserId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false).Annotation("MySql:CharSet", "utf8mb4"), Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false).Annotation("MySql:CharSet", "utf8mb4"), Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false).Annotation("MySql:CharSet", "utf8mb4"), Phone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true).Annotation("MySql:CharSet", "utf8mb4") }, constraints: table =>
            {
                table.PrimaryKey("PK_Readers", x => x.Id);
                table.ForeignKey(name: "FK_Readers_AspNetUsers_UserId", column: x => x.UserId, principalTable: "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            }).Annotation("MySql:CharSet", "utf8mb4");
            // 建立Loans表及其主键、外键和必要检查约束。
            migrationBuilder.CreateTable(
                name: "Loans", columns: table => new { Id = table.Column<int>(type: "int", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn), BookId = table.Column<int>(type: "int", nullable: false), ReaderId = table.Column<int>(type: "int", nullable: false), BorrowDate = table.Column<DateOnly>(type: "date", nullable: false), DueDate = table.Column<DateOnly>(type: "date", nullable: false), ReturnDate = table.Column<DateOnly>(type: "date", nullable: true), RequestId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false).Annotation("MySql:CharSet", "utf8mb4") }, constraints: table =>
            {
                table.PrimaryKey("PK_Loans", x => x.Id);
                table.CheckConstraint("CK_Loans_Dates", "`DueDate` >= `BorrowDate` AND (`ReturnDate` IS NULL OR `ReturnDate` >= `BorrowDate`)");
                table.ForeignKey(name: "FK_Loans_Books_BookId", column: x => x.BookId, principalTable: "Books", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "FK_Loans_Readers_ReaderId", column: x => x.ReaderId, principalTable: "Readers", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            }).Annotation("MySql:CharSet", "utf8mb4");
            // 索引IX_AspNetRoleClaims_RoleId用于关系查询或唯一性约束。
            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId", table: "AspNetRoleClaims", column: "RoleId");
            // 索引RoleNameIndex用于关系查询或唯一性约束。
            migrationBuilder.CreateIndex(
                name: "RoleNameIndex", table: "AspNetRoles", column: "NormalizedName", unique: true);
            // 索引IX_AspNetUserClaims_UserId用于关系查询或唯一性约束。
            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId", table: "AspNetUserClaims", column: "UserId");
            // 索引IX_AspNetUserLogins_UserId用于关系查询或唯一性约束。
            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId", table: "AspNetUserLogins", column: "UserId");
            // 索引IX_AspNetUserRoles_RoleId用于关系查询或唯一性约束。
            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId", table: "AspNetUserRoles", column: "RoleId");
            // 索引EmailIndex用于关系查询或唯一性约束。
            migrationBuilder.CreateIndex(
                name: "EmailIndex", table: "AspNetUsers", column: "NormalizedEmail", unique: true);
            // 索引UserNameIndex用于关系查询或唯一性约束。
            migrationBuilder.CreateIndex(
                name: "UserNameIndex", table: "AspNetUsers", column: "NormalizedUserName", unique: true);
            // 索引IX_Books_Isbn用于关系查询或唯一性约束。
            migrationBuilder.CreateIndex(
                name: "IX_Books_Isbn", table: "Books", column: "Isbn", unique: true);
            // 索引IX_Loans_BookId用于关系查询或唯一性约束。
            migrationBuilder.CreateIndex(
                name: "IX_Loans_BookId", table: "Loans", column: "BookId");
            // 索引IX_Loans_ReaderId用于关系查询或唯一性约束。
            migrationBuilder.CreateIndex(
                name: "IX_Loans_ReaderId", table: "Loans", column: "ReaderId");
            // 索引IX_Loans_RequestId用于关系查询或唯一性约束。
            migrationBuilder.CreateIndex(
                name: "IX_Loans_RequestId", table: "Loans", column: "RequestId", unique: true);
            // 索引IX_Readers_Email用于关系查询或唯一性约束。
            migrationBuilder.CreateIndex(
                name: "IX_Readers_Email", table: "Readers", column: "Email", unique: true);
            // 索引IX_Readers_UserId用于关系查询或唯一性约束。
            migrationBuilder.CreateIndex(
                name: "IX_Readers_UserId", table: "Readers", column: "UserId", unique: true);
        }

        /// <summary>按反向依赖顺序移除本迁移创建的表，只用于明确要求的数据库回滚。</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AspNetRoleClaims");
            migrationBuilder.DropTable(name: "AspNetUserClaims");
            migrationBuilder.DropTable(name: "AspNetUserLogins");
            migrationBuilder.DropTable(name: "AspNetUserRoles");
            migrationBuilder.DropTable(name: "AspNetUserTokens");
            migrationBuilder.DropTable(name: "Loans");
            migrationBuilder.DropTable(name: "AspNetRoles");
            migrationBuilder.DropTable(name: "Books");
            migrationBuilder.DropTable(name: "Readers");
            migrationBuilder.DropTable(name: "AspNetUsers");
        }
    }
}
