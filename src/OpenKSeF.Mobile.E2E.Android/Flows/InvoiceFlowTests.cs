using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("Regression")]
public sealed class InvoiceFlowTests : AndroidTestBase
{
    protected override bool UsesSeededUser => true;

    [Test]
    public void InvoiceList_RendersItems_AndSupportsScrollAndRefresh()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        LoginAsSeededUser();
        authFlow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));

        var tenantFlow = new AndroidTenantNavigationFlow(AndroidDriver);
        tenantFlow.SelectTenantWithInvoices();

        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(45));
        wait.UntilVisible(ByAutoId("InvoiceListPageCollectionViewInvoices"));

        var invoiceItems = AndroidDriver.FindElements(ByAutoId("InvoiceListPageFrameInvoiceItem"));
        Assert.That(invoiceItems.Count, Is.GreaterThan(0), "Expected at least one invoice item.");

        var firstInvoiceTexts = invoiceItems[0].FindElements(By.ClassName("android.widget.TextView"))
            .Select(label => label.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text));
        Assert.That(firstInvoiceTexts, Is.Not.Empty, "Expected the first invoice item to show text.");

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

        wait.UntilVisible(ByAutoId("InvoiceListPageCollectionViewInvoices"));

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
            driver.FindElements(ByAutoId("InvoiceListPageCollectionViewInvoices")).Count > 0);
    }

    [Test]
    public async Task Offline_ShowsOwnCachedInvoicesLabeledOffline()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        var tenantFlow = new AndroidTenantNavigationFlow(AndroidDriver);
        var other = await E2EBackend.CreateSeededUserAsync();
        try
        {
            var otherNumbers = await E2EBackend.GetInvoiceNumbersAsync(other);
            authFlow.Login(other.Username, other.Password);
            authFlow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));
            tenantFlow.SelectTenantWithInvoices();
            authFlow.SignOut();

            LoginAsSeededUser();
            authFlow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));
            tenantFlow.SelectTenantWithInvoices();
            var ownNumbers = await E2EBackend.GetInvoiceNumbersAsync(SeededUser);
            var shown = ownNumbers.Where(AndroidDriver.PageSource.Contains).ToList();
            Assert.That(shown, Is.Not.Empty, "Expected an own invoice on the list before going offline.");

            SetAirplaneMode(true);
            AndroidDriver.TerminateApp(AndroidOptions.AppPackage);
            AndroidDriver.ActivateApp(AndroidOptions.AppPackage);

            new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(60)).UntilVisible(By.XPath("//*[contains(@text,'Tryb offline')]"));
            var source = AndroidDriver.PageSource;
            Assert.Multiple(() =>
            {
                Assert.That(shown, Has.All.Matches<string>(source.Contains), "Cached own invoices missing offline.");
                Assert.That(otherNumbers.Where(source.Contains), Is.Empty, "Another user's cached invoice shown offline.");
            });
        }
        finally
        {
            SetAirplaneMode(false);
            await E2EBackend.DeleteUserAsync(other);
        }
    }

    private void SetAirplaneMode(bool on) =>
        AndroidDriver.ExecuteScript("mobile: shell", new Dictionary<string, object>
        {
            ["command"] = "cmd",
            ["args"] = new[] { "connectivity", "airplane-mode", on ? "enable" : "disable" }
        });
}

