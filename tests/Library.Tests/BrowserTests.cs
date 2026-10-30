using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Support.UI;
namespace Library.Tests;
[TestClass, TestCategory("Browser")]
public class BrowserTests {
 private TestEnvironment env=null!;private IWebDriver driver=null!;private WebDriverWait wait=null!;
 public TestContext TestContext { get; set; }=null!;
 [TestInitialize] public async Task Initialize() {
  env=TestEnvironment.Current;await env.StartBrowserHost();
  if(env.Browser.Equals("chrome",StringComparison.OrdinalIgnoreCase)) {
   var options=new ChromeOptions();options.AddArguments("--headless=new","--no-sandbox","--disable-dev-shm-usage","--window-size=1440,1000");driver=new ChromeDriver(options);
  } else {
   var options=new EdgeOptions();options.AddArguments("--headless=new","--disable-gpu","--window-size=1440,1000","--no-sandbox","--disable-dev-shm-usage","--remote-debugging-pipe","--no-first-run");
   options.AddArgument("--user-data-dir="+Path.Combine(env.Root,".local","browser-profiles",Guid.NewGuid().ToString("N")));var driverDirectory=Path.Combine(env.Root,".local","webdriver");
   driver=File.Exists(Path.Combine(driverDirectory,"msedgedriver.exe")) ? new EdgeDriver(EdgeDriverService.CreateDefaultService(driverDirectory),options,TimeSpan.FromSeconds(30)) : new EdgeDriver(options);
  }
  wait=new WebDriverWait(driver,TimeSpan.FromSeconds(15));
  wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException),typeof(NoSuchElementException));
 }
 [TestCleanup] public void Cleanup() {
  if(driver is not null) {try {if(TestContext.CurrentTestOutcome!=UnitTestOutcome.Passed) {var folder=Path.Combine(env.Root,"TestResults","screenshots");Directory.CreateDirectory(folder);var name=Path.Combine(folder,TestContext.TestName+".png");((ITakesScreenshot)driver).GetScreenshot().SaveAsFile(name);TestContext.AddResultFile(name);}}finally {driver.Quit();driver.Dispose();}}
 }
 private void Login(string email,string? password=null) {
  driver.Navigate().GoToUrl(env.BrowserUrl+"/Account/Login");driver.FindElement(By.Id("Email")).SendKeys(email);driver.FindElement(By.Id("Password")).SendKeys(password??env.Password);driver.FindElement(By.CssSelector("button[type=submit]")).Click();
 }
 private void WaitText(string text)=>wait.Until(d=> {
  try { return ((string)((IJavaScriptExecutor)d).ExecuteScript("return document.body ? document.body.innerText : ''")).Contains(text); }
  catch(WebDriverException ex) when(ex.Message.Contains("Node with given id") || ex.Message.Contains("execution context was destroyed")) { return false; }
 });
 [TestMethod] public void Admin_login_and_logout_in_real_browser() {
  Login("admin@library.example");WaitText("LIBRARY WORKSPACE · ADMIN");driver.FindElement(By.CssSelector("nav button[type=submit]")).Click();WaitText("Sign in to your library");driver.Navigate().GoToUrl(env.BrowserUrl+"/Books");wait.Until(d=>d.Url.Contains("/Account/Login"));
 }
 [TestMethod] public void Reader_login_and_direct_admin_access_are_isolated() {
  Login("reader@library.example");WaitText("READER WORKSPACE");Assert.AreEqual(0,driver.FindElements(By.CssSelector("nav a[href='/Readers']")).Count);driver.Navigate().GoToUrl(env.BrowserUrl+"/Readers");WaitText("Access denied");
 }
 [TestMethod] public void Invalid_login_shows_generic_feedback() {
  Login("unknown@library.example","BadPassword9");WaitText("Unable to sign in with these credentials.");Assert.AreEqual("",driver.FindElement(By.Id("Password")).GetDomProperty("value"));
 }
 [TestMethod] public void Admin_can_add_book_create_reader_and_record_loan() {
  var suffix=Guid.NewGuid().ToString("N");var title="Browser book "+suffix;var name="Browser reader "+suffix;var email=suffix+"@library.example";
  Login("admin@library.example");WaitText("LIBRARY WORKSPACE · ADMIN");
  driver.Navigate().GoToUrl(env.BrowserUrl+"/Books/Create");driver.FindElement(By.Id("Isbn")).SendKeys(suffix);driver.FindElement(By.Id("Title")).SendKeys(title);driver.FindElement(By.Id("Author")).SendKeys("Test Author");driver.FindElement(By.Id("TotalCopies")).SendKeys("1");driver.FindElement(By.CssSelector("main button[type=submit]")).Click();WaitText("Book added to the catalog.");WaitText(title);
  driver.Navigate().GoToUrl(env.BrowserUrl+"/Readers/Create");driver.FindElement(By.Id("Name")).SendKeys(name);driver.FindElement(By.Id("Email")).SendKeys(email);driver.FindElement(By.Id("Password")).SendKeys(env.Password);driver.FindElement(By.CssSelector("main button[type=submit]")).Click();WaitText("Reader and login account created.");
  driver.Navigate().GoToUrl(env.BrowserUrl+"/Loans/Create");new SelectElement(driver.FindElement(By.Id("BookId"))).SelectByText(title+" (1 available)");new SelectElement(driver.FindElement(By.Id("ReaderId"))).SelectByText(name+" — "+email);driver.FindElement(By.CssSelector("main button[type=submit]")).Click();WaitText("Loan recorded.");WaitText(title);WaitText(name);WaitText("On loan");
  driver.Navigate().GoToUrl(env.BrowserUrl+"/Books");var row=driver.FindElements(By.CssSelector("tbody tr")).Single(x=>x.Text.Contains(title));StringAssert.Contains(row.Text,"0 available");
  driver.FindElement(By.CssSelector("nav button[type=submit]")).Click();WaitText("Sign in to your library");Login(email);WaitText("READER WORKSPACE");
 }
 [TestMethod] public void Login_layout_fits_a_narrow_viewport() {
  driver.Manage().Window.Size=new System.Drawing.Size(390,844);driver.Navigate().GoToUrl(env.BrowserUrl+"/Account/Login");WaitText("Sign in to your library");
  var overflow=(bool)((IJavaScriptExecutor)driver).ExecuteScript("return document.documentElement.scrollWidth > window.innerWidth");Assert.IsFalse(overflow,"Login page should not overflow horizontally.");Assert.IsTrue(driver.FindElement(By.CssSelector("button[type=submit]")).Displayed);
 }
}
