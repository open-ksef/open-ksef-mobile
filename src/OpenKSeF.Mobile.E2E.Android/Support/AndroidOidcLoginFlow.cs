using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;
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
            E2EBackend.Unmet("KEYCLOAK_USERNAME / KEYCLOAK_PASSWORD are required for login E2E test.");
        }

        Login(username!, password!);
    }

    public void Login(string username, string password, bool? pushConsent = false)
    {
        var nativeWait = new WaitHelper(_driver, TimeSpan.FromSeconds(45));

        EnsureServerConfigured();
        var emailEntry = nativeWait.UntilVisible(ByAutoId("LoginPageEntryEmail"));
        emailEntry.Clear();
        emailEntry.SendKeys(username);

        var passwordEntry = _driver.FindElement(ByAutoId("LoginPageEntryPassword"));
        passwordEntry.Clear();
        passwordEntry.SendKeys(password);

        var loginButton = _driver.FindElement(ByAutoId("LoginPageButtonLogin"));
        loginButton.Click();

        if (pushConsent is not { } accept)
            return;

        try
        {
            new WebDriverWait(_driver, TimeSpan.FromSeconds(60)).Until(d =>
                IsInvoiceList(d) ||
                d.FindElements(ByAutoId("OnboardingStepIndicator")).Count > 0 ||
                d.FindElements(PushConsentMessage).Count > 0);
        }
        catch (WebDriverTimeoutException)
        {
            return;
        }

        AnswerPushConsent(accept, TimeSpan.FromSeconds(3));
    }

    public void SignOut()
    {
        _driver.TerminateApp(AppPackage);
        _driver.ActivateApp(AppPackage);

        var settle = new WebDriverWait(_driver, TimeSpan.FromSeconds(60));
        settle.IgnoreExceptionTypes(typeof(WebDriverException));
        settle.Until(d =>
            d.FindElements(ByAutoId("LoginPageEntryEmail")).Count > 0 ||
            d.FindElements(ByAutoId("LoginPageEntryServerUrl")).Count > 0 ||
            d.FindElements(ByAutoId("OnboardingStepIndicator")).Count > 0 ||
            d.FindElements(AccountTab).Count > 0 ||
            d.FindElements(PushConsentMessage).Count > 0);

        if (AnswerPushConsent(accept: false, TimeSpan.FromSeconds(3)))
            new WebDriverWait(_driver, TimeSpan.FromSeconds(15)).Until(d => d.FindElements(AccountTab).Count > 0);

        if (_driver.FindElements(ByAutoId("OnboardingStepIndicator")).Count > 0)
        {
            _driver.TerminateApp(AppPackage);
            _driver.ExecuteScript("mobile: shell", new Dictionary<string, object>
            {
                ["command"] = "run-as",
                ["args"] = new[] { AppPackage, "rm", "-rf", "shared_prefs" }
            });
            _driver.ActivateApp(AppPackage);
            return;
        }

        if (_driver.FindElements(AccountTab).Count == 0)
            return;

        _driver.FindElement(AccountTab).Click();
        _driver.FindElement(MobileBy.AndroidUIAutomator(
            "new UiScrollable(new UiSelector().scrollable(true)).scrollIntoView(new UiSelector().resourceId(\"" + AppPackage + ":id/AccountPageRowLogout\"))")).Click();
        new WaitHelper(_driver, TimeSpan.FromSeconds(15)).UntilVisible(By.Id("android:id/button1")).Click();
        new WaitHelper(_driver, TimeSpan.FromSeconds(60)).UntilVisible(ByAutoId("LoginPageEntryEmail"));
    }

    private void EnsureServerConfigured()
    {
        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(45));
        wait.Until(d =>
            d.FindElements(ByAutoId("LoginPageEntryEmail")).Any(e => e.Displayed) ||
            d.FindElements(ByAutoId("LoginPageEntryServerUrl")).Any(e => e.Displayed));

        var serverEntry = _driver.FindElements(ByAutoId("LoginPageEntryServerUrl")).FirstOrDefault(e => e.Displayed);
        if (serverEntry is null)
            return;

        serverEntry.Clear();
        serverEntry.SendKeys(Environment.GetEnvironmentVariable("E2E_APP_SERVER_URL") ?? "http://10.0.2.2:8080");
        _driver.FindElement(ByAutoId("LoginPageButtonSaveServer")).Click();
    }

    private static string AppPackage => Environment.GetEnvironmentVariable("ANDROID_APP_PACKAGE") ?? "com.openksef.mobile";

    private static By AccountTab => By.XPath("//*[@text='Konto' or @content-desc='Konto']");

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
            currentDriver.FindElements(ByAutoId("OnboardingStepIndicator")).Count > 0 ||
            currentDriver.FindElements(PushConsentMessage).Count > 0);
        AnswerPushConsent(accept: false, TimeSpan.FromSeconds(3));
        wait.Until(currentDriver =>
            IsInvoiceList(currentDriver) || currentDriver.FindElements(ByAutoId("OnboardingStepIndicator")).Count > 0);
    }

    public void WaitForAuthenticatedInvoiceList(TimeSpan timeout)
    {
        new WebDriverWait(_driver, timeout).Until(d => IsInvoiceList(d) || d.FindElements(PushConsentMessage).Count > 0);
        AnswerPushConsent(accept: false, TimeSpan.FromSeconds(3));
        new WebDriverWait(_driver, timeout).Until(IsInvoiceList);
    }

    public static By PushConsentMessage =>
        By.XPath("//*[@resource-id='android:id/message' and contains(@text, 'powiadomienia o nowych fakturach')]");

    public bool AnswerPushConsent(bool accept, TimeSpan timeout)
    {
        try
        {
            new WebDriverWait(_driver, timeout).Until(d => d.FindElements(PushConsentMessage).Count > 0);
        }
        catch (WebDriverTimeoutException)
        {
            return false;
        }

        _driver.FindElement(By.Id(accept ? "android:id/button1" : "android:id/button2")).Click();
        return true;
    }

    private static bool IsInvoiceList(IWebDriver d) =>
        d.FindElements(ByAutoId("InvoiceListPageLabelTitle")).Count > 0 ||
        d.FindElements(ByAutoId("InvoiceListPageCollectionViewInvoices")).Count > 0;

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
