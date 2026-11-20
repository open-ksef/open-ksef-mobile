using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using SkiaSharp;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("QrSetup")]
public sealed class QrSetupFlowTests : AndroidTestBase
{
    [Test]
    public void QrSetupPage_HasExpectedElements()
    {
        OpenScanPage();

        Assert.That(AndroidDriver.FindElement(ByAutoId("ScanSetupQrPageTitle")).Displayed, Is.True, "QR setup page title should be visible");
        Assert.That(AndroidDriver.FindElement(ByAutoId("ScanSetupQrButtonGallery")).Displayed, Is.True, "Gallery button should be visible");
        Assert.That(AndroidDriver.FindElement(ByAutoId("ScanSetupQrButtonSkip")).Displayed, Is.True, "Skip button should be visible");
    }

    [Test]
    public void QrSetupPage_SkipButton_NavigatesToLogin()
    {
        OpenScanPage();

        AndroidDriver.FindElement(ByAutoId("ScanSetupQrButtonSkip")).Click();

        var wait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(30));
        wait.Until(d => d.FindElements(ByAutoId("ScanSetupQrPageTitle")).Count == 0 && d.FindElements(ByAutoId("LoginPageButtonLogin")).Count > 0);
        Assert.That(AndroidDriver.FindElement(ByAutoId("LoginPageButtonLogin")).Displayed, Is.True, "Login page should be visible after skipping QR setup");
    }

    [Test]
    public void QrWithStaleSetupToken_ViaGalleryPick_SetsServerAndLandsOnLogin()
    {
        var serverUrl = Environment.GetEnvironmentVariable("APP_EXTERNAL_BASE_URL");
        if (string.IsNullOrWhiteSpace(serverUrl))
            E2EBackend.Unmet("APP_EXTERNAL_BASE_URL is required for this QR E2E test.");

        var wait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(30));
        OpenScanPage();

        var qrPayload = $$"""{"type":"openksef-setup","version":1,"serverUrl":"{{serverUrl!.TrimEnd('/')}}","setupToken":"stale-token"}""";
        var qrImagePath = GenerateQrCodeImage(qrPayload);
        var devicePath = "/sdcard/Download/openksef-setup-qr.png";

        AndroidDriver.PushFile(devicePath, File.ReadAllBytes(qrImagePath));
        new AndroidOidcLoginFlow(AndroidDriver).Shell("am", "broadcast", "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE", "-d", $"file://{devicePath}");

        AndroidDriver.FindElement(ByAutoId("ScanSetupQrButtonGallery")).Click();

        try
        {
            new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(15)) { Message = "Android file picker did not show Download." }
                .Until(d => d.FindElements(By.XPath("//*[contains(@text, 'Download')]")).Count > 0);
            AndroidDriver.FindElements(By.XPath("//*[contains(@text, 'Download')]")).First().Click();

            new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(10)) { Message = "QR image not found in the Android file picker." }
                .Until(d => d.FindElements(By.XPath("//*[contains(@text, 'openksef-setup-qr') or starts-with(@content-desc, 'openksef-setup-qr')]")).FirstOrDefault())
                .Click();

            var switchButton = By.XPath("//*[@text='Przełącz' or @text='PRZEŁĄCZ']");
            wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException));
            wait.Until(d =>
            {
                d.FindElements(switchButton).FirstOrDefault()?.Click();
                return d.FindElements(ByAutoId("ScanSetupQrPageTitle")).Count == 0 &&
                    d.FindElements(ByAutoId("LoginPageButtonLogin")).Count > 0;
            });

            Assert.That(AndroidDriver.FindElement(ByAutoId("LoginPageButtonLogin")).Displayed, Is.True,
                "Login page should be visible after scanning a QR with a stale setup token.");
        }
        finally
        {
            try
            {
                AndroidDriver.ExecuteScript("mobile: shell", new Dictionary<string, object>
                {
                    ["command"] = "rm",
                    ["args"] = new[] { "-f", devicePath }
                });
            }
            catch { }

            if (File.Exists(qrImagePath))
                File.Delete(qrImagePath);
        }
    }

    private void OpenScanPage()
    {
        new AndroidOidcLoginFlow(AndroidDriver).Grant("android.permission.CAMERA");
        var wait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(30));
        wait.Until(d => d.FindElements(ByAutoId("LoginPageButtonLogin")).Count > 0 || d.FindElements(ByAutoId("LoginPageEntryServerUrl")).Count > 0);

        if (AndroidDriver.FindElements(ByAutoId("LoginPageButtonScanQr")).Count == 0)
            AndroidDriver.FindElement(ByAutoId("LoginPageButtonChangeServer")).Click();
        wait.Until(d => d.FindElements(ByAutoId("LoginPageButtonScanQr")).Count > 0);
        AndroidDriver.FindElement(ByAutoId("LoginPageButtonScanQr")).Click();

        wait.Until(d =>
        {
            d.FindElements(By.Id("com.android.permissioncontroller:id/permission_allow_foreground_only_button")).FirstOrDefault()?.Click();
            return d.FindElements(ByAutoId("ScanSetupQrButtonGallery")).Count > 0;
        });
    }

    /// <summary>
    /// Generates a minimal QR code PNG using ZXing.Net.
    /// Returns the path to the temporary file.
    /// </summary>
    private static string GenerateQrCodeImage(string content)
    {
        var writer = new ZXing.BarcodeWriterGeneric
        {
            Format = ZXing.BarcodeFormat.QR_CODE,
            Options = new ZXing.Common.EncodingOptions
            {
                Width = 400,
                Height = 400,
                Margin = 2
            }
        };

        var matrix = writer.Encode(content);
        var tempPath = Path.Combine(Path.GetTempPath(), $"openksef-qr-{Guid.NewGuid():N}.png");
        WriteBitMatrixAsPng(matrix, tempPath);

        return tempPath;
    }

    private static void WriteBitMatrixAsPng(ZXing.Common.BitMatrix matrix, string path)
    {
        using var bitmap = new SKBitmap(matrix.Width, matrix.Height);
        for (var y = 0; y < matrix.Height; y++)
            for (var x = 0; x < matrix.Width; x++)
                bitmap.SetPixel(x, y, matrix[x, y] ? SKColors.Black : SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, png.ToArray());
    }
}
