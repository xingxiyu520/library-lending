using Library.Web.Data;
using Library.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// 创建Host并合并本机覆盖配置与环境变量，真实连接信息不进入源代码。
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false).AddEnvironmentVariables();
var connection = builder.Configuration.GetConnectionString("Library");
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("Configure ConnectionStrings:Library using user secrets, environment variables or appsettings.Local.json.");
// MySQL上下文与Identity共用同一事务边界。
builder.Services.AddDbContext<LibraryDbContext>(o => o.UseMySql(connection, new MySqlServerVersion(new Version(8, 4, 0))));
// 密码、唯一邮箱和失败锁定规则统一由Identity执行。
builder.Services.AddIdentity<IdentityUser, IdentityRole>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 8;
    o.Password.RequireDigit = true;
    o.Password.RequireLowercase = false;
    o.Password.RequireUppercase = false;
    o.Password.RequireNonAlphanumeric = false;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
}).AddEntityFrameworkStores<LibraryDbContext>().AddDefaultTokenProviders();
// Cookie仅供本站会话，设置HttpOnly、SameSite和有效期。
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/Denied";
    o.Cookie.Name = "Library.Session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
});
// 每次请求重验安全戳，读者账号删除或更正后旧会话立即失效。
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
// 全局验证写请求的防伪令牌；控制器还显式标记写入口以便安全扫描。
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
// 注册统一业务时钟、事务服务与借阅查询服务。
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<BusinessClock>();
builder.Services.AddScoped<LibraryService>();
builder.Services.AddScoped<LoanQueryService>();
// 统一错误页、安全响应头和认证中间件按顺序组成请求管道。
var app = builder.Build();
app.UseExceptionHandler("/Home/Error");
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
    app.UseHsts();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; frame-ancestors 'none'; form-action 'self'; base-uri 'self'";
    context.Response.Headers.CacheControl = "no-store";
    await next();
});
// 静态资源先处理；动态路由在认证后由控制器执行角色授权。
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
// 数据库迁移只在显式配置启用时运行，随后可幂等准备虚构演示数据。
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.MigrateAsync();
}

if (app.Configuration.GetValue<bool>("Seed:Enabled"))
{
    using var scope = app.Services.CreateScope();
    await SeedData.InitializeAsync(scope.ServiceProvider, app.Configuration);
}

app.Run();
/// <summary>提供WebApplicationFactory可访问的应用入口，生产启动仍由顶层语句执行。</summary>
public partial class Program
{
}
