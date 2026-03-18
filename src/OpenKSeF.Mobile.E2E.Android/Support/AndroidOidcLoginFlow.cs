using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Support.UI;

namespace OpenKSeF.Mobile.E2E.Android.Support;

public sealed class AndroidOidcLoginFlow(AndroidDriver driver)
{
    private readonly AndroidDriver _driver = driver;

    public void LoginWithKeycloakFromEnvironment()
    {
        var username = Environment.GetEnvironmentVariable("KEYCLOAK_USERNAME");
        var password = Environment.GetEnvironmentVariable("KEYCLOAK_PASSWORD");

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Assert.Ignore("KEYCLOAK_USERNAME / KEYCLOAK_PASSWORD are required for login E2E test.");
        }

        Login(username, password);
    }

    public void Login(string username, string password)
    {
        var nativeWait = new WaitHelper(_driver, TimeSpan.FromSeconds(45));

        var emailEntry = nativeWait.UntilVisible(ByAutoId("LoginPageEntryEmail"));
        emailEntry.Clear();
        emailEntry.SendKeys(username);

        var passwordEntry = _driver.FindElement(ByAutoId("LoginPageEntryPassword"));
        passwordEntry.Clear();
        passwordEntry.SendKeys(password);

        var loginButton = _driver.FindElement(ByAutoId("LoginPageButtonLogin"));
        loginButton.Click();
    }

    public void LoginViaKeycloakRedirect(string username, string password)
    {
        var nativeWait = new WaitHelper(_driver, TimeSpan.FromSeconds(45));
        var loginButton = nativeWait.UntilVisible(ByAutoId("LoginPageButtonGoogle"));
        loginButton.Click();

        SwitchToBrowserContext();

        var webWait = new WebDriverWait(_driver, TimeSpan.FromSeconds(45));
        webWait.Until(browser => browser.FindElements(By.Id("username")).Count > 0);
        _driver.FindElement(By.Id("username")).SendKeys(username);
        _driver.FindElement(By.Id("password")).SendKeys(password);
        _driver.FindElement(By.Id("kc-login")).Click();

        SwitchToNativeContext();
    }

    public void WaitForAuthenticatedPage(TimeSpan timeout)
    {
        var wait = new WebDriverWait(_driver, timeout);
        wait.Until(currentDriver =>
            currentDriver.FindElements(ByAutoId("InvoiceListPageLabelTitle")).Count > 0 ||
            currentDriver.FindElements(ByAutoId("InvoiceListPageCollectionViewInvoices")).Count > 0 ||
            currentDriver.FindElements(ByAutoId("OnboardingStepIndicator")).Count > 0);
    }

    public void WaitForAuthenticatedInvoiceList(TimeSpan timeout)
    {
        var wait = new WebDriverWait(_driver, timeout);
        wait.Until(currentDriver =>
            currentDriver.FindElements(ByAutoId("InvoiceListPageLabelTitle")).Count > 0 ||
            currentDriver.FindElements(ByAutoId("InvoiceListPageCollectionViewInvoices")).Count > 0);
    }

    private void SwitchToBrowserContext()
    {
        var contextWait = new WebDriverWait(_driver, TimeSpan.FromSeconds(30));
        contextWait.Until(_ => _driver.Contexts.Any(context =>
            context.Contains("WEBVIEW", StringComparison.OrdinalIgnoreCase)));

        var browserContext = _driver.Contexts.First(context =>
            context.Contains("WEBVIEW", StringComparison.OrdinalIgnoreCase));

        _driver.Context = browserContext;
    }

    private void SwitchToNativeContext()
    {
        _driver.Context = "NATIVE_APP";
    }
}
