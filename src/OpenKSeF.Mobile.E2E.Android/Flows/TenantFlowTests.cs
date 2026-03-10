using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Support.UI;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("Regression")]
public sealed class TenantFlowTests : AndroidTestBase
{
    [Test]
    public void TenantList_SelectionFlow_ChangesCurrentTenantContext()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        authFlow.LoginWithKeycloakFromEnvironment();

        var tenantFlow = new AndroidTenantNavigationFlow(AndroidDriver);
        tenantFlow.OpenTenantList();

        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(45));
        var tenantList = wait.UntilVisible(MobileBy.AccessibilityId("TenantsPageCollectionViewTenants"));
        Assert.That(tenantList, Is.Not.Null);

        var tenantItems = AndroidDriver.FindElements(MobileBy.AccessibilityId("TenantsPageFrameTenantItem"));
        Assert.That(tenantItems.Count, Is.GreaterThanOrEqualTo(2), "Expected at least two tenant items for selection flow.");

        tenantItems[0].Click();

        var invoiceWait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(30));
        invoiceWait.Until(driver => driver.FindElements(MobileBy.AccessibilityId("InvoiceListPageLabelTitle")).Count > 0);

        var invoiceTitle = AndroidDriver.FindElement(MobileBy.AccessibilityId("InvoiceListPageLabelTitle"));
        Assert.That(invoiceTitle.Text, Does.Contain("Faktury"));

        AndroidDriver.Navigate().Back();

        wait.UntilVisible(MobileBy.AccessibilityId("TenantsPageCollectionViewTenants"));

        var secondTenant = AndroidDriver.FindElements(MobileBy.AccessibilityId("TenantsPageFrameTenantItem")).Last();
        secondTenant.Click();

        invoiceWait.Until(driver => driver.FindElements(MobileBy.AccessibilityId("InvoiceListPageCollectionViewInvoices")).Count > 0);
        var invoiceList = AndroidDriver.FindElement(MobileBy.AccessibilityId("InvoiceListPageCollectionViewInvoices"));
        Assert.That(invoiceList, Is.Not.Null);
    }
}
