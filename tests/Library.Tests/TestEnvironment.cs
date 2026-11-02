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
/// <summary>整套测试共用的隔离环境，创建随机测试库和密码，并在结束时清理自有资源。</summary>
public sealed class TestEnvironment : IDisposable
{
    /// <summary>延迟创建的程序集级测试环境，避免每个用例重复启动数据库与Host。</summary>
    private static readonly Lazy<TestEnvironment> Shared = new(() => new TestEnvironment());
    /// <summary>按需创建且由程序集共享的隔离测试环境。</summary>
    public static TestEnvironment Current => Shared.Value;

    /// <summary>测试结束时收集失败附件并释放浏览器，或在程序集结束时清理共享测试环境。</summary>
    public static void Cleanup()
    {
        if (Shared.IsValueCreated)
            Shared.Value.Dispose();
    }

    /// <summary>当前解决方案目录，只用于本地定位配置及测试结果。</summary>
    public string Root { get; }
    /// <summary>指向随机隔离测试库的连接字符串，禁止写入公共结果。</summary>
    public string Connection { get; }
    /// <summary>随机生成的librarytest_库名，避免修改其他测试或开发数据。</summary>
    public string Database { get; } = "librarytest_" + Guid.NewGuid().ToString("N");
    /// <summary>输入或测试时使用的密码；正式存储由Identity生成哈希，不写入公共仓库。</summary>
    public string Password { get; } = "Test" + Guid.NewGuid().ToString("N") + "9";
    /// <summary>当前测试选择的浏览器类型，本地默认Edge、CI可选择Chrome。</summary>
    public string Browser { get; }
    /// <summary>测试用WebApplicationFactory，使用真实MVC与Identity管道。</summary>
    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>仅供创建和删除随机隔离库的测试连接，不输出到公开日志。</summary>
    private readonly string adminConnection;
    /// <summary>当前套件启动的独立Kestrel进程，清理时只终止该进程。</summary>
    private Process? browserHost;
    /// <summary>当前测试构建的独立Host快照目录，避免构建覆盖正在运行的程序集。</summary>
    private string? browserHostDirectory;
    /// <summary>测试覆盖环境变量之前的原值，清理时恢复调用环境。</summary>
    private readonly Dictionary<string, string?> original = new();
    /// <summary>由当前套件独立Host提供的回环地址，端口动态分配。</summary>
    public string BrowserUrl { get; private set; } = "";

    /// <summary>定位解决方案并读取忽略目录或环境变量中的测试连接，创建随机隔离库与虚构账号。</summary>
    private TestEnvironment()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LibraryLending.sln")))
            dir = dir.Parent;
        Root = dir?.FullName ?? throw new InvalidOperationException("Cannot locate LibraryLending.sln.");
        var settingsPath = Path.Combine(Root, ".local", "test-settings.json");
        using var settings = File.Exists(settingsPath) ? JsonDocument.Parse(File.ReadAllText(settingsPath)) : null;
        adminConnection = Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION") ?? settings?.RootElement.GetProperty("Connection").GetString() ?? throw new InvalidOperationException("Run scripts/Initialize-Local.ps1 or set LIBRARY_TEST_CONNECTION before Run All. See docs/TESTING.md.");
        Browser = Environment.GetEnvironmentVariable("LIBRARY_TEST_BROWSER") ?? (settings?.RootElement.TryGetProperty("Browser", out var browser) == true ? browser.GetString() : null) ?? "edge";
        // 只创建随机前缀的隔离库，测试不写入开发数据库。
        using (var db = new MySqlConnection(adminConnection))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = $"CREATE DATABASE {Database} CHARACTER SET utf8mb4";
            command.ExecuteNonQuery();
        }

        Connection = new MySqlConnectionStringBuilder(adminConnection)
        {
            Database = Database
        }.ConnectionString;
        // 为当前测试进程及其独立Host设置同一隔离配置，结束后恢复原环境。
        foreach (var pair in AppSettings())
        {
            original[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }

        var temp = Path.Combine(Root, ".local", "test-temp");
        Directory.CreateDirectory(temp);
        foreach (var key in new[]
        {
            "TEMP",
            "TMP"
        }

        )
        {
            original[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, temp);
        }

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
        // 创建客户端触发Host构建、迁移及虚构账号初始化。
        using var client = Factory.CreateClient(new() { AllowAutoRedirect = false });
    }

    /// <summary>为隔离Host提供同一测试连接和随机密码，避免使用开发库或生产凭据。</summary>
    public Dictionary<string, string> AppSettings() => new()
    {
        ["ConnectionStrings__Library"] = Connection,
        ["Database__MigrateOnStartup"] = "true",
        ["Seed__Enabled"] = "true",
        ["Seed__AdminEmail"] = "admin@library.example",
        ["Seed__ReaderEmail"] = "reader@library.example",
        ["Seed__AdminPassword"] = Password,
        ["Seed__ReaderPassword"] = Password,
        ["ASPNETCORE_ENVIRONMENT"] = "Testing"
    };
    /// <summary>为一次数据库操作创建独立依赖注入作用域，避免复用被跟踪的旧实体。</summary>
    public IServiceScope Scope() => Factory.Services.CreateScope();
    /// <summary>在独立作用域内调用业务服务和真实数据库，操作结束即释放上下文。</summary>
    public async Task<T> Use<T>(Func<LibraryService, LibraryDbContext, Task<T>> action)
    {
        using var scope = Scope();
        return await action(scope.ServiceProvider.GetRequiredService<LibraryService>(), scope.ServiceProvider.GetRequiredService<LibraryDbContext>());
    }

    /// <summary>复制当前构建为独立快照并启动回环Kestrel，等待登录页可用后供浏览器及JMeter访问。</summary>
    public async Task StartBrowserHost()
    {
        if (browserHost is not null)
            return;
        // 选择可用的回环端口；复制当前构建避免后续编译覆盖运行中的Host。
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        BrowserUrl = $"http://127.0.0.1:{port}";
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var buildDirectory = Path.Combine(Root, "src", "Library.Web", "bin", configuration, "net8.0");
        browserHostDirectory = Path.Combine(Root, ".local", "browser-host", Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.EnumerateFiles(buildDirectory, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(browserHostDirectory, Path.GetRelativePath(buildDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        var dll = Path.Combine(browserHostDirectory, "Library.Web.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.Combine(Root, "src", "Library.Web"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(dll);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add(BrowserUrl);
        foreach (var pair in AppSettings())
            start.Environment[pair.Key] = pair.Value;
        browserHost = Process.Start(start) ?? throw new InvalidOperationException("Unable to start browser host.");
        browserHost.OutputDataReceived += (_, _) =>
        {
        };
        browserHost.ErrorDataReceived += (_, _) =>
        {
        };
        browserHost.BeginOutputReadLine();
        browserHost.BeginErrorReadLine();
        using var client = new HttpClient();
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (browserHost.HasExited)
                throw new InvalidOperationException("Browser host exited before readiness.");
            try
            {
                if ((await client.GetAsync(BrowserUrl + "/Account/Login")).IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("Browser host did not become ready.");
    }

    // 仅重启当前套件持有的测试进程；数据库保留以检验持久化。
    /// <summary>只终止本套件拥有的Host并重新启动，保留数据库以检验跨进程持久化。</summary>
    public async Task RestartBrowserHost()
    {
        if (browserHost is { HasExited: false })
        {
            browserHost.Kill(entireProcessTree: true);
            await browserHost.WaitForExitAsync();
        }

        browserHost?.Dispose();
        browserHost = null;
        await StartBrowserHost();
    }

    /// <summary>释放当前对象持有的资源；测试环境同时撤销临时环境变量并删除自己的隔离数据库。</summary>
    public void Dispose()
    {
        if (browserHost is { HasExited: false })
        {
            browserHost.Kill(entireProcessTree: true);
            browserHost.WaitForExit(5000);
        }

        browserHost?.Dispose();
        Factory.Dispose();
        MySqlConnection.ClearAllPools();
        using (var db = new MySqlConnection(adminConnection))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = $"DROP DATABASE {Database}";
            command.ExecuteNonQuery();
        }

        foreach (var pair in original)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
    }
}

/// <summary>在MSTest程序集结束时清理独立Host、数据库及环境变量。</summary>
[TestClass]
public class AssemblyLifecycle
{
    /// <summary>测试结束时收集失败附件并释放浏览器，或在程序集结束时清理共享测试环境。</summary>
    [AssemblyCleanup]
    public static void Cleanup() => TestEnvironment.Cleanup();
}
