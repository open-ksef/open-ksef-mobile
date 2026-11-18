using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Support.UI;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("Regression")]
public sealed class InvoiceLineSheetFlowTests : AndroidTestBase
{
    protected override bool UsesSeededUser => true;

    [Test]
    public async Task LineSheet_EditRejectNegativePriceSave_UpdatesTotal()
    {
        await E2EBackend.CompleteSellerProfilesAsync(SeededUser);
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        LoginAsSeededUser();
        authFlow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));
        new AndroidTenantNavigationFlow(AndroidDriver).SelectTenantWithInvoices();

        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(45));
        wait.UntilVisible(By.XPath("//*[@text='Wystawione' or @content-desc='Wystawione']")).Click();
        wait.UntilVisible(ByAutoId("IssuedInvoiceListPageButtonNewEmpty")).Click();

        wait.UntilVisible(ByAutoId("InvoiceDraftPageLabelTotal"));
        AndroidDriver.FindElement(MobileBy.AndroidUIAutomator(
            "new UiScrollable(new UiSelector().scrollable(true)).scrollIntoView(new UiSelector().resourceId(\"" + AndroidOptions.AppPackage + ":id/InvoiceDraftPageFrameLine\"))")).Click();
        wait.UntilVisible(ByAutoId("InvoiceLineSheetLabelTitle"));
        AndroidDriver.FindElement(ByAutoId("InvoiceLineSheetEntryName")).SendKeys("Konsultacje");
        var quantity = AndroidDriver.FindElement(ByAutoId("InvoiceLineSheetEntryQuantity"));
        quantity.Clear();
        quantity.SendKeys("2");
        var price = AndroidDriver.FindElement(ByAutoId("InvoiceLineSheetEntryPrice"));
        price.SendKeys("-1");
        AndroidDriver.FindElement(ByAutoId("InvoiceLineSheetButtonSave")).Click();

        Assert.That(wait.UntilVisible(ByAutoId("InvoiceLineSheetLabelError")).Text, Is.EqualTo("Cena netto nie może być ujemna."));

        price.Clear();
        price.SendKeys("100");
        AndroidDriver.FindElement(ByAutoId("InvoiceLineSheetButtonSave")).Click();

        new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(15)).Until(d => d.FindElements(ByAutoId("InvoiceLineSheetLabelTitle")).Count == 0);
        new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(15)).Until(d =>
            d.FindElement(ByAutoId("InvoiceDraftPageLabelTotal")).Text.StartsWith("246,00"));
    }
}
