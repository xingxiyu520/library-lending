using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace Library.Tests;
/// <summary>将JMeter小规模smoke纳入MSTest，使Visual Studio Run All同时覆盖工具测试。</summary>
[TestClass, TestCategory("Load")]
public class LoadTests
{
    /// <summary>MSTest上下文，用于把原始结果附件留在本地测试记录中。</summary>
    public TestContext TestContext { get; set; } = null !;

    /// <summary>执行3用户、3轮的JMeter登录与只读smoke，断言36条主样本全部成功。</summary>
    [TestMethod]
    public async Task Authenticated_reader_JMeter_smoke_has_no_failed_samples()
    {
        var env = TestEnvironment.Current;
        await env.StartBrowserHost();
        var home = Environment.GetEnvironmentVariable("LIBRARY_JMETER_HOME") ?? Path.Combine(env.Root, ".local", "tools", "apache-jmeter-5.6.3");
        var jar = Path.Combine(home, "bin", "ApacheJMeter.jar");
        Assert.IsTrue(File.Exists(jar), "Run scripts/Install-JMeter.ps1 (Java 17/21 required) before Run All, or configure LIBRARY_JMETER_HOME.");
        var folder = Path.Combine(env.Root, "TestResults", "load", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var properties = Path.Combine(folder, "session.properties");
        var uri = new Uri(env.BrowserUrl);
        var jtl = Path.Combine(folder, "reader-smoke.jtl");
        var log = Path.Combine(folder, "jmeter.log");
        await File.WriteAllTextAsync(properties, $"host=127.0.0.1\nport={uri.Port}\nemail=reader@library.example\npassword={env.Password}\nusers=3\nloops=3\njmeter.save.saveservice.output_format=csv\njmeter.save.saveservice.print_field_names=true\njmeter.save.saveservice.subresults=false\n", new UTF8Encoding(false));
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
            foreach (var value in new[]
            {
                "-jar",
                jar,
                "-n",
                "-t",
                Path.Combine(env.Root, "tests", "Load", "reader-smoke.jmx"),
                "-q",
                properties,
                "-l",
                jtl,
                "-j",
                log
            }

            )
                start.ArgumentList.Add(value);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("JMeter could not start.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try
            {
                await process.WaitForExitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                Assert.Fail("JMeter smoke exceeded 90 seconds.");
            }

            var output = (await stdout) + (await stderr);
            await File.WriteAllTextAsync(Path.Combine(folder, "runner.log"), output);
            TestContext.AddResultFile(Path.Combine(folder, "runner.log"));
            if (File.Exists(log))
                TestContext.AddResultFile(log);
            Assert.AreEqual(0, process.ExitCode, output);
            Assert.IsTrue(File.Exists(jtl), "JMeter produced no samples.");
            TestContext.AddResultFile(jtl);
            using var parser = new TextFieldParser(jtl)
            {
                TextFieldType = FieldType.Delimited,
                HasFieldsEnclosedInQuotes = true
            };
            parser.SetDelimiters(",");
            var headers = parser.ReadFields()!;
            var success = Array.IndexOf(headers, "success");
            var elapsed = Array.IndexOf(headers, "elapsed");
            Assert.IsTrue(success >= 0 && elapsed >= 0);
            var samples = new List<string[]>();
            while (!parser.EndOfData)
            {
                if (parser.ReadFields()is { } fields)
                    samples.Add(fields);
            }

            var rows = samples.ToArray();
            Assert.AreEqual(36, rows.Length, "3 readers × 3 loops × 4 requests must execute.");
            var failures = rows.Count(x => x[success] != "true");
            var times = rows.Select(x => int.Parse(x[elapsed], CultureInfo.InvariantCulture)).Order().ToArray();
            var summary = new
            {
                Users = 3,
                Loops = 3,
                Samples = rows.Length,
                Failed = failures,
                MeanMilliseconds = times.Average(),
                P95Milliseconds = times[(int)Math.Ceiling(times.Length * 0.95) - 1],
                MaximumMilliseconds = times[^1],
                Scope = "Local smoke baseline; no production capacity or latency SLA claimed."
            };
            var summaryPath = Path.Combine(folder, "summary.json");
            await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            TestContext.AddResultFile(summaryPath);
            Assert.AreEqual(0, failures, "JMeter response status/content assertions failed; inspect local JTL and log.");
        }
        finally
        {
            File.Delete(properties);
        }
    }
}
