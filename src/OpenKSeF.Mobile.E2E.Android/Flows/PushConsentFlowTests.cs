using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

/// <summary>
/// T-763: the device is registered only after "Tak" on the consent alert, and the choice is kept per account.
/// </summary>
[Category("Regression")]
public sealed class PushConsentFlowTests : AndroidTestBase
{
    protected override bool UsesSeededUser => true;

    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(15);

    [Test]
    public async Task AccountAOff_BTurnsOn_AStaysOffAndUnregisteredAtNextLogin()
    {
        var b = await E2EBackend.CreateSeededUserAsync();
        try
        {
            var flow = new AndroidOidcLoginFlow(AndroidDriver);
            ForgetConsent();

            LoginAsSeededUser(pushConsent: null);
            Assert.That(flow.AnswerPushConsent(accept: false, TimeSpan.FromSeconds(60)), Is.True, "A: consent alert expected on first login");
            flow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(30));
            Assert.That(await E2EBackend.StaysWithoutDevicesAsync(SeededUser, Quiet), Is.True, "A declined: no device row expected");
            Assert.That(PushToggleOn(), Is.False, "A declined: switch off");

            flow.SignOut();
            flow.Login(b.Username, b.Password, pushConsent: null);
            Assert.That(flow.AnswerPushConsent(accept: true, TimeSpan.FromSeconds(60)), Is.True, "B: consent alert expected, A's choice must not leak");
            flow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(30));
            Assert.That(await E2EBackend.WaitForDevicesAsync(b, 1, TimeSpan.FromSeconds(30)), Is.EqualTo(1), "B accepted: one device row");
            Assert.That(await E2EBackend.CountDevicesAsync(SeededUser), Is.Zero, "B's registration must not land on A");
            Assert.That(PushToggleOn(), Is.True, "B accepted: switch on");

            flow.SignOut();
            Assert.That(await E2EBackend.WaitForNoDevicesAsync(b, TimeSpan.FromSeconds(30)), Is.Zero, "B's row removed at sign-out");

            LoginAsSeededUser(pushConsent: null);
            Assert.That(flow.AnswerPushConsent(accept: true, TimeSpan.FromSeconds(10)), Is.False, "A: no second consent alert, and B's choice must not leak");
            flow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(30));
            Assert.That(await E2EBackend.StaysWithoutDevicesAsync(SeededUser, Quiet), Is.True, "A stays unregistered at next login");
            Assert.That(PushToggleOn(), Is.False, "A stays off");

            AndroidDriver.TerminateApp(AndroidOptions.AppPackage);
            AndroidDriver.ActivateApp(AndroidOptions.AppPackage);
            flow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));
            Assert.That(await E2EBackend.StaysWithoutDevicesAsync(SeededUser, Quiet), Is.True, "A stays unregistered after a cold start");
        }
        finally
        {
            new AndroidOidcLoginFlow(AndroidDriver).SignOut();
            await E2EBackend.DeleteUserAsync(b);
        }
    }

    [Test]
    public async Task FreshInstall_Declines_NoRegistration_SwitchOff_AskedOnce()
    {
        var flow = new AndroidOidcLoginFlow(AndroidDriver);
        ForgetConsent();

        LoginAsSeededUser(pushConsent: null);
        Assert.That(flow.AnswerPushConsent(accept: false, TimeSpan.FromSeconds(60)), Is.True, "consent alert expected on first login");
        flow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(30));
        Assert.That(await E2EBackend.StaysWithoutDevicesAsync(SeededUser, Quiet), Is.True, "declined: no device row expected");
        Assert.That(PushToggleOn(), Is.False, "declined: switch off");

        AndroidDriver.TerminateApp(AndroidOptions.AppPackage);
        AndroidDriver.ActivateApp(AndroidOptions.AppPackage);
        Assert.That(flow.AnswerPushConsent(accept: true, TimeSpan.FromSeconds(15)), Is.False, "asked once: no alert after a cold start");
        flow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));
        Assert.That(await E2EBackend.StaysWithoutDevicesAsync(SeededUser, Quiet), Is.True, "still no device row after a cold start");
    }

    private bool PushToggleOn()
    {
        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(15));
        wait.UntilVisible(By.XPath("//*[@text='Konto' or @content-desc='Konto']")).Click();
        var on = wait.UntilVisible(ByAutoId("AccountPageToggleNotifications")).GetAttribute("checked") == "true";
        wait.UntilVisible(By.XPath("//*[@text='Otrzymane' or @content-desc='Otrzymane']")).Click();
        return on;
    }

    private void ForgetConsent()
    {
        AndroidDriver.TerminateApp(AndroidOptions.AppPackage);
        AndroidDriver.ExecuteScript("mobile: shell", new Dictionary<string, object>
        {
            ["command"] = "run-as",
            ["args"] = new[] { AndroidOptions.AppPackage, "sed", "-i", "-e", "/push_choice_/d", "-e", "/push_disabled_by_user/d", $"shared_prefs/{AndroidOptions.AppPackage}_preferences.xml" }
        });
        AndroidDriver.ActivateApp(AndroidOptions.AppPackage);
    }
}

/// <summary>
/// T-763 AC5: the Android 13+ notification permission is requested only after "Tak".
/// </summary>
[Category("Regression")]
public sealed class PushConsentPermissionFlowTests : AndroidTestBase
{
    protected override bool UsesSeededUser => true;

    protected override bool AutoGrantPermissions => false;

    [Test]
    public async Task FreshInstall_Accepts_PermissionPromptOnlyAfterTak_OneRegistration_SwitchOn()
    {
        const string permission = "android.permission.POST_NOTIFICATIONS";
        try
        {
            Shell("pm", "revoke", AndroidOptions.AppPackage, permission);
            Shell("pm", "clear-permission-flags", AndroidOptions.AppPackage, permission, "user-set", "user-fixed");
        }
        catch (WebDriverException ex) when (ex.Message.Contains("adb_shell", StringComparison.Ordinal))
        {
            E2EBackend.Unmet("Resetting the notification permission needs Appium started with --allow-insecure=uiautomator2:adb_shell.");
        }

        AndroidDriver.TerminateApp(AndroidOptions.AppPackage);
        Shell("run-as", AndroidOptions.AppPackage, "sed", "-i", "-e", "/push_choice_/d", "-e", "/push_disabled_by_user/d", $"shared_prefs/{AndroidOptions.AppPackage}_preferences.xml");
        AndroidDriver.ActivateApp(AndroidOptions.AppPackage);

        var flow = new AndroidOidcLoginFlow(AndroidDriver);
        LoginAsSeededUser(pushConsent: null);

        new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(60)).Until(d => d.FindElements(AndroidOidcLoginFlow.PushConsentMessage).Count > 0);
        Assert.That(AndroidDriver.FindElements(AllowButton), Is.Empty, "no OS permission prompt before Tak");
        Assert.That(await E2EBackend.CountDevicesAsync(SeededUser), Is.Zero, "no device row before Tak");

        AndroidDriver.FindElement(By.Id("android:id/button1")).Click();
        new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(15)).Until(d => d.FindElements(AllowButton).FirstOrDefault())!.Click();

        flow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(30));
        Assert.That(await E2EBackend.WaitForDevicesAsync(SeededUser, 1, TimeSpan.FromSeconds(30)), Is.EqualTo(1), "accepted: one device row");
        await Task.Delay(Quiet);
        Assert.That(await E2EBackend.CountDevicesAsync(SeededUser), Is.EqualTo(1), "accepted: exactly one registration");

        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(15));
        wait.UntilVisible(By.XPath("//*[@text='Konto' or @content-desc='Konto']")).Click();
        Assert.That(wait.UntilVisible(ByAutoId("AccountPageToggleNotifications")).GetAttribute("checked"), Is.EqualTo("true"), "accepted: switch on");
    }

    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(10);

    private static By AllowButton => By.Id("com.android.permissioncontroller:id/permission_allow_button");

    private void Shell(string command, params string[] args) =>
        AndroidDriver.ExecuteScript("mobile: shell", new Dictionary<string, object> { ["command"] = command, ["args"] = args });
}
