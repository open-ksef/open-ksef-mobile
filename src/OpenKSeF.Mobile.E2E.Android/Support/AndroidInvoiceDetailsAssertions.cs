using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
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
        var copyTransfer = ScrollIntoView("InvoiceDetailsPageButtonCopyTransfer");
        var showQr = ScrollIntoView("InvoiceDetailsPageButtonShowQr");

        Assert.Multiple(() =>
        {
            Assert.That(amount.Text, Is.Not.Empty);
            Assert.That(vendorName.Text, Is.Not.Empty);
            Assert.That(ksefNumber.Text, Is.Not.Empty);
            Assert.That(copyTransfer.Displayed, Is.True);
            Assert.That(showQr.Displayed, Is.True);
        });
    }

    public IWebElement ScrollIntoView(string automationId) =>
        _driver.FindElement(MobileBy.AndroidUIAutomator(
            $"new UiScrollable(new UiSelector().scrollable(true)).scrollIntoView(new UiSelector().resourceId(\"{_driver.CurrentPackage}:id/{automationId}\"))"));
}
