using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Library.Web.Data;
using Library.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
[assembly: DoNotParallelize]
namespace Library.Tests;
public sealed class TestEnvironment : IDisposable {
 private static readonly Lazy<TestEnvironment> Shared = new(() => new TestEnvironment());
 public static TestEnvironment Current => Shared.Value;
 public static void Cleanup() { if (Shared.IsValueCreated) Shared.Value.Dispose(); }
 public string Root { get; }
 public string Connection { get; }
 public string Database { get; } = "librarytest_" + Guid.NewGuid().ToString("N");
 public string Password { get; } = "Test" + Guid.NewGuid().ToString("N") + "9";
 public string Browser { get; }
 public WebApplicationFactory<Program> Factory { get; }
 private readonly string adminConnection;
 private Process? browserHost;
 private string? browserHostDirectory;
 private readonly Dictionary<string, string?> original = new();
 public string BrowserUrl { get; private set; } = "";
 private TestEnvironment() {
  var dir = new DirectoryInfo(AppContext.BaseDirectory);
  while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LibraryLending.sln"))) dir = dir.Parent;
  Root = dir?.FullName ?? throw new InvalidOperationException("Cannot locate LibraryLending.sln.");
  var settingsPath = Path.Combine(Root, ".local", "test-settings.json");
  using var settings = File.Exists(settingsPath) ? JsonDocument.Parse(File.ReadAllText(settingsPath)) : null;
  adminConnection = Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")
   ?? settings?.RootElement.GetProperty("Connection").GetString()
   ?? throw new InvalidOperationException("Run scripts/Configure-Local.ps1 or set LIBRARY_TEST_CONNECTION before Run All. See docs/TESTING.md.");
  Browser = Environment.GetEnvironmentVariable("LIBRARY_TEST_BROWSER") ?? (settings?.RootElement.TryGetProperty("Browser", out var browser) == true ? browser.GetString() : null) ?? "edge";
  using (var db = new MySqlConnection(adminConnection)) { db.Open(); using var command = db.CreateCommand(); command.CommandText = $"CREATE DATABASE {Database} CHARACTER SET utf8mb4"; command.ExecuteNonQuery(); }
  Connection = new MySqlConnectionStringBuilder(adminConnection) { Database = Database }.ConnectionString;
  foreach (var pair in AppSettings()) { original[pair.Key] = Environment.GetEnvironmentVariable(pair.Key); Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
  var temp = Path.Combine(Root, ".local", "test-temp"); Directory.CreateDirectory(temp);
  foreach (var key in new[] { "TEMP", "TMP" }) { original[key] = Environment.GetEnvironmentVariable(key); Environment.SetEnvironmentVariable(key, temp); }
  Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
  // Creating the client builds the host, applies migrations and seeds fictional accounts.
  using var client = Factory.CreateClient(new() { AllowAutoRedirect = false });
 }
 public Dictionary<string, string> AppSettings() => new() {
  ["ConnectionStrings__Library"] = Connection,
  ["Database__MigrateOnStartup"] = "true", ["Seed__Enabled"] = "true",
  ["Seed__AdminEmail"] = "admin@library.example", ["Seed__ReaderEmail"] = "reader@library.example",
  ["Seed__AdminPassword"] = Password, ["Seed__ReaderPassword"] = Password,
  ["ASPNETCORE_ENVIRONMENT"] = "Testing"
 };
 public IServiceScope Scope() => Factory.Services.CreateScope();
 public async Task<T> Use<T>(Func<LibraryService, LibraryDbContext, Task<T>> action) {
  using var scope = Scope(); return await action(scope.ServiceProvider.GetRequiredService<LibraryService>(), scope.ServiceProvider.GetRequiredService<LibraryDbContext>());
 }
 public async Task StartBrowserHost() {
  if (browserHost is not null) return;
  var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
  BrowserUrl = $"http://127.0.0.1:{port}";
  var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
  var buildDirectory = Path.Combine(Root, "src", "Library.Web", "bin", configuration, "net8.0");
  browserHostDirectory = Path.Combine(Root, ".local", "browser-host", Guid.NewGuid().ToString("N"));
  foreach(var file in Directory.EnumerateFiles(buildDirectory,"*",SearchOption.AllDirectories)) {
   var destination=Path.Combine(browserHostDirectory,Path.GetRelativePath(buildDirectory,file));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(file,destination);
  }
  var dll = Path.Combine(browserHostDirectory,"Library.Web.dll");
  var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.Combine(Root, "src", "Library.Web"), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
  start.ArgumentList.Add(dll); start.ArgumentList.Add("--urls"); start.ArgumentList.Add(BrowserUrl);
  foreach (var pair in AppSettings()) start.Environment[pair.Key] = pair.Value;
  browserHost = Process.Start(start) ?? throw new InvalidOperationException("Unable to start browser host.");
  browserHost.OutputDataReceived += (_, _) => { }; browserHost.ErrorDataReceived += (_, _) => { }; browserHost.BeginOutputReadLine(); browserHost.BeginErrorReadLine();
  using var client = new HttpClient();
  for (var attempt = 0; attempt < 120; attempt++) {
   if (browserHost.HasExited) throw new InvalidOperationException("Browser host exited before readiness.");
   try { if ((await client.GetAsync(BrowserUrl + "/Account/Login")).IsSuccessStatusCode) return; } catch (HttpRequestException) { }
   await Task.Delay(250);
  } throw new TimeoutException("Browser host did not become ready.");
 }
 public void Dispose() {
  if (browserHost is { HasExited: false }) { browserHost.Kill(entireProcessTree: true); browserHost.WaitForExit(5000); }
  browserHost?.Dispose(); Factory.Dispose(); MySqlConnection.ClearAllPools();
  using (var db = new MySqlConnection(adminConnection)) { db.Open(); using var command = db.CreateCommand(); command.CommandText = $"DROP DATABASE {Database}"; command.ExecuteNonQuery(); }
  foreach (var pair in original) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
 }
}
[TestClass] public class AssemblyLifecycle {
 [AssemblyCleanup] public static void Cleanup() => TestEnvironment.Cleanup();
}
