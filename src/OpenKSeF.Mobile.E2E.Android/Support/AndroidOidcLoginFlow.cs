using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;

namespace OpenKSeF.Mobile.E2E.Android.Support;

public sealed class AndroidOidcLoginFlow(AndroidDriver driver)
{
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
        DisableChromeTranslate();
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
        _driver.TerminateApp(ChromePackage);
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
            ClearAppState();
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

    public void ClearAppState()
    {
        _driver.TerminateApp(ChromePackage);
        _driver.TerminateApp(AppPackage);
        Shell("run-as", AppPackage, "rm", "-rf", "shared_prefs");
        _driver.ActivateApp(AppPackage);
    }

    public void ClearStaleServerUrl()
    {
        string prefs;
        try
        {
            prefs = Shell("run-as", AppPackage, "cat", $"shared_prefs/{AppPackage}_preferences.xml")?.ToString() ?? string.Empty;
        }
        catch (WebDriverException)
        {
            return;
        }

        if (prefs.Contains("name=\"server_url\"", StringComparison.Ordinal) &&
            !prefs.Contains($">{AppServerUrl.TrimEnd('/')}", StringComparison.Ordinal))
            ClearAppState();
    }

    public void Grant(string permission)
    {
        try
        {
            Shell("pm", "grant", AppPackage, permission);
        }
        catch (WebDriverException ex) when (ex.Message.Contains("adb_shell", StringComparison.Ordinal))
        {
        }
    }

    public object? Shell(string command, params string[] args) =>
        _driver.ExecuteScript("mobile: shell", new Dictionary<string, object> { ["command"] = command, ["args"] = args });

    private static bool _translateDisabled;

    // Chrome's translate bar and "Save password?" message cover the Keycloak page. Chrome reads this file on a debuggable (emulator) build.
    private void DisableChromeTranslate()
    {
        if (_translateDisabled)
            return;
        Shell("echo", "_", "--disable-features=Translate,TranslateUI", "--disable-save-password-bubble", ">", "/data/local/tmp/chrome-command-line");
        _driver.TerminateApp(ChromePackage);
        DisableChromePasswordManager();
        _translateDisabled = true;
    }

    // Chrome 113 ignores --disable-save-password-bubble; after its message closes, UiAutomator2 no longer sees the 2FA page.
    private void DisableChromePasswordManager()
    {
        const string preferences = "/data/data/" + ChromePackage + "/app_chrome/Default/Preferences";
        var json = Shell("su", "0", "cat", preferences)?.ToString() ?? string.Empty;
        if (!json.StartsWith('{'))
            return;
        var prefs = JsonNode.Parse(json)!.AsObject();
        prefs["credentials_enable_service"] = false;
        (prefs["profile"] ??= new JsonObject())["password_manager_enabled"] = false;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(prefs.ToJsonString()));
        Shell("echo", encoded, "|", "su", "0", "sh", "-c", $"'base64 -d > {preferences}'");
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
        serverEntry.SendKeys(AppServerUrl);
        _driver.FindElement(ByAutoId("LoginPageButtonSaveServer")).Click();
    }

    private const string ChromePackage = "com.android.chrome";

    private static string AppServerUrl => Environment.GetEnvironmentVariable("E2E_APP_SERVER_URL") ?? "http://10.0.2.2:8080";

    private static string AppPackage => Environment.GetEnvironmentVariable("ANDROID_APP_PACKAGE") ?? "com.openksef.mobile";

    private static By AccountTab => By.XPath("//*[@text='Konto' or @content-desc='Konto']");

    public void WaitForAuthenticatedPage(TimeSpan timeout)
    {
        var wait = new WebDriverWait(_driver, timeout);
        wait.Until(IsInApp);
        AnswerPushConsent(accept: false, TimeSpan.FromSeconds(3));
        wait.Until(currentDriver =>
            AllowNotificationPrompt(currentDriver) ||
            IsInvoiceList(currentDriver) || currentDriver.FindElements(ByAutoId("OnboardingStepIndicator")).Count > 0);
    }

    public void WaitForAuthenticatedInvoiceList(TimeSpan timeout)
    {
        new WebDriverWait(_driver, timeout).Until(d => IsInvoiceList(d) || d.FindElements(PushConsentMessage).Count > 0);
        AnswerPushConsent(accept: false, TimeSpan.FromSeconds(3));
        new WebDriverWait(_driver, timeout).Until(d => AllowNotificationPrompt(d) || IsInvoiceList(d));
    }

    // Returns false so a wait keeps polling after the OS prompt is gone.
    private static bool AllowNotificationPrompt(IWebDriver d)
    {
        d.FindElements(By.Id("com.android.permissioncontroller:id/permission_allow_button")).FirstOrDefault()?.Click();
        return false;
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
        wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException));
        wait.Until(d =>
        {
            DismissSavePasswordMessage();
            return InPageSource(d, "totp") || InPageSource(d, "otp") || IsInApp(d);
        });

        if (InPageSource(_driver, "totp"))
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
        else if (InPageSource(_driver, "otp"))
        {
            var secret = StoredSecret(username) ?? Environment.GetEnvironmentVariable("E2E_TEST_TOTP_SECRET");
            if (string.IsNullOrWhiteSpace(secret))
                Assert.Fail($"{username} has 2FA but no TOTP secret: set E2E_TEST_TOTP_SECRET or remove the user's OTP credential.");
            SubmitCode("otp", "Potwierdź", username, secret!);
        }
    }

    private void SubmitCode(string fieldId, string buttonText, string username, string secret)
    {
        ScrollTo(ByHtmlId(fieldId)).SendKeys(Totp.NextCode(username, secret));
        _driver.ExecuteScript("mobile: pressKey", new Dictionary<string, object> { ["keycode"] = 66 });
    }

    private IWebElement FindButton(string text) => ScrollTo(By.XPath($"//android.widget.Button[@text='{text}']"));

    // UiAutomator2 reports below-the-fold Chrome elements with zero or clipped height, so swipe up until one sits fully inside the page area.
    private IWebElement ScrollTo(By by)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            DismissSavePasswordMessage();
            var visibleBottom = VisibleContentBottom();
            var element = _driver.FindElements(by).FirstOrDefault(e => e.Size.Height > 0 && e.Location.Y >= 0 && e.Location.Y + e.Size.Height <= visibleBottom);
            if (element is not null)
                return element;
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
        throw new WebDriverTimeoutException($"{by} is not fully visible after scrolling.");
    }

    // The flag above may be ignored by the installed Chrome; while the message shows, UiAutomator2 cannot read the page under it.
    private void DismissSavePasswordMessage()
    {
        try
        {
            var message = _driver.FindElements(By.XPath("//*[@text='Save password?' or @text='Zapisać hasło?']"))
                .Where(e => e.Size.Height > 0).MaxBy(e => e.Size.Width * e.Size.Height);
            if (message is null)
                return;
            var x = message.Location.X + message.Size.Width / 2;
            var y = message.Location.Y + message.Size.Height / 2;
            var finger = new PointerInputDevice(PointerKind.Touch, "finger");
            var drag = new ActionSequence(finger);
            drag.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, x, y, TimeSpan.Zero));
            drag.AddAction(finger.CreatePointerDown(MouseButton.Touch));
            drag.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, x, 0, TimeSpan.FromMilliseconds(150)));
            drag.AddAction(finger.CreatePointerUp(MouseButton.Touch));
            _driver.PerformActions([drag]);
        }
        catch (StaleElementReferenceException)
        {
        }
    }

    private int VisibleContentBottom()
    {
        var bottom = _driver.FindElements(By.ClassName("android.webkit.WebView")).Select(w => w.Location.Y + w.Size.Height).DefaultIfEmpty(_driver.Manage().Window.Size.Height).Min();
        var overlays = _driver.FindElements(By.XPath("//*[contains(@resource-id,'infobar') or contains(@resource-id,'translate') or contains(@resource-id,'message_banner')]"))
            .Where(o => o.Size.Height > 0 && o.Location.Y > bottom / 2).Select(o => o.Location.Y);
        return overlays.Append(bottom).Min();
    }

    internal static string SecretPath(string username) => Path.Combine(SecretDirectory, $"{username}.txt");

    internal static string? StoredSecret(string username) =>
        File.Exists(SecretPath(username)) ? File.ReadAllText(SecretPath(username)) : null;

    private static bool IsInvoiceList(IWebDriver d) =>
        d.FindElements(ByAutoId("InvoiceListPageLabelTitle")).Count > 0 ||
        d.FindElements(ByAutoId("InvoiceListPageCollectionViewInvoices")).Count > 0;

    private static bool IsInApp(IWebDriver d) =>
        IsInvoiceList(d) ||
        d.FindElements(ByAutoId("OnboardingStepIndicator")).Count > 0 ||
        d.FindElements(PushConsentMessage).Count > 0;

    // Off-screen Chrome fields are in the page source with zero bounds but not returned by FindElements.
    private static bool InPageSource(IWebDriver d, string htmlId) => d.PageSource.Contains($"resource-id=\"{htmlId}\"", StringComparison.Ordinal);

    private static By ByHtmlId(string id) => By.XPath($"//*[@resource-id='{id}']");
}
