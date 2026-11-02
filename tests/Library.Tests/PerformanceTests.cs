using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace Library.Tests;
// 1/5/10并发分档，每档5秒逐步启动+至少60秒稳定区间，Run All会实际运行全部档位。
/// <summary>按1、5、10独立读者执行JMeter分档，每档稳定采样不少于60秒并保存实际指标。</summary>
[TestClass, TestCategory("Load"), TestCategory("Performance")]
public class PerformanceTests
{
    /// <summary>MSTest上下文，用于把原始结果附件留在本地测试记录中。</summary>
    public TestContext TestContext { get; set; } = null !;

    /// <summary>按参数执行独立读者会话的70秒负载，检查稳定区间、数据隔离和实际性能指标。</summary>
    [DataTestMethod]
    [DataRow(1, DisplayName = "ReaderLoad_1")]
    [DataRow(5, DisplayName = "ReaderLoad_5")]
    [DataRow(10, DisplayName = "ReaderLoad_10")]
    public async Task Independent_readers_complete_staged_workload_without_errors(int users)
    {
        var env = TestEnvironment.Current;
        await env.StartBrowserHost();
        var home = Environment.GetEnvironmentVariable("LIBRARY_JMETER_HOME") ?? Path.Combine(env.Root, ".local", "tools", "apache-jmeter-5.6.3");
        var jar = Path.Combine(home, "bin", "ApacheJMeter.jar");
        Assert.IsTrue(File.Exists(jar), "Run scripts/Install-JMeter.ps1 with Java17/21 before Run All.");
        var folder = Path.Combine(env.Root, "TestResults", "performance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var properties = Path.Combine(folder, "session.properties");
        var jtl = Path.Combine(folder, "staged.jtl");
        var log = Path.Combine(folder, "jmeter.log");
        var accounts = new List<(string Email, string Marker, int Loan, int Book)>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        // 至少两个账号用于反向归属断言；即使1并发也核对另一读者的记录不可见。
        for (var i = 0; i < Math.Max(2, users); i++)
        {
            var marker = "Load" + Guid.NewGuid().ToString("N");
            var email = marker.ToLowerInvariant() + "@library.example";
            var reader = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = "Load reader " + i, Email = email, Password = env.Password }));
            var book = await env.Use((s, _) => s.AddBookAsync(new() { Isbn = Guid.NewGuid().ToString("N"), Title = marker, Author = "Performance", TotalCopies = 2 }));
            var loan = await env.Use((s, _) => s.LendAsync(new() { BookId = book.Id, ReaderId = reader.Id, BorrowDate = today, DueDate = today.AddDays(7), RequestId = Guid.NewGuid().ToString() }));
            Assert.IsTrue(reader.Success && book.Success && loan.Success);
            accounts.Add((email, marker, loan.Id!.Value, book.Id!.Value));
        }

        var dataSize = await env.Use(async (_, db) => new { Books = await db.Books.CountAsync(), Readers = await db.Readers.CountAsync(), Loans = await db.Loans.CountAsync() });
        var config = new StringBuilder($"host=127.0.0.1\nport={new Uri(env.BrowserUrl).Port}\nusers={users}\njmeter.save.saveservice.output_format=csv\njmeter.save.saveservice.print_field_names=true\njmeter.save.saveservice.subresults=false\n");
        for (var i = 0; i < users; i++)
        {
            var a = accounts[i];
            config.Append($"email.{i}={a.Email}\npassword.{i}={env.Password}\nmarker.{i}={a.Marker}\nloanId.{i}={a.Loan}\nbookId.{i}={a.Book}\nforeignLoanId.{i}={accounts[(i + 1) % accounts.Count].Loan}\n");
        }

        await File.WriteAllTextAsync(properties, config.ToString(), new UTF8Encoding(false));
        try
        {
            var start = new ProcessStartInfo("java")
            {
                WorkingDirectory = env.Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in new[]
            {
                "-jar",
                jar,
                "-n",
                "-t",
                Path.Combine(env.Root, "tests", "Load", "reader-staged.jmx"),
                "-q",
                properties,
                "-l",
                jtl,
                "-j",
                log
            }

            )
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("JMeter could not start.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(115));
            try
            {
                await process.WaitForExitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                Assert.Fail("JMeter stage exceeded115 seconds.");
            }

            var runner = Path.Combine(folder, "runner.log");
            await File.WriteAllTextAsync(runner, (await stdout) + (await stderr));
            TestContext.AddResultFile(runner);
            if (File.Exists(log))
                TestContext.AddResultFile(log);
            if (File.Exists(jtl))
                TestContext.AddResultFile(jtl);
            Assert.AreEqual(0, process.ExitCode, "Inspect JMeter runner.log.");
            Assert.IsTrue(File.Exists(jtl));
            using var parser = new TextFieldParser(jtl)
            {
                TextFieldType = FieldType.Delimited,
                HasFieldsEnclosedInQuotes = true
            };
            parser.SetDelimiters(",");
            var header = parser.ReadFields()!;
            var rows = new List<Dictionary<string, string>>();
            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields()!;
                rows.Add(header.Select((name, index) => (name, fields[index])).ToDictionary(x => x.name, x => x.Item2));
            }

            Assert.IsTrue(rows.Count > 0);
            var failed = rows.Count(x => x["success"] != "true");
            var begin = rows.Min(x => long.Parse(x["timeStamp"], CultureInfo.InvariantCulture));
            // 登录与最初5秒逐步启动区间不混入稳定阶段性能统计，但仍检查它们全部成功。
            var steady = rows.Where(x => !x["label"].StartsWith("Login", StringComparison.Ordinal) && x["label"] != "Reader sign in" && long.Parse(x["timeStamp"], CultureInfo.InvariantCulture) >= begin + 5000).ToArray();
            Assert.IsTrue(steady.Length > 0);
            var times = steady.Select(x => int.Parse(x["elapsed"], CultureInfo.InvariantCulture)).Order().ToArray();
            var first = steady.Min(x => long.Parse(x["timeStamp"], CultureInfo.InvariantCulture));
            var last = steady.Max(x => long.Parse(x["timeStamp"], CultureInfo.InvariantCulture) + long.Parse(x["elapsed"], CultureInfo.InvariantCulture));
            var seconds = (last - first) / 1000d;
            var summary = new
            {
                Users = users,
                RampSeconds = 5,
                ScheduledSeconds = 70,
                StableSeconds = seconds,
                DataSize = dataSize,
                TotalSamples = rows.Count,
                StableSamples = steady.Length,
                Failed = failed,
                ErrorRate = (double)failed / rows.Count,
                MeanMilliseconds = times.Average(),
                P95Milliseconds = times[(int)Math.Ceiling(times.Length * 0.95) - 1],
                MaximumMilliseconds = times[^1],
                ThroughputPerSecond = steady.Length / seconds,
                ActiveThreads = steady.Select(x => x["threadName"]).Distinct().Count(),
                Scope = "Local staged workload; independent reader sessions; no production SLA claimed."
            };
            var summaryPath = Path.Combine(folder, "summary.json");
            await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            TestContext.AddResultFile(summaryPath);
            Assert.AreEqual(users, summary.ActiveThreads);
            Assert.IsTrue(seconds >= 60, "Stable sampling must cover at least60 seconds.");
            Assert.AreEqual(0, failed, "Inspect response assertions in JTL, including per-reader row isolation.");
        }
        finally
        {
            File.Delete(properties);
        }
    }
}
