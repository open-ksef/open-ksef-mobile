using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;
using OpenQA.Selenium.Appium.Android;

namespace OpenKSeF.Mobile.E2E.Android.Support;

public sealed class AndroidInvoiceDetailsAssertions(AndroidDriver driver)
{
    private readonly AndroidDriver _driver = driver;

    public void AssertDetailsVisible()
    {
        var wait = new WaitHelper(_driver, TimeSpan.FromSeconds(45));

        var amount = wait.UntilVisible(ByAutoId("InvoiceDetailsPageLabelAmount"));
        var vendorName = wait.UntilVisible(ByAutoId("InvoiceDetailsPageLabelVendorName"));
        var ksefNumber = wait.UntilVisible(ByAutoId("InvoiceDetailsPageLabelKsefNumber"));
        var copyTransfer = wait.UntilVisible(ByAutoId("InvoiceDetailsPageButtonCopyTransfer"));
        var showQr = wait.UntilVisible(ByAutoId("InvoiceDetailsPageButtonShowQr"));

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
