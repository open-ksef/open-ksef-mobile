using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Support.UI;

namespace OpenKSeF.Mobile.E2E.Android.Support;

public sealed class AndroidTenantNavigationFlow(AndroidDriver driver)
{
    private readonly AndroidDriver _driver = driver;

    public void OpenTenantList()
    {
        var wait = new WaitHelper(_driver, TimeSpan.FromSeconds(45));
        var tenantsTab = wait.UntilVisible(By.XPath("//*[@text='Firmy' or @content-desc='Firmy']"));
        tenantsTab.Click();
        wait.UntilVisible(ByAutoId("TenantsPageCollectionViewTenants"));
        new WebDriverWait(_driver, TimeSpan.FromSeconds(30)).Until(d =>
            d.FindElements(ByAutoId("TenantsPageFrameTenantItem")).Count > 0 ||
            d.FindElements(ByAutoId("TenantsPageButtonAddEmptyState")).Any(e => e.Displayed));
    }

    public void SelectTenantWithInvoices()
    {
        OpenTenantList();
        var tenantCount = _driver.FindElements(ByAutoId("TenantsPageFrameTenantItem")).Count;
        Assert.That(tenantCount, Is.GreaterThan(0), "Expected at least one tenant item.");

        for (var i = 0; i < tenantCount; i++)
        {
            if (i > 0)
                OpenTenantList();
            _driver.FindElements(ByAutoId("TenantsPageFrameTenantItem"))[i].Click();

            try
            {
                new WebDriverWait(_driver, TimeSpan.FromSeconds(20)).Until(d =>
                    d.FindElements(ByAutoId("InvoiceListPageFrameInvoiceItem")).Count > 0);
                return;
            }
            catch (WebDriverTimeoutException)
            {
            }
        }

        Assert.Ignore("No tenant of the E2E user has invoices; seed them with open-ksef/scripts/setup-test-data.ps1.");
    }
}
