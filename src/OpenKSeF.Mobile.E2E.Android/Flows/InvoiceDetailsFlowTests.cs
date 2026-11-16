using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using SkiaSharp;
using ZXing;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("Regression")]
public sealed class InvoiceDetailsFlowTests : AndroidTestBase
{
    protected override bool UsesSeededUser => true;

    [Test]
    public void InvoiceDetails_DisplaysMetadata_AndBackNavigatesToList()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        LoginAsSeededUser();
        authFlow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));

        var tenantFlow = new AndroidTenantNavigationFlow(AndroidDriver);
        tenantFlow.SelectTenantWithInvoices();

        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(45));
        wait.UntilVisible(ByAutoId("InvoiceListPageCollectionViewInvoices"));

        var firstInvoiceItem = AndroidDriver.FindElements(ByAutoId("InvoiceListPageFrameInvoiceItem"))
            .FirstOrDefault();
        Assert.That(firstInvoiceItem, Is.Not.Null, "Expected at least one invoice item to open details.");
        firstInvoiceItem!.Click();

        var detailAssertions = new AndroidInvoiceDetailsAssertions(AndroidDriver);
        detailAssertions.AssertDetailsVisible();

        AndroidDriver.Navigate().Back();

        var backWait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(30));
        backWait.Until(driver =>
            driver.FindElements(ByAutoId("InvoiceListPageCollectionViewInvoices")).Count > 0);
    }

    [Test]
    public async Task QrPage_DecodesToZbpPayloadOfInvoice()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        LoginAsSeededUser();
        authFlow.WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));
        new AndroidTenantNavigationFlow(AndroidDriver).SelectTenantWithInvoices();

        AndroidDriver.FindElements(ByAutoId("InvoiceListPageFrameInvoiceItem")).First().Click();
        var details = new AndroidInvoiceDetailsAssertions(AndroidDriver);
        details.AssertDetailsVisible();
        details.ScrollIntoView("InvoiceDetailsPageButtonShowQr").Click();

        var qr = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(45)).UntilVisible(ByAutoId("QrCodePageImageQr"));
        using var png = SKBitmap.Decode(((ITakesScreenshot)qr).GetScreenshot().AsByteArray);
        using var image = png.Copy(SKColorType.Rgba8888);
        var reader = new BarcodeReaderGeneric { Options = { PossibleFormats = [BarcodeFormat.QR_CODE], TryHarder = true } };
        var decoded = reader.Decode(new RGBLuminanceSource(image.Bytes, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.RGBA32));
        Assert.That(decoded, Is.Not.Null, "QR image on QrCodePage did not decode.");

        var segments = decoded!.Text.Split('|');
        Assert.That(segments, Has.Length.EqualTo(9), decoded.Text);
        var numbers = await E2EBackend.GetInvoiceNumbersAsync(SeededUser);
        Assert.Multiple(() =>
        {
            Assert.That(segments[0], Is.EqualTo(E2EBackend.SeedVendorNip));
            Assert.That(segments[1], Is.EqualTo("PL"));
            Assert.That(segments[2], Is.EqualTo(E2EBackend.SeedAccount));
            Assert.That(segments[3], Is.EqualTo(E2EBackend.SeedGrossGrosze));
            Assert.That(numbers, Does.Contain(segments[5]));
        });
    }
}
