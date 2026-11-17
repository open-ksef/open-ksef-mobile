using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("Regression")]
public sealed class SettingsNotificationFlowTests : AndroidTestBase
{
    protected override bool UsesSeededUser => true;

    [Test]
    public void AccountPage_NotificationToggle_FlipsSwitchAndStatusBothWays()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        LoginAsSeededUser();
        authFlow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));

        // Navigate to Account (Konto) tab
        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(15));
        var accountTab = wait.UntilVisible(By.XPath("//*[contains(@text, 'Konto')]"));
        accountTab.Click();

        // Verify notifications section loaded
        var notificationSection = wait.UntilVisible(ByAutoId("AccountPageSectionNotifications"));
        Assert.That(notificationSection.Displayed, Is.True, "Notification settings section should be visible");

        var startedOn = IsToggleOn(wait);

        // Two taps cover both directions and leave the install as it was, because noReset keeps the opt-out key
        TapToggleAndAssert(wait, expectOn: !startedOn);
        TapToggleAndAssert(wait, expectOn: startedOn);
    }

    private void TapToggleAndAssert(WaitHelper wait, bool expectOn)
    {
        wait.UntilVisible(ByAutoId("AccountPageToggleNotifications")).Click();

        // Handle Android notification permission dialog (API 33+)
        try
        {
            var permissionWait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(5));
            var allowButton = permissionWait.Until(d =>
            {
                var buttons = d.FindElements(By.Id("com.android.permissioncontroller:id/permission_allow_button"));
                return buttons.Count > 0 ? buttons[0] : null;
            });
            allowButton?.Click();
        }
        catch (WebDriverTimeoutException)
        {
            // Permission dialog may not appear if already granted or API < 33
        }

        var expectedStatus = expectOn ? "Powiadomienia push są włączone." : "Powiadomienia push są wyłączone.";
        new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(20)).Until(d =>
            d.FindElement(ByAutoId("AccountPageLabelNotificationStatus")).Text.StartsWith(expectedStatus, StringComparison.Ordinal));
        Assert.That(IsToggleOn(wait), Is.EqualTo(expectOn), "Switch should match the status text");
    }

    private static bool IsToggleOn(WaitHelper wait) =>
        wait.UntilVisible(ByAutoId("AccountPageToggleNotifications")).GetAttribute("checked") == "true";
}

[Category("Regression")]
public sealed class SettingsNotificationDeniedFlowTests : AndroidTestBase
{
    protected override bool UsesSeededUser => true;

    protected override bool AutoGrantPermissions => false;

    [Test]
    public void AccountPage_NotificationToggle_ShowsDeniedMessage_WhenPermissionDenied()
    {
        const string permission = "android.permission.POST_NOTIFICATIONS";
        try
        {
            AndroidDriver.ExecuteScript("mobile: shell", new Dictionary<string, object>
            {
                ["command"] = "pm",
                ["args"] = new[] { "revoke", AndroidOptions.AppPackage, permission }
            });
            AndroidDriver.ExecuteScript("mobile: shell", new Dictionary<string, object>
            {
                ["command"] = "pm",
                ["args"] = new[] { "clear-permission-flags", AndroidOptions.AppPackage, permission, "user-set", "user-fixed" }
            });
        }
        catch (WebDriverException ex) when (ex.Message.Contains("adb_shell", StringComparison.Ordinal))
        {
            E2EBackend.Unmet("Resetting the notification permission needs Appium started with --allow-insecure=uiautomator2:adb_shell.");
        }

        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        authFlow.SignOut();
        LoginAsSeededUser();
        authFlow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));

        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(15));
        wait.UntilVisible(By.XPath("//*[@text='Konto' or @content-desc='Konto']")).Click();

        var toggle = wait.UntilVisible(ByAutoId("AccountPageToggleNotifications"));
        Assert.That(toggle.GetAttribute("checked"), Is.EqualTo("false"), "Push should start off without the permission");
        toggle.Click();

        var denyButton = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(10)).Until(d =>
            d.FindElements(By.Id("com.android.permissioncontroller:id/permission_deny_button")).FirstOrDefault());
        denyButton.Click();

        new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(20)).Until(d =>
            d.FindElement(ByAutoId("AccountPageLabelNotificationStatus")).Text.StartsWith("Brak uprawnień", StringComparison.Ordinal));
        Assert.That(toggle.GetAttribute("checked"), Is.EqualTo("false"), "Switch should stay off after denial");
    }
}
