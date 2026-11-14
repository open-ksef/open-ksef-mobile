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
        new WaitHelper(_driver, TimeSpan.FromSeconds(45)).UntilVisible(ByAutoId("LoginPageButtonLogin")).Click();

        // The Custom Tab is Chrome, not an app WebView; Chrome exposes HTML ids as native resource-ids.
        var webWait = new WebDriverWait(_driver, TimeSpan.FromSeconds(45));
        webWait.Until(d => d.FindElements(ByHtmlId("username")).Count > 0);
        var usernameField = _driver.FindElement(ByHtmlId("username"));
        usernameField.Clear();
        usernameField.SendKeys(username);
        _driver.FindElement(ByHtmlId("password")).SendKeys(password);
        _driver.FindElement(ByHtmlId("kc-login")).Click();
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

    private static By ByHtmlId(string id) => By.XPath($"//*[@resource-id='{id}']");
}
