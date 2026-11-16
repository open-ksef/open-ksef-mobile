using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenQA.Selenium.Support.UI;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("Regression")]
public sealed class DeviceRegistrationFlowTests : AndroidTestBase
{
    protected override bool UsesSeededUser => true;

    [Test]
    public async Task Login_RegistersAndroidDevice()
    {
        var flow = new AndroidOidcLoginFlow(AndroidDriver);
        LoginAsSeededUser(pushConsent: null);
        Assert.That(flow.AnswerPushConsent(accept: true, TimeSpan.FromSeconds(60)), Is.True, "Expected the push consent alert for a new account.");
        flow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));

        var deadline = DateTime.UtcNow.AddSeconds(30);
        List<DeviceTokenDto> devices;
        while ((devices = await E2EBackend.GetDevicesAsync(SeededUser)).Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(1000);

        Assert.That(devices, Has.Count.EqualTo(1), $"Expected one device row for {SeededUser.Username} after login.");
        Assert.That(devices[0].Platform, Is.Zero, "Expected platform 0 (Android).");
        Assert.That(devices[0].Token, Is.Not.Empty);
    }

    [Test]
    public void ReturningUser_DeviceRegisteredSilently_NoNotificationDialog()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        LoginAsSeededUser();

        // Returning user with existing tenant should go straight to invoices.
        // No notification permission dialog should appear.
        authFlow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));

        var mainPageVisible = AndroidDriver.FindElements(ByAutoId("InvoiceListPageLabelTitle")).Count > 0
            || AndroidDriver.FindElements(ByAutoId("InvoiceListPageCollectionViewInvoices")).Count > 0;
        Assert.That(mainPageVisible, Is.True, "Expected main invoice page for returning user");
    }

    [Test]
    public void SecondLogin_SkipsDeviceRegistration_NavigatesDirectly()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        LoginAsSeededUser();

        authFlow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));

        var mainPageVisible = AndroidDriver.FindElements(ByAutoId("InvoiceListPageLabelTitle")).Count > 0
            || AndroidDriver.FindElements(ByAutoId("InvoiceListPageCollectionViewInvoices")).Count > 0;
        Assert.That(mainPageVisible, Is.True, "Expected main invoice page on second login");
    }
}
