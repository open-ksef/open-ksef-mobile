using OpenKSeF.Mobile.E2E.Android.Configuration;
using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenQA.Selenium;

namespace OpenKSeF.Mobile.E2E.Android.InfrastructureTests;

[Category("DeviceFree")]
public class AndroidDriverFactoryTests
{
    [Test]
    public void BuildAppiumOptions_IncludesRequiredCapabilities()
    {
        var apk = Path.GetTempFileName();
        try
        {
            var capabilities = AndroidDriverFactory.BuildAppiumOptions(CreateOptions(apk)).ToCapabilities();

            Assert.That(ReadCapability(capabilities, "platformName"), Is.EqualTo("Android"));
            Assert.That(ReadCapability(capabilities, "automationName"), Is.EqualTo("UiAutomator2"));
            Assert.That(ReadCapability(capabilities, "deviceName"), Is.EqualTo("Pixel_8_API_35"));
            Assert.That(ReadCapability(capabilities, "app"), Is.EqualTo(apk));
            Assert.That(ReadCapability(capabilities, "appPackage"), Is.EqualTo("com.openksef.mobile"));
            Assert.That(ReadCapability(capabilities, "appActivity"), Is.EqualTo(".MainActivity"));
        }
        finally
        {
            File.Delete(apk);
        }
    }

    [Test]
    public void BuildAppiumOptions_OmitsAppWhenBinaryMissing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.apk");

        var capabilities = AndroidDriverFactory.BuildAppiumOptions(CreateOptions(missing)).ToCapabilities();

        Assert.That(ReadCapability(capabilities, "app"), Is.Null);
        Assert.That(ReadCapability(capabilities, "appPackage"), Is.EqualTo("com.openksef.mobile"));
    }

    private static AndroidE2ETestOptions CreateOptions(string appPath)
    {
        return new AndroidE2ETestOptions
        {
            AppiumServerUrl = "http://127.0.0.1:4723/",
            PlatformName = "Android",
            AutomationName = "UiAutomator2",
            DeviceName = "Pixel_8_API_35",
            AppPath = appPath,
            AppPackage = "com.openksef.mobile",
            AppActivity = ".MainActivity",
            CommandTimeoutSeconds = 300
        };
    }

    private static object? ReadCapability(ICapabilities capabilities, string key)
    {
        return capabilities.GetCapability($"appium:{key}") ?? capabilities.GetCapability(key);
    }
}
