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
    [Test]
    public void AccountPage_NotificationToggle_FlipsSwitchAndStatusBothWays()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        authFlow.LoginWithKeycloakFromEnvironment();
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

    [Test]
    public void AccountPage_NotificationToggle_ShowsDeniedMessage_WhenPermissionDenied()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        authFlow.LoginWithKeycloakFromEnvironment();
        authFlow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));

        // Navigate to Account (Konto) tab
        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(15));
        var accountTab = wait.UntilVisible(By.XPath("//*[contains(@text, 'Konto')]"));
        accountTab.Click();

        var toggle = wait.UntilVisible(ByAutoId("AccountPageToggleNotifications"));
        toggle.Click();

        // Deny Android notification permission
        try
        {
            var permissionWait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(5));
            var denyButton = permissionWait.Until(d =>
            {
                var buttons = d.FindElements(By.Id("com.android.permissioncontroller:id/permission_deny_button"));
                return buttons.Count > 0 ? buttons[0] : null;
            });
            denyButton?.Click();
        }
        catch (WebDriverTimeoutException)
        {
            // autoGrantPermissions in AndroidDriverFactory grants the permission, so this path needs a session without it
            Assert.Inconclusive("No permission dialog: the tap only changed the push state, denial was not exercised.");
        }

        new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(20)).Until(d =>
            d.FindElement(ByAutoId("AccountPageLabelNotificationStatus")).Text.StartsWith("Brak uprawnień", StringComparison.Ordinal));
        Assert.That(toggle.GetAttribute("checked"), Is.EqualTo("false"), "Switch should stay off after denial");
    }
}
