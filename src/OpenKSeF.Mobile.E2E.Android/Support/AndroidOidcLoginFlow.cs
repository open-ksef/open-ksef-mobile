using System.Text.RegularExpressions;
using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Support.UI;

namespace OpenKSeF.Mobile.E2E.Android.Support;

public sealed class AndroidOidcLoginFlow(AndroidDriver driver)
{
    private static readonly Dictionary<string, long> LastOtpStep = [];
    private static readonly string SecretDirectory = Path.Combine(Path.GetTempPath(), "openksef-e2e-totp");

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
        EnsureServerConfigured();
        new WaitHelper(_driver, TimeSpan.FromSeconds(45)).UntilVisible(ByAutoId("LoginPageButtonLogin")).Click();

        // The Custom Tab is Chrome, not an app WebView; Chrome exposes HTML ids as native resource-ids.
        var webWait = new WebDriverWait(_driver, TimeSpan.FromSeconds(45));
        webWait.Until(d => d.FindElements(ByHtmlId("username")).Count > 0);
        var usernameField = _driver.FindElement(ByHtmlId("username"));
        usernameField.Clear();
        usernameField.SendKeys(username);
        _driver.FindElement(ByHtmlId("password")).SendKeys(password);
        FindButton("Zaloguj się").Click();

        CompleteTwoFactor(username);

        if (pushConsent is not { } accept)
            return;

        try
        {
            new WebDriverWait(_driver, TimeSpan.FromSeconds(60)).Until(IsInApp);
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
            d.FindElements(ByAutoId("LoginPageButtonLogin")).Count > 0 ||
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
        new WaitHelper(_driver, TimeSpan.FromSeconds(60)).UntilVisible(ByAutoId("LoginPageButtonLogin"));
    }

    private void EnsureServerConfigured()
    {
        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(45));
        wait.Until(d =>
            d.FindElements(ByAutoId("LoginPageButtonLogin")).Any(e => e.Displayed) ||
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

    public void WaitForAuthenticatedPage(TimeSpan timeout)
    {
        var wait = new WebDriverWait(_driver, timeout);
        wait.Until(IsInApp);
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

    private void CompleteTwoFactor(string username)
    {
        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(45));
        wait.Until(d => d.FindElements(ByHtmlId("totp")).Count > 0 || d.FindElements(ByHtmlId("otp")).Count > 0 || IsInApp(d));

        if (_driver.FindElements(ByHtmlId("totp")).Count > 0)
        {
            var secret = Regex.Match(_driver.PageSource, @"text=""((?:[A-Z2-7]{4} ){3,}[A-Z2-7]{1,4})""").Groups[1].Value;
            Assert.That(secret, Is.Not.Empty, "TOTP secret not found on the 2FA setup page.");
            Directory.CreateDirectory(SecretDirectory);
            File.WriteAllText(SecretPath(username), secret);
            SubmitCode("totp", "Włącz weryfikację", username, secret);

            wait.Until(d => d.FindElements(ByHtmlId("kcRecoveryCodesConfirmationCheck")).Count > 0 || IsInApp(d));
            if (_driver.FindElements(ByHtmlId("kcRecoveryCodesConfirmationCheck")).Count > 0)
            {
                _driver.FindElement(ByHtmlId("kcRecoveryCodesConfirmationCheck")).Click();
                FindButton("Dalej").Click();
            }
        }
        else if (_driver.FindElements(ByHtmlId("otp")).Count > 0)
        {
            var secret = File.Exists(SecretPath(username))
                ? File.ReadAllText(SecretPath(username))
                : Environment.GetEnvironmentVariable("E2E_TEST_TOTP_SECRET");
            if (string.IsNullOrWhiteSpace(secret))
                Assert.Fail($"{username} has 2FA but no TOTP secret: set E2E_TEST_TOTP_SECRET or remove the user's OTP credential.");
            SubmitCode("otp", "Potwierdź", username, secret!);
        }
    }

    // Keycloak rejects a code reused within its time step, so a second login in the same step waits for the next one.
    private void SubmitCode(string fieldId, string buttonText, string username, string secret)
    {
        var step = Totp.CurrentStep;
        if (LastOtpStep.TryGetValue(username, out var last) && last >= step)
        {
            Thread.Sleep(TimeSpan.FromSeconds((last + 1) * 30 - DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1));
            step = Totp.CurrentStep;
        }
        LastOtpStep[username] = step;

        _driver.FindElement(ByHtmlId(fieldId)).SendKeys(Totp.Code(secret, step));
        FindButton(buttonText).Click();
    }

    // UiAutomator2 drops a below-the-fold button in Chrome (zero height), so swipe up until it has a size.
    private IWebElement FindButton(string text)
    {
        var by = By.XPath($"//android.widget.Button[@text='{text}']");
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var button = _driver.FindElements(by).FirstOrDefault(e => e.Size.Height > 0);
            if (button is not null)
                return button;
            var size = _driver.Manage().Window.Size;
            _driver.ExecuteScript("mobile: swipeGesture", new Dictionary<string, object>
            {
                ["left"] = size.Width / 10,
                ["top"] = size.Height / 4,
                ["width"] = size.Width * 8 / 10,
                ["height"] = size.Height / 2,
                ["direction"] = "up",
                ["percent"] = 0.75
            });
        }
        return _driver.FindElement(by);
    }

    private static string SecretPath(string username) => Path.Combine(SecretDirectory, $"{username}.txt");

    private static bool IsInvoiceList(IWebDriver d) =>
        d.FindElements(ByAutoId("InvoiceListPageLabelTitle")).Count > 0 ||
        d.FindElements(ByAutoId("InvoiceListPageCollectionViewInvoices")).Count > 0;

    private static bool IsInApp(IWebDriver d) =>
        IsInvoiceList(d) ||
        d.FindElements(ByAutoId("OnboardingStepIndicator")).Count > 0 ||
        d.FindElements(PushConsentMessage).Count > 0;

    private static By ByHtmlId(string id) => By.XPath($"//*[@resource-id='{id}']");
}
