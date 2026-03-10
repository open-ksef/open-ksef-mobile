using OpenKSeF.Mobile.E2E.Shared.Configuration;

namespace OpenKSeF.Mobile.E2E.Shared;

public class MobileTestConfigurationTests
{
    [Test]
    public void Load_FromEnvironmentVariables_UsesExpectedValues()
    {
        Environment.SetEnvironmentVariable("APPIUM_SERVER_URL", "http://localhost:4723/");
        Environment.SetEnvironmentVariable("APP_PATH", "/tmp/app.apk");
        Environment.SetEnvironmentVariable("PLATFORM_NAME", "Android");
        Environment.SetEnvironmentVariable("DEVICE_NAME", "Pixel_7");

        try
        {
            var config = MobileTestConfiguration.Load();

            Assert.That(config.AppiumServerUrl, Is.EqualTo("http://localhost:4723/"));
            Assert.That(config.AppPath, Is.EqualTo("/tmp/app.apk"));
            Assert.That(config.PlatformName, Is.EqualTo("Android"));
            Assert.That(config.DeviceName, Is.EqualTo("Pixel_7"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("APPIUM_SERVER_URL", null);
            Environment.SetEnvironmentVariable("APP_PATH", null);
            Environment.SetEnvironmentVariable("PLATFORM_NAME", null);
            Environment.SetEnvironmentVariable("DEVICE_NAME", null);
        }
    }

    [Test]
    public void Load_WithoutEnvironmentVariables_UsesDefaults()
    {
        var config = MobileTestConfiguration.Load();

        Assert.That(config.AppiumServerUrl, Is.EqualTo("http://127.0.0.1:4723/"));
        Assert.That(config.PlatformName, Is.EqualTo("Android"));
        Assert.That(config.DeviceName, Is.Not.Empty);
    }
}
