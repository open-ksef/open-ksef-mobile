using OpenKSeF.Mobile.E2E.Android.Configuration;
using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenQA.Selenium;

namespace OpenKSeF.Mobile.E2E.Android.InfrastructureTests;

public class AndroidDriverFactoryTests
{
    [Test]
    public void BuildAppiumOptions_IncludesRequiredCapabilities()
    {
        var options = new AndroidE2ETestOptions
        {
            AppiumServerUrl = "http://127.0.0.1:4723/",
            PlatformName = "Android",
            AutomationName = "UiAutomator2",
            DeviceName = "Pixel_8_API_35",
            AppPath = "/tmp/openksef.apk",
            AppPackage = "com.openksef.mobile",
            AppActivity = ".MainActivity",
            CommandTimeoutSeconds = 300
        };

        var appiumOptions = AndroidDriverFactory.BuildAppiumOptions(options);
        var capabilities = appiumOptions.ToCapabilities();

        Assert.That(ReadCapability(capabilities, "platformName"), Is.EqualTo("Android"));
        Assert.That(ReadCapability(capabilities, "automationName"), Is.EqualTo("UiAutomator2"));
        Assert.That(ReadCapability(capabilities, "deviceName"), Is.EqualTo("Pixel_8_API_35"));
        Assert.That(ReadCapability(capabilities, "app"), Is.EqualTo("/tmp/openksef.apk"));
        Assert.That(ReadCapability(capabilities, "appPackage"), Is.EqualTo("com.openksef.mobile"));
        Assert.That(ReadCapability(capabilities, "appActivity"), Is.EqualTo(".MainActivity"));
    }

    private static object? ReadCapability(ICapabilities capabilities, string key)
    {
        return capabilities.GetCapability($"appium:{key}") ?? capabilities.GetCapability(key);
    }
}
