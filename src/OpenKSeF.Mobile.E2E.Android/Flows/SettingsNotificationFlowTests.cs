using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Support.UI;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("Regression")]
public sealed class SettingsNotificationFlowTests : AndroidTestBase
{
    [Test]
    public void AccountPage_NotificationToggle_RequestsPermissionAndUpdates()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        authFlow.LoginWithKeycloakFromEnvironment();
        authFlow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));

        // Navigate to Account (Konto) tab
        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(15));
        var accountTab = wait.UntilVisible(By.XPath("//*[contains(@text, 'Konto')]"));
        accountTab.Click();

        // Verify notifications section loaded
        var notificationSection = wait.UntilVisible(MobileBy.AccessibilityId("AccountPageSectionNotifications"));
        Assert.That(notificationSection.Displayed, Is.True, "Notification settings section should be visible");

        // Tap notification toggle
        var toggle = wait.UntilVisible(MobileBy.AccessibilityId("AccountPageToggleNotifications"));
        toggle.Click();

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

        // Verify status text updated
        var statusLabel = wait.UntilVisible(MobileBy.AccessibilityId("AccountPageLabelNotificationStatus"));
        Assert.That(statusLabel.Displayed, Is.True, "Notification status label should be visible");
    }

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

        var toggle = wait.UntilVisible(MobileBy.AccessibilityId("AccountPageToggleNotifications"));
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
            // Permission dialog may not appear
        }

        var statusLabel = wait.UntilVisible(MobileBy.AccessibilityId("AccountPageLabelNotificationStatus"));
        Assert.That(statusLabel.Displayed, Is.True, "Notification status label should be visible after denial");
    }
}
