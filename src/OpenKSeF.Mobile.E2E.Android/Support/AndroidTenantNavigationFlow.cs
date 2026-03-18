using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium.Android;

namespace OpenKSeF.Mobile.E2E.Android.Support;

public sealed class AndroidTenantNavigationFlow(AndroidDriver driver)
{
    private readonly AndroidDriver _driver = driver;

    public void OpenTenantList()
    {
        var wait = new WaitHelper(_driver, TimeSpan.FromSeconds(45));
        var tenantsTab = wait.UntilVisible(By.XPath("//*[contains(@text, 'NIP-y')]"));
        tenantsTab.Click();
        wait.UntilVisible(ByAutoId("TenantsPageCollectionViewTenants"));
    }

    public void SelectAnyTenant()
    {
        OpenTenantList();
        var wait = new WaitHelper(_driver, TimeSpan.FromSeconds(45));
        wait.UntilVisible(ByAutoId("TenantsPageCollectionViewTenants"));

        var tenantItems = _driver.FindElements(ByAutoId("TenantsPageFrameTenantItem"));
        Assert.That(tenantItems.Count, Is.GreaterThan(0), "Expected at least one tenant item.");
        tenantItems[0].Click();
    }
}
