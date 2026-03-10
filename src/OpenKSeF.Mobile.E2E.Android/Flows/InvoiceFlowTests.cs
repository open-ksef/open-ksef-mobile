using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Support.UI;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("Regression")]
public sealed class InvoiceFlowTests : AndroidTestBase
{
    [Test]
    public void InvoiceList_RendersItems_AndSupportsScrollAndRefresh()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        authFlow.LoginWithKeycloakFromEnvironment();
        authFlow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));

        var tenantFlow = new AndroidTenantNavigationFlow(AndroidDriver);
        tenantFlow.SelectAnyTenant();

        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(45));
        wait.UntilVisible(MobileBy.AccessibilityId("InvoiceListPageCollectionViewInvoices"));

        var invoiceItems = AndroidDriver.FindElements(MobileBy.AccessibilityId("InvoiceListPageFrameInvoiceItem"));
        Assert.That(invoiceItems.Count, Is.GreaterThan(0), "Expected at least one invoice item.");

        var firstInvoiceText = invoiceItems[0].Text;
        Assert.That(firstInvoiceText, Is.Not.Empty);

        // Scroll a bit down and ensure list still present.
        AndroidDriver.ExecuteScript("mobile: scrollGesture", new Dictionary<string, object>
        {
            ["left"] = 100,
            ["top"] = 400,
            ["width"] = 800,
            ["height"] = 1200,
            ["direction"] = "down",
            ["percent"] = 0.75
        });

        wait.UntilVisible(MobileBy.AccessibilityId("InvoiceListPageCollectionViewInvoices"));

        // Trigger a refresh by waiting for list stability after gesture-based refresh intent.
        AndroidDriver.ExecuteScript("mobile: swipeGesture", new Dictionary<string, object>
        {
            ["left"] = 100,
            ["top"] = 250,
            ["width"] = 800,
            ["height"] = 600,
            ["direction"] = "down",
            ["percent"] = 0.85
        });

        var stableWait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(30));
        stableWait.Until(driver =>
            driver.FindElements(MobileBy.AccessibilityId("InvoiceListPageCollectionViewInvoices")).Count > 0);
    }
}
