using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Support.UI;
using SkiaSharp;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("Regression")]
public sealed class InvoiceLineSheetFlowTests : AndroidTestBase
{
    protected override bool UsesSeededUser => true;

    [Test]
    public async Task LineSheet_EditRejectNegativePriceSave_UpdatesTotal()
    {
        var wait = await OpenDraftAsync();
        OpenLineSheet(wait);
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

    [Test]
    public async Task LineSheet_DarkModeAndThemeSwitch_RecolorsBarsAndClose()
    {
        try
        {
            var wait = await OpenDraftAsync();
            UiMode("yes");
            var title = AndroidDriver.FindElement(By.XPath("//*[@text='Nowa faktura']"));
            AssertDarkSoon(() => ToolbarPixel(title), true, "draft nav bar after first switch to dark");

            OpenLineSheet(wait);
            var close = AndroidDriver.FindElement(ByAutoId("InvoiceLineSheetButtonClose"));
            Assert.That(close.Displayed, Is.True, "close X in dark mode");
            AssertDarkSoon(StatusBarPixel, true, "sheet status bar in dark mode");
            AssertDarkSoon(() => Extreme(close, brightest: true), false, "close X light on dark sheet");
            Capture("01-sheet-dark");

            UiMode("no");
            AssertDarkSoon(StatusBarPixel, false, "sheet status bar after switch to light");
            AssertDarkSoon(() => Extreme(close, brightest: false), true, "close X dark on light sheet");
            Capture("02-sheet-light");

            close.Click();
            new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(15)).Until(d => d.FindElements(ByAutoId("InvoiceLineSheetLabelTitle")).Count == 0);
            AssertDarkSoon(() => ToolbarPixel(title), false, "draft nav bar after switch to light");
            AssertDarkSoon(StatusBarPixel, false, "draft status bar after switch to light");
            Capture("03-draft-light");

            UiMode("yes");
            AssertDarkSoon(() => ToolbarPixel(title), true, "draft nav bar after switch to dark");
            AssertDarkSoon(StatusBarPixel, true, "draft status bar after switch to dark");
            Capture("04-draft-dark");
        }
        finally
        {
            UiMode("no");
        }
    }

    private async Task<WaitHelper> OpenDraftAsync()
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
        return wait;
    }

    private void OpenLineSheet(WaitHelper wait)
    {
        AndroidDriver.FindElement(MobileBy.AndroidUIAutomator(
            "new UiScrollable(new UiSelector().scrollable(true)).scrollIntoView(new UiSelector().resourceId(\"" + AndroidOptions.AppPackage + ":id/InvoiceDraftPageFrameLine\"))")).Click();
        wait.UntilVisible(ByAutoId("InvoiceLineSheetLabelTitle"));
    }

    private void UiMode(string night) =>
        AndroidDriver.ExecuteScript("mobile: shell", new Dictionary<string, object> { ["command"] = "cmd", ["args"] = new[] { "uimode", "night", night } });

    private void AssertDarkSoon(Func<SKColor> pixel, bool dark, string what)
    {
        var last = SKColors.Transparent;
        try
        {
            new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(10)).Until(_ => IsDark(last = pixel()) == dark);
        }
        catch (WebDriverTimeoutException)
        {
            Capture("fail-" + what.Replace(' ', '-'));
            Assert.Fail($"{what}: expected {(dark ? "dark" : "light")}, pixel {last}");
        }
    }

    private static bool IsDark(SKColor c) => 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue < 128;

    private SKBitmap Screen() => SKBitmap.Decode(AndroidDriver.GetScreenshot().AsByteArray);

    private SKColor StatusBarPixel()
    {
        using var screen = Screen();
        return screen.GetPixel(screen.Width * 2 / 5, 3);
    }

    private SKColor ToolbarPixel(IWebElement title)
    {
        using var screen = Screen();
        return screen.GetPixel(2, title.Location.Y + title.Size.Height / 2);
    }

    private static SKColor Extreme(IWebElement element, bool brightest)
    {
        using var png = SKBitmap.Decode(((ITakesScreenshot)element).GetScreenshot().AsByteArray);
        var pixels = png.Pixels;
        return brightest ? pixels.MaxBy(Luma) : pixels.MinBy(Luma);
    }

    private static double Luma(SKColor c) => 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;

    private void Capture(string name) =>
        ScreenshotHelper.Capture(AndroidDriver, Path.Combine(TestContext.CurrentContext.WorkDirectory, "TestResults", "ThemeSwitch"), name);
}
