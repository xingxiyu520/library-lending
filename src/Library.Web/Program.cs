using Library.Web.Data;
using Library.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false).AddEnvironmentVariables();
var connection = builder.Configuration.GetConnectionString("Library");
if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Configure ConnectionStrings:Library using user secrets, environment variables or appsettings.Local.json.");
builder.Services.AddDbContext<LibraryDbContext>(o => o.UseMySql(connection, new MySqlServerVersion(new Version(8, 4, 0))));
builder.Services.AddIdentity<IdentityUser, IdentityRole>(o => {
 o.User.RequireUniqueEmail = true; o.Password.RequiredLength = 8; o.Password.RequireDigit = true;
 o.Password.RequireLowercase = false; o.Password.RequireUppercase = false; o.Password.RequireNonAlphanumeric = false;
 o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
}).AddEntityFrameworkStores<LibraryDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o => {
 o.LoginPath = "/Account/Login"; o.AccessDeniedPath = "/Account/Denied"; o.Cookie.Name = "Library.Session";
 o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax; o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
});
// Revalidate on every request so deleted or changed reader accounts lose existing sessions immediately.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddSingleton(TimeProvider.System); builder.Services.AddScoped<BusinessClock>(); builder.Services.AddScoped<LibraryService>();
var app = builder.Build();
app.UseExceptionHandler("/Home/Error");
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")) app.UseHsts();
app.Use(async (context, next) => {
 context.Response.Headers["X-Content-Type-Options"] = "nosniff"; context.Response.Headers["Referrer-Policy"] = "same-origin";
 context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; frame-ancestors 'none'; form-action 'self'; base-uri 'self'";
 context.Response.Headers.CacheControl = "no-store"; await next();
});
app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup")) {
 using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.MigrateAsync();
}
if (app.Configuration.GetValue<bool>("Seed:Enabled")) {
 using var scope = app.Services.CreateScope(); await SeedData.InitializeAsync(scope.ServiceProvider, app.Configuration);
}
app.Run();
public partial class Program { }
