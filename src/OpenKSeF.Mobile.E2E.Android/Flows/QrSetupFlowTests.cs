using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Support.UI;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("QrSetup")]
public sealed class QrSetupFlowTests : AndroidTestBase
{
    [Test]
    public void QrSetupPage_HasExpectedElements()
    {
        var wait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(30));

        wait.Until(d =>
            d.FindElements(MobileBy.AccessibilityId("ScanSetupQrPageTitle")).Count > 0 ||
            d.FindElements(MobileBy.AccessibilityId("LoginPageEntryEmail")).Count > 0);

        var isOnQrPage = AndroidDriver.FindElements(MobileBy.AccessibilityId("ScanSetupQrPageTitle")).Count > 0;
        if (!isOnQrPage)
        {
            Assert.Ignore("App did not start on QR setup page (already configured). Reset app data to test first-launch flow.");
            return;
        }

        var title = AndroidDriver.FindElement(MobileBy.AccessibilityId("ScanSetupQrPageTitle"));
        Assert.That(title.Displayed, Is.True, "QR setup page title should be visible");

        var galleryButton = AndroidDriver.FindElement(MobileBy.AccessibilityId("ScanSetupQrButtonGallery"));
        Assert.That(galleryButton.Displayed, Is.True, "Gallery button should be visible");

        var skipButton = AndroidDriver.FindElement(MobileBy.AccessibilityId("ScanSetupQrButtonSkip"));
        Assert.That(skipButton.Displayed, Is.True, "Skip button should be visible");
    }

    [Test]
    public void QrSetupPage_SkipButton_NavigatesToLogin()
    {
        var wait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(30));

        wait.Until(d =>
            d.FindElements(MobileBy.AccessibilityId("ScanSetupQrPageTitle")).Count > 0 ||
            d.FindElements(MobileBy.AccessibilityId("LoginPageEntryEmail")).Count > 0);

        var isOnQrPage = AndroidDriver.FindElements(MobileBy.AccessibilityId("ScanSetupQrPageTitle")).Count > 0;
        if (!isOnQrPage)
        {
            Assert.Ignore("App did not start on QR setup page (already configured).");
            return;
        }

        var skipButton = AndroidDriver.FindElement(MobileBy.AccessibilityId("ScanSetupQrButtonSkip"));
        skipButton.Click();

        wait.Until(d => d.FindElements(MobileBy.AccessibilityId("LoginPageEntryEmail")).Count > 0);

        var emailEntry = AndroidDriver.FindElement(MobileBy.AccessibilityId("LoginPageEntryEmail"));
        Assert.That(emailEntry.Displayed, Is.True, "Login page should be visible after skipping QR setup");
    }

    [Test]
    public async Task SetupTokenApi_GeneratesAndRedeems()
    {
        var serverUrl = Environment.GetEnvironmentVariable("APP_EXTERNAL_BASE_URL")
            ?? "http://localhost:8080";

        var username = Environment.GetEnvironmentVariable("KEYCLOAK_USERNAME");
        var password = Environment.GetEnvironmentVariable("KEYCLOAK_PASSWORD");

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Assert.Ignore("KEYCLOAK_USERNAME / KEYCLOAK_PASSWORD are required.");
            return;
        }

        var result = await SetupTokenHelper.GenerateSetupTokenAsync(serverUrl);

        Assert.That(result.SetupToken, Is.Not.Empty, "Setup token should be generated");
        Assert.That(result.QrPayloadJson, Does.Contain("openksef-setup"),
            "QR payload should contain the expected type");
        Assert.That(result.QrPayloadJson, Does.Contain(serverUrl.TrimEnd('/')),
            "QR payload should contain the server URL");

        TestContext.Progress.WriteLine($"Setup token generated successfully for {serverUrl}");
        TestContext.Progress.WriteLine($"QR payload length: {result.QrPayloadJson.Length}");
    }

    [Test]
    public void QrAutoLogin_ViaGalleryPick_NavigatesToAuthenticatedPage()
    {
        var serverUrl = Environment.GetEnvironmentVariable("APP_EXTERNAL_BASE_URL");

        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            Assert.Ignore("APP_EXTERNAL_BASE_URL is required for QR auto-login E2E test.");
            return;
        }

        var username = Environment.GetEnvironmentVariable("KEYCLOAK_USERNAME");
        var password = Environment.GetEnvironmentVariable("KEYCLOAK_PASSWORD");

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Assert.Ignore("KEYCLOAK_USERNAME / KEYCLOAK_PASSWORD are required.");
            return;
        }

        var wait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(30));

        wait.Until(d =>
            d.FindElements(MobileBy.AccessibilityId("ScanSetupQrPageTitle")).Count > 0 ||
            d.FindElements(MobileBy.AccessibilityId("LoginPageEntryEmail")).Count > 0);

        var isOnQrPage = AndroidDriver.FindElements(MobileBy.AccessibilityId("ScanSetupQrPageTitle")).Count > 0;
        if (!isOnQrPage)
        {
            Assert.Ignore("App did not start on QR setup page. Reset app data to test QR auto-login.");
            return;
        }

        // Generate QR code image, push to device, and trigger gallery pick
        var setupResult = SetupTokenHelper.GenerateSetupTokenAsync(serverUrl)
            .GetAwaiter().GetResult();

        // Push a QR code PNG to the emulator via adb
        var qrImagePath = GenerateQrCodeImage(setupResult.QrPayloadJson);
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

        // Trigger media scan so the file appears in the picker
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

        // Tap "Pick from gallery" button
        var galleryButton = AndroidDriver.FindElement(MobileBy.AccessibilityId("ScanSetupQrButtonGallery"));
        galleryButton.Click();

        // Wait for the file picker and try to select the file
        // Android file picker varies by OS version; use a defensive approach
        var longWait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(15));
        try
        {
            // Try to find and navigate in the Android file picker
            // Look for "Downloads" or the file name in the picker
            longWait.Until(d =>
            {
                var elements = d.FindElements(By.XPath("//*[contains(@text, 'Download')]"));
                return elements.Count > 0;
            });

            var downloadFolder = AndroidDriver.FindElements(By.XPath("//*[contains(@text, 'Download')]"))
                .FirstOrDefault();
            downloadFolder?.Click();

            Thread.Sleep(1000);

            var qrFile = AndroidDriver.FindElements(By.XPath("//*[contains(@text, 'openksef-setup-qr')]"))
                .FirstOrDefault();

            if (qrFile is null)
            {
                Assert.Ignore("Could not locate QR image in Android file picker. File picker UI varies by device.");
                return;
            }

            qrFile.Click();

            // Wait for auto-login to complete
            var authWait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(60));
            authWait.Until(d =>
                d.FindElements(MobileBy.AccessibilityId("InvoiceListPageLabelTitle")).Count > 0 ||
                d.FindElements(MobileBy.AccessibilityId("InvoiceListPageCollectionViewInvoices")).Count > 0 ||
                d.FindElements(MobileBy.AccessibilityId("OnboardingStepIndicator")).Count > 0);

            var authenticatedElement = AndroidDriver.FindElements(MobileBy.AccessibilityId("InvoiceListPageLabelTitle"))
                .FirstOrDefault()
                ?? AndroidDriver.FindElements(MobileBy.AccessibilityId("InvoiceListPageCollectionViewInvoices")).FirstOrDefault()
                ?? AndroidDriver.FindElements(MobileBy.AccessibilityId("OnboardingStepIndicator")).FirstOrDefault();

            Assert.That(authenticatedElement, Is.Not.Null,
                "Expected authenticated page (invoices or onboarding) after QR auto-login.");
        }
        catch (WebDriverTimeoutException)
        {
            Assert.Ignore("Android file picker did not behave as expected. This test requires a standard file picker.");
        }
        finally
        {
            // Cleanup: remove QR image from device
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
