using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Support.UI;

namespace Library.Tests;
/// <summary>通过无头真实浏览器执行管理员与读者的核心流程，验证页面和端到端行为。</summary>
[TestClass, TestCategory("Browser")]
public class BrowserTests
{
    /// <summary>共享的隔离测试环境，所有用例使用随机数据库及虚构账号。</summary>
    private TestEnvironment env = null !;
    /// <summary>当前测试拥有的WebDriver会话，在清理阶段退出以释放浏览器进程。</summary>
    private IWebDriver driver = null !;
    /// <summary>浏览器显式等待器，用于等待导航和页面元素满足断言条件。</summary>
    private WebDriverWait wait = null !;
    /// <summary>MSTest上下文，用于把原始结果附件留在本地测试记录中。</summary>
    public TestContext TestContext { get; set; } = null !;

    /// <summary>为当前测试配置共享隔离环境；浏览器测试还会创建独立无头会话。</summary>
    [TestInitialize]
    public async Task Initialize()
    {
        env = TestEnvironment.Current;
        await env.StartBrowserHost();
        if (env.Browser.Equals("chrome", StringComparison.OrdinalIgnoreCase))
        {
            var options = new ChromeOptions();
            options.AddArguments("--headless=new", "--no-sandbox", "--disable-dev-shm-usage", "--window-size=1440,1000");
            driver = new ChromeDriver(options);
        }
        else
        {
            var options = new EdgeOptions();
            options.AddArguments("--headless=new", "--disable-gpu", "--window-size=1440,1000", "--no-sandbox", "--disable-dev-shm-usage", "--remote-debugging-pipe", "--no-first-run");
            options.AddArgument("--user-data-dir=" + Path.Combine(env.Root, ".local", "browser-profiles", Guid.NewGuid().ToString("N")));
            var driverDirectory = Path.Combine(env.Root, ".local", "webdriver");
            driver = File.Exists(Path.Combine(driverDirectory, "msedgedriver.exe")) ? new EdgeDriver(EdgeDriverService.CreateDefaultService(driverDirectory), options, TimeSpan.FromSeconds(30)) : new EdgeDriver(options);
        }

        wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
        wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException), typeof(NoSuchElementException));
    }

    /// <summary>测试结束时收集失败附件并释放浏览器，或在程序集结束时清理共享测试环境。</summary>
    [TestCleanup]
    public void Cleanup()
    {
        if (driver is not null)
        {
            try
            {
                if (TestContext.CurrentTestOutcome != UnitTestOutcome.Passed)
                {
                    var folder = Path.Combine(env.Root, "TestResults", "screenshots");
                    Directory.CreateDirectory(folder);
                    var name = Path.Combine(folder, TestContext.TestName + ".png");
                    ((ITakesScreenshot)driver).GetScreenshot().SaveAsFile(name);
                    TestContext.AddResultFile(name);
                }
            }
            finally
            {
                driver.Quit();
                driver.Dispose();
            }
        }
    }

    /// <summary>填写真实浏览器的邮箱与密码控件并提交，后续页面由等待条件确认。</summary>
    private void Login(string email, string? password = null)
    {
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Account/Login");
        driver.FindElement(By.Id("Email")).SendKeys(email);
        driver.FindElement(By.Id("Password")).SendKeys(password ?? env.Password);
        driver.FindElement(By.CssSelector("button[type=submit]")).Click();
    }

    /// <summary>等待浏览器正文出现预期文案，处理页面导航完成与UI更新的时序差异。</summary>
    private void WaitText(string text) => wait.Until(d =>
    {
        try
        {
            return ((string)((IJavaScriptExecutor)d).ExecuteScript("return document.body ? document.body.innerText : ''")).Contains(text);
        }
        catch (WebDriverException ex)when (ex.Message.Contains("Node with given id") || ex.Message.Contains("execution context was destroyed"))
        {
            return false;
        }
    });
    /// <summary>将既有浏览器步骤附件保存到本地忽略目录，并关联当前MSTest结果。</summary>
    private void CaptureEvidence(string step)
    {
        var folder = Path.Combine(env.Root, "TestResults", "demo");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, TestContext.TestName + "-" + step + ".png");
        ((ITakesScreenshot)driver).GetScreenshot().SaveAsFile(path);
        TestContext.AddResultFile(path);
    }

    /// <summary>验证管理员真实浏览器登录到角色首页，退出后不再持有受保护会话。</summary>
    [TestMethod]
    public void Admin_login_and_logout_in_real_browser()
    {
        Login("admin@library.example");
        WaitText("LIBRARY WORKSPACE · ADMIN");
        CaptureEvidence("01-admin-home");
        driver.FindElement(By.CssSelector("nav button[type=submit]")).Click();
        WaitText("Sign in to your library");
        CaptureEvidence("admin-logout");
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Books");
        wait.Until(d => d.Url.Contains("/Account/Login"));
    }

    /// <summary>浏览器读者登录后尝试管理员地址，验证角色首页与拒绝访问。</summary>
    [TestMethod]
    public void Reader_login_and_direct_admin_access_are_isolated()
    {
        Login("reader@library.example");
        WaitText("READER WORKSPACE");
        Assert.AreEqual(0, driver.FindElements(By.CssSelector("nav a[href='/Readers']")).Count);
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Readers");
        WaitText("Access denied");
        CaptureEvidence("reader-access-denied");
    }

    /// <summary>浏览器错误登录显示统一提示，不泄露账号是否存在。</summary>
    [TestMethod]
    public void Invalid_login_shows_generic_feedback()
    {
        Login("unknown@library.example", "BadPassword9");
        WaitText("Unable to sign in with these credentials.");
        Assert.AreEqual("", driver.FindElement(By.Id("Password")).GetDomProperty("value"));
        CaptureEvidence("invalid-login");
    }

    /// <summary>验证真实浏览器从新增图书、创建读者到登记借出及库存变化的完整流程。</summary>
    [TestMethod]
    public void Admin_can_add_book_create_reader_and_record_loan()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var title = "Browser book " + suffix;
        var name = "Browser reader " + suffix;
        var email = suffix + "@library.example";
        Login("admin@library.example");
        WaitText("LIBRARY WORKSPACE · ADMIN");
        CaptureEvidence("01-admin-home");
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Books/Create");
        driver.FindElement(By.Id("Isbn")).SendKeys(suffix);
        driver.FindElement(By.Id("Title")).SendKeys(title);
        driver.FindElement(By.Id("Author")).SendKeys("Test Author");
        driver.FindElement(By.Id("TotalCopies")).SendKeys("1");
        driver.FindElement(By.CssSelector("main button[type=submit]")).Click();
        WaitText("Book added to the catalog.");
        WaitText(title);
        CaptureEvidence("02-book-created");
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Readers/Create");
        driver.FindElement(By.Id("Name")).SendKeys(name);
        driver.FindElement(By.Id("Email")).SendKeys(email);
        driver.FindElement(By.Id("Password")).SendKeys(env.Password);
        driver.FindElement(By.CssSelector("main button[type=submit]")).Click();
        WaitText("Reader and login account created.");
        CaptureEvidence("03-reader-created");
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Loans/Create");
        new SelectElement(driver.FindElement(By.Id("BookId"))).SelectByText(title + " (1 available)");
        new SelectElement(driver.FindElement(By.Id("ReaderId"))).SelectByText(name + " — " + email);
        driver.FindElement(By.CssSelector("main button[type=submit]")).Click();
        WaitText("Loan recorded.");
        WaitText(title);
        WaitText(name);
        WaitText("On loan");
        CaptureEvidence("04-loan-recorded");
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Books");
        var row = driver.FindElements(By.CssSelector("tbody tr")).Single(x => x.Text.Contains(title));
        StringAssert.Contains(row.Text, "0 available");
        CaptureEvidence("05-zero-available");
        driver.FindElement(By.CssSelector("nav button[type=submit]")).Click();
        WaitText("Sign in to your library");
        Login(email);
        WaitText("READER WORKSPACE");
        CaptureEvidence("06-reader-home");
    }

    /// <summary>使用窄视口检查登录页面不产生水平溢出。</summary>
    [TestMethod]
    public void Login_layout_fits_a_narrow_viewport()
    {
        driver.Manage().Window.Size = new System.Drawing.Size(390, 844);
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Account/Login");
        WaitText("Sign in to your library");
        var overflow = (bool)((IJavaScriptExecutor)driver).ExecuteScript("return document.documentElement.scrollWidth > window.innerWidth");
        Assert.IsFalse(overflow, "Login page should not overflow horizontally.");
        Assert.IsTrue(driver.FindElement(By.CssSelector("button[type=submit]")).Displayed);
    }

    /// <summary>验证维护图书和读者、实际归还、历史保护及读者查看归还结果的闭环。</summary>
    [TestMethod]
    public async Task Admin_maintains_book_reader_and_completes_return_in_browser()
    {
        var key = Guid.NewGuid().ToString("N");
        var email = key + "@library.example";
        var book = await env.Use((s, _) => s.AddBookAsync(new() { Isbn = key, Title = "Return " + key, Author = "Browser Author", TotalCopies = 1 }));
        var reader = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = "Return reader " + key, Email = email, Password = env.Password }));
        Login("admin@library.example");
        WaitText("LIBRARY WORKSPACE · ADMIN");
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Books/Edit/" + book.Id);
        var title = driver.FindElement(By.Id("Title"));
        title.Clear();
        title.SendKeys("Edited " + key);
        driver.FindElement(By.CssSelector("main button[type=submit]")).Click();
        WaitText("Book updated.");
        WaitText("Edited " + key);
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Readers/Edit/" + reader.Id);
        var name = driver.FindElement(By.Id("Name"));
        name.Clear();
        name.SendKeys("Edited reader " + key);
        driver.FindElement(By.CssSelector("main button[type=submit]")).Click();
        WaitText("Reader and login account updated.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var loan = await env.Use((s, _) => s.LendAsync(new() { BookId = book.Id, ReaderId = reader.Id, BorrowDate = today, DueDate = today.AddDays(14), RequestId = Guid.NewGuid().ToString() }));
        Assert.IsTrue(loan.Success, loan.Error);
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Loans/Return/" + loan.Id);
        driver.FindElement(By.CssSelector("main button[type=submit]")).Click();
        WaitText("Book returned.");
        var loanRow = driver.FindElement(By.CssSelector($"tr[data-loan-id='{loan.Id}']"));
        StringAssert.Contains(loanRow.Text, "Returned");
        Assert.AreEqual(0, loanRow.FindElements(By.LinkText("Return")).Count);
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Books?q=" + key);
        var row = driver.FindElement(By.CssSelector($"tr[data-book-id='{book.Id}']"));
        StringAssert.Contains(row.Text, "1 available");
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Books/Delete/" + book.Id);
        driver.FindElement(By.CssSelector("main button[type=submit]")).Click();
        WaitText("Books with loan history cannot be deleted.");
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Readers/Delete/" + reader.Id);
        driver.FindElement(By.CssSelector("main button[type=submit]")).Click();
        WaitText("Readers with loan history cannot be deleted.");
        driver.FindElement(By.CssSelector("nav button[type=submit]")).Click();
        WaitText("Sign in to your library");
        Login(email);
        WaitText("READER WORKSPACE");
        driver.FindElement(By.LinkText("My loans")).Click();
        WaitText("Edited " + key);
        WaitText("Returned");
    }

    /// <summary>浏览器核对目录搜索排序和只读权限，并确认个人逾期借阅的数据隔离。</summary>
    [TestMethod]
    public async Task Reader_searches_sorts_catalogue_and_sees_only_own_loans_in_browser()
    {
        var key = Guid.NewGuid().ToString("N");
        var first = await env.Use((s, _) => s.AddBookAsync(new() { Isbn = key, Title = "A " + key, Author = "Browser search " + key, TotalCopies = 1 }));
        var second = await env.Use((s, _) => s.AddBookAsync(new() { Isbn = Guid.NewGuid().ToString("N"), Title = "B " + key, Author = "Browser search " + key, TotalCopies = 2 }));
        var reader = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = "Own " + key, Email = key + "@library.example", Password = env.Password }));
        var other = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = "Other " + key, Email = "other" + key + "@library.example", Password = env.Password }));
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        Assert.IsTrue((await env.Use((s, _) => s.LendAsync(new() { BookId = first.Id, ReaderId = reader.Id, BorrowDate = today.AddDays(-3), DueDate = today.AddDays(-1), RequestId = Guid.NewGuid().ToString() }))).Success);
        Assert.IsTrue((await env.Use((s, _) => s.LendAsync(new() { BookId = second.Id, ReaderId = other.Id, BorrowDate = today, DueDate = today.AddDays(14), RequestId = Guid.NewGuid().ToString() }))).Success);
        Login(key + "@library.example");
        WaitText("READER WORKSPACE");
        driver.FindElement(By.LinkText("Books")).Click();
        WaitText("Books");
        driver.FindElement(By.Id("q")).SendKeys("Browser search " + key);
        new SelectElement(driver.FindElement(By.Id("sort"))).SelectByValue("available_desc");
        driver.FindElement(By.CssSelector("main button[type=submit]")).Click();
        wait.Until(d => d.Url.Contains("sort=available_desc"));
        var rows = driver.FindElements(By.CssSelector("tbody tr"));
        Assert.AreEqual(2, rows.Count);
        StringAssert.Contains(rows[0].Text, "B " + key);
        Assert.AreEqual(0, driver.FindElements(By.CssSelector("main a[href*='/Books/Edit'],main a[href*='/Books/Delete']")).Count);
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Books?q=missing-" + key);
        WaitText("No books match your search.");
        driver.Navigate().GoToUrl(env.BrowserUrl + "/MyLoans?readerId=" + other.Id);
        WaitText("A " + key);
        WaitText("Overdue");
        Assert.IsFalse(driver.FindElement(By.TagName("body")).Text.Contains("B " + key));
    }

    /// <summary>验证无借阅历史的图书及读者经过确认后可以删除，账号同步移除。</summary>
    [TestMethod]
    public async Task Admin_confirms_deleting_unused_book_and_reader_in_browser()
    {
        var key = Guid.NewGuid().ToString("N");
        var book = await env.Use((s, _) => s.AddBookAsync(new() { Isbn = key, Title = "Delete " + key, Author = "Browser", TotalCopies = 0 }));
        var reader = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = "Delete " + key, Email = key + "@library.example", Password = env.Password }));
        Login("admin@library.example");
        WaitText("LIBRARY WORKSPACE · ADMIN");
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Books/Delete/" + book.Id);
        WaitText("Confirm delete");
        driver.FindElement(By.CssSelector("main button[type=submit]")).Click();
        WaitText("Book deleted.");
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Books?q=" + key);
        WaitText("No books match your search.");
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Readers/Delete/" + reader.Id);
        WaitText("Confirm delete");
        driver.FindElement(By.CssSelector("main button[type=submit]")).Click();
        WaitText("Reader and login account deleted.");
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Readers?q=" + key);
        WaitText("No readers match your search.");
    }

    // 管理员通过真实表单更正日期并确认删除误录，确认列表与库存同步更新。
    /// <summary>通过表单更正应还日，再确认删除未归还误录，核对列表和库存。</summary>
    [TestMethod]
    public async Task Admin_corrects_and_deletes_outstanding_mistake_in_browser()
    {
        var key = Guid.NewGuid().ToString("N");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var book = await env.Use((s, _) => s.AddBookAsync(new() { Isbn = key, Title = "Mistake " + key, Author = "Browser", TotalCopies = 1 }));
        var reader = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = "Mistake reader", Email = key + "@library.example", Password = env.Password }));
        var loan = await env.Use((s, _) => s.LendAsync(new() { BookId = book.Id, ReaderId = reader.Id, BorrowDate = today.AddDays(-3), DueDate = today.AddDays(3), RequestId = Guid.NewGuid().ToString() }));
        Assert.IsTrue(loan.Success);
        Login("admin@library.example");
        WaitText("LIBRARY WORKSPACE · ADMIN");
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Loans/Edit/" + loan.Id);
        // HTML日期控件使用键盘输入可能受系统区域设置影响，原生value赋值只用于填表。
        ((IJavaScriptExecutor)driver).ExecuteScript("document.getElementById('DueDate').value=arguments[0]", today.AddDays(7).ToString("yyyy-MM-dd"));
        driver.FindElement(By.CssSelector("main button[type=submit]")).Click();
        WaitText("Loan dates updated.");
        var row = driver.FindElement(By.CssSelector($"tr[data-loan-id='{loan.Id}']"));
        StringAssert.Contains(row.Text, today.AddDays(7).ToString("yyyy-MM-dd"));
        row.FindElement(By.LinkText("Delete mistaken loan")).Click();
        WaitText("Confirm delete");
        driver.FindElement(By.CssSelector("main button[type=submit]")).Click();
        WaitText("Mistaken loan deleted.");
        Assert.AreEqual(0, driver.FindElements(By.CssSelector($"tr[data-loan-id='{loan.Id}']")).Count);
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Books?q=" + key);
        StringAssert.Contains(driver.FindElement(By.CssSelector($"tr[data-book-id='{book.Id}']")).Text, "1 available");
    }

    // 读者通过控件组合搜索/逾期筛选/排序，并验证URL伪造与清空均不越权。
    /// <summary>通过真实控件组合个人借阅条件，核对逾期、空态、清空及维护地址拒绝。</summary>
    [TestMethod]
    public async Task Reader_combines_loan_filters_and_clear_preserves_scope_in_browser()
    {
        var key = Guid.NewGuid().ToString("N");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var reader = await env.Use((s, _) => s.CreateReaderAsync(new() { Name = "Filter reader", Email = key + "@library.example", Password = env.Password }));
        var book = await env.Use((s, _) => s.AddBookAsync(new() { Isbn = key, Title = "Filter " + key, Author = "Browser", TotalCopies = 1 }));
        var loan = await env.Use((s, _) => s.LendAsync(new() { BookId = book.Id, ReaderId = reader.Id, BorrowDate = today.AddDays(-3), DueDate = today.AddDays(-1), RequestId = Guid.NewGuid().ToString() }));
        Assert.IsTrue(loan.Success);
        Login(key + "@library.example");
        WaitText("READER WORKSPACE");
        driver.FindElement(By.LinkText("My loans")).Click();
        WaitText("My loans");
        driver.FindElement(By.Id("q")).SendKeys(key);
        new SelectElement(driver.FindElement(By.Id("status"))).SelectByValue("overdue");
        new SelectElement(driver.FindElement(By.Id("sort"))).SelectByValue("due");
        driver.FindElement(By.CssSelector("main button[type=submit]")).Click();
        wait.Until(d => d.Url.Contains("status=overdue"));
        WaitText("Overdue");
        Assert.AreEqual(1, driver.FindElements(By.CssSelector("tbody tr")).Count);
        driver.Navigate().GoToUrl(env.BrowserUrl + "/MyLoans?status=returned");
        WaitText("No loans match your search.");
        driver.FindElement(By.LinkText("Clear")).Click();
        WaitText("Filter " + key);
        Assert.AreEqual(1, driver.FindElements(By.CssSelector("tbody tr")).Count);
        driver.Navigate().GoToUrl(env.BrowserUrl + "/Loans/Edit/" + loan.Id);
        WaitText("Access denied");
    }
}
