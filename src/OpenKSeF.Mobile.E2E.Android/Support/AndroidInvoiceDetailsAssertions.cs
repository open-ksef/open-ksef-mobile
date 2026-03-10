using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace OpenKSeF.Mobile.E2E.Android.Support;

public sealed class AndroidInvoiceDetailsAssertions(AndroidDriver driver)
{
    private readonly AndroidDriver _driver = driver;

    public void AssertDetailsVisible()
    {
        var wait = new WaitHelper(_driver, TimeSpan.FromSeconds(45));

        var amount = wait.UntilVisible(MobileBy.AccessibilityId("InvoiceDetailsPageLabelAmount"));
        var vendorName = wait.UntilVisible(MobileBy.AccessibilityId("InvoiceDetailsPageLabelVendorName"));
        var ksefNumber = wait.UntilVisible(MobileBy.AccessibilityId("InvoiceDetailsPageLabelKsefNumber"));
        var copyTransfer = wait.UntilVisible(MobileBy.AccessibilityId("InvoiceDetailsPageButtonCopyTransfer"));
        var showQr = wait.UntilVisible(MobileBy.AccessibilityId("InvoiceDetailsPageButtonShowQr"));

        Assert.Multiple(() =>
        {
            Assert.That(amount.Text, Is.Not.Empty);
            Assert.That(vendorName.Text, Is.Not.Empty);
            Assert.That(ksefNumber.Text, Is.Not.Empty);
            Assert.That(copyTransfer.Displayed, Is.True);
            Assert.That(showQr.Displayed, Is.True);
        });
    }
}
