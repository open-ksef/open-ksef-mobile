using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
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

        var emailEntry = nativeWait.UntilVisible(MobileBy.AccessibilityId("LoginPageEntryEmail"));
        emailEntry.Clear();
        emailEntry.SendKeys(username);

        var passwordEntry = _driver.FindElement(MobileBy.AccessibilityId("LoginPageEntryPassword"));
        passwordEntry.Clear();
        passwordEntry.SendKeys(password);

        var loginButton = _driver.FindElement(MobileBy.AccessibilityId("LoginPageButtonLogin"));
        loginButton.Click();
    }

    public void LoginViaKeycloakRedirect(string username, string password)
    {
        var nativeWait = new WaitHelper(_driver, TimeSpan.FromSeconds(45));
        var loginButton = nativeWait.UntilVisible(MobileBy.AccessibilityId("LoginPageButtonGoogle"));
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
            currentDriver.FindElements(MobileBy.AccessibilityId("InvoiceListPageLabelTitle")).Count > 0 ||
            currentDriver.FindElements(MobileBy.AccessibilityId("InvoiceListPageCollectionViewInvoices")).Count > 0 ||
            currentDriver.FindElements(MobileBy.AccessibilityId("OnboardingStepIndicator")).Count > 0);
    }

    public void WaitForAuthenticatedInvoiceList(TimeSpan timeout)
    {
        var wait = new WebDriverWait(_driver, timeout);
        wait.Until(currentDriver =>
            currentDriver.FindElements(MobileBy.AccessibilityId("InvoiceListPageLabelTitle")).Count > 0 ||
            currentDriver.FindElements(MobileBy.AccessibilityId("InvoiceListPageCollectionViewInvoices")).Count > 0);
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
