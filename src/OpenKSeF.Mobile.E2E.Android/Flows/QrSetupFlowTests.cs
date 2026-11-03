using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("QrSetup")]
public sealed class QrSetupFlowTests : AndroidTestBase
{
    [Test]
    public void QrSetupPage_HasExpectedElements()
    {
        var wait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(30));

        wait.Until(d =>
            d.FindElements(ByAutoId("ScanSetupQrPageTitle")).Count > 0 ||
            d.FindElements(ByAutoId("LoginPageEntryEmail")).Count > 0);

        var isOnQrPage = AndroidDriver.FindElements(ByAutoId("ScanSetupQrPageTitle")).Count > 0;
        if (!isOnQrPage)
        {
            Assert.Ignore("App did not start on QR setup page (already configured). Reset app data to test first-launch flow.");
            return;
        }

        var title = AndroidDriver.FindElement(ByAutoId("ScanSetupQrPageTitle"));
        Assert.That(title.Displayed, Is.True, "QR setup page title should be visible");

        var galleryButton = AndroidDriver.FindElement(ByAutoId("ScanSetupQrButtonGallery"));
        Assert.That(galleryButton.Displayed, Is.True, "Gallery button should be visible");

        var skipButton = AndroidDriver.FindElement(ByAutoId("ScanSetupQrButtonSkip"));
        Assert.That(skipButton.Displayed, Is.True, "Skip button should be visible");
    }

    [Test]
    public void QrSetupPage_SkipButton_NavigatesToLogin()
    {
        var wait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(30));

        wait.Until(d =>
            d.FindElements(ByAutoId("ScanSetupQrPageTitle")).Count > 0 ||
            d.FindElements(ByAutoId("LoginPageEntryEmail")).Count > 0);

        var isOnQrPage = AndroidDriver.FindElements(ByAutoId("ScanSetupQrPageTitle")).Count > 0;
        if (!isOnQrPage)
        {
            Assert.Ignore("App did not start on QR setup page (already configured).");
            return;
        }

        var skipButton = AndroidDriver.FindElement(ByAutoId("ScanSetupQrButtonSkip"));
        skipButton.Click();

        wait.Until(d => d.FindElements(ByAutoId("LoginPageEntryEmail")).Count > 0);

        var emailEntry = AndroidDriver.FindElement(ByAutoId("LoginPageEntryEmail"));
        Assert.That(emailEntry.Displayed, Is.True, "Login page should be visible after skipping QR setup");
    }

    [Test]
    public void QrWithStaleSetupToken_ViaGalleryPick_SetsServerAndLandsOnLogin()
    {
        var serverUrl = Environment.GetEnvironmentVariable("APP_EXTERNAL_BASE_URL");

        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            Assert.Ignore("APP_EXTERNAL_BASE_URL is required for this QR E2E test.");
            return;
        }

        var wait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(30));

        wait.Until(d =>
            d.FindElements(ByAutoId("ScanSetupQrPageTitle")).Count > 0 ||
            d.FindElements(ByAutoId("LoginPageEntryEmail")).Count > 0);

        if (AndroidDriver.FindElements(ByAutoId("ScanSetupQrPageTitle")).Count == 0)
        {
            AndroidDriver.FindElement(ByAutoId("LoginPageButtonScanQr")).Click();
            wait.Until(d => d.FindElements(ByAutoId("ScanSetupQrPageTitle")).Count > 0);
        }

        var qrPayload = $$"""{"type":"openksef-setup","version":1,"serverUrl":"{{serverUrl.TrimEnd('/')}}","setupToken":"stale-token"}""";
        var qrImagePath = GenerateQrCodeImage(qrPayload);
        var devicePath = "/sdcard/Download/openksef-setup-qr.png";

        try
        {
            AndroidDriver.PushFile(devicePath, File.ReadAllBytes(qrImagePath));
            TestContext.Progress.WriteLine($"QR image pushed to device: {devicePath}");
        }
        catch (Exception ex)
        {
            Assert.Ignore($"Could not push QR image to device: {ex.Message}");
            return;
        }

        try
        {
            AndroidDriver.ExecuteScript("mobile: shell", new Dictionary<string, object>
            {
                ["command"] = "am",
                ["args"] = new[] { "broadcast", "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE", "-d", $"file://{devicePath}" }
            });
        }
        catch
        {
            // Best-effort media scan
        }

        AndroidDriver.FindElement(ByAutoId("ScanSetupQrButtonGallery")).Click();

        try
        {
            try
            {
                new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(15)).Until(d =>
                    d.FindElements(By.XPath("//*[contains(@text, 'Download')]")).Count > 0);
            }
            catch (WebDriverTimeoutException)
            {
                Assert.Ignore("Android file picker did not behave as expected. This test requires a standard file picker.");
                return;
            }

            AndroidDriver.FindElements(By.XPath("//*[contains(@text, 'Download')]")).FirstOrDefault()?.Click();

            Thread.Sleep(1000);

            var qrFile = AndroidDriver.FindElements(By.XPath("//*[contains(@text, 'openksef-setup-qr')]"))
                .FirstOrDefault();

            if (qrFile is null)
            {
                Assert.Ignore("Could not locate QR image in Android file picker. File picker UI varies by device.");
                return;
            }

            qrFile.Click();

            var switchButton = By.XPath("//*[@text='Przełącz' or @text='PRZEŁĄCZ']");
            wait.Until(d =>
                d.FindElements(switchButton).Count > 0 ||
                d.FindElements(ByAutoId("ScanSetupQrPageTitle")).Count == 0);
            AndroidDriver.FindElements(switchButton).FirstOrDefault()?.Click();

            wait.Until(d =>
                d.FindElements(ByAutoId("ScanSetupQrPageTitle")).Count == 0 &&
                d.FindElements(ByAutoId("LoginPageEntryEmail")).Count > 0);

            Assert.That(AndroidDriver.FindElement(ByAutoId("LoginPageEntryEmail")).Displayed, Is.True,
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

        // Write a minimal BMP/PNG from the bit matrix
        WriteBitMatrixAsPng(matrix, tempPath);

        return tempPath;
    }

    private static void WriteBitMatrixAsPng(ZXing.Common.BitMatrix matrix, string path)
    {
        var width = matrix.Width;
        var height = matrix.Height;

        // Write as BMP (simpler than PNG, but Android can read it)
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        var rowBytes = ((width * 3 + 3) / 4) * 4;
        var imageSize = rowBytes * height;
        var fileSize = 54 + imageSize;

        // BMP header
        bw.Write((byte)'B'); bw.Write((byte)'M');
        bw.Write(fileSize);
        bw.Write(0);
        bw.Write(54);

        // DIB header
        bw.Write(40);
        bw.Write(width);
        bw.Write(height);
        bw.Write((short)1);
        bw.Write((short)24);
        bw.Write(0);
        bw.Write(imageSize);
        bw.Write(2835); bw.Write(2835);
        bw.Write(0); bw.Write(0);

        // Pixel data (bottom-up)
        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                byte color = matrix[x, y] ? (byte)0 : (byte)255;
                bw.Write(color); bw.Write(color); bw.Write(color);
            }
            // Padding
            for (int p = width * 3; p < rowBytes; p++)
                bw.Write((byte)0);
        }
    }
}
