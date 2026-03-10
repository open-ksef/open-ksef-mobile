using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Support.UI;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("Regression")]
public sealed class InvoiceDetailsFlowTests : AndroidTestBase
{
    [Test]
    public void InvoiceDetails_DisplaysMetadata_AndBackNavigatesToList()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        authFlow.LoginWithKeycloakFromEnvironment();
        authFlow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));

        var tenantFlow = new AndroidTenantNavigationFlow(AndroidDriver);
        tenantFlow.SelectAnyTenant();

        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(45));
        wait.UntilVisible(MobileBy.AccessibilityId("InvoiceListPageCollectionViewInvoices"));

        var firstInvoiceItem = AndroidDriver.FindElements(MobileBy.AccessibilityId("InvoiceListPageFrameInvoiceItem"))
            .FirstOrDefault();
        Assert.That(firstInvoiceItem, Is.Not.Null, "Expected at least one invoice item to open details.");
        firstInvoiceItem!.Click();

        var detailAssertions = new AndroidInvoiceDetailsAssertions(AndroidDriver);
        detailAssertions.AssertDetailsVisible();

        AndroidDriver.Navigate().Back();

        var backWait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(30));
        backWait.Until(driver =>
            driver.FindElements(MobileBy.AccessibilityId("InvoiceListPageCollectionViewInvoices")).Count > 0);
    }
}
