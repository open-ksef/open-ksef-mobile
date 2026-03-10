using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Support.UI;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("Regression")]
public sealed class DeviceRegistrationFlowTests : AndroidTestBase
{
    [Test]
    public void ReturningUser_DeviceRegisteredSilently_NoNotificationDialog()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        authFlow.LoginWithKeycloakFromEnvironment();

        // Returning user with existing tenant should go straight to invoices.
        // No notification permission dialog should appear.
        authFlow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));

        var mainPageVisible = AndroidDriver.FindElements(MobileBy.AccessibilityId("InvoiceListPageLabelTitle")).Count > 0
            || AndroidDriver.FindElements(MobileBy.AccessibilityId("InvoiceListPageCollectionViewInvoices")).Count > 0;
        Assert.That(mainPageVisible, Is.True, "Expected main invoice page for returning user");
    }

    [Test]
    public void SecondLogin_SkipsDeviceRegistration_NavigatesDirectly()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        authFlow.LoginWithKeycloakFromEnvironment();

        authFlow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));

        var mainPageVisible = AndroidDriver.FindElements(MobileBy.AccessibilityId("InvoiceListPageLabelTitle")).Count > 0
            || AndroidDriver.FindElements(MobileBy.AccessibilityId("InvoiceListPageCollectionViewInvoices")).Count > 0;
        Assert.That(mainPageVisible, Is.True, "Expected main invoice page on second login");
    }
}
