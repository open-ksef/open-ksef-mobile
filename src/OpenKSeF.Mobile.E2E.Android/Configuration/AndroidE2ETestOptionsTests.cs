using OpenKSeF.Mobile.E2E.Android.Configuration;
using OpenKSeF.Mobile.E2E.Shared.Configuration;

namespace OpenKSeF.Mobile.E2E.Android.ConfigurationTests;

public class AndroidE2ETestOptionsTests
{
    [Test]
    public void FromMobileConfig_UsesAndroidEnvironmentOverrides()
    {
        Environment.SetEnvironmentVariable("ANDROID_APP_PATH", "/tmp/openksef-android.apk");
        Environment.SetEnvironmentVariable("ANDROID_DEVICE_NAME", "Pixel_8_API_35");
        Environment.SetEnvironmentVariable("ANDROID_APP_PACKAGE", "com.openksef.mobile");
        Environment.SetEnvironmentVariable("ANDROID_APP_ACTIVITY", ".MainActivity");

        try
        {
            var baseConfig = new MobileTestConfiguration
            {
                AppiumServerUrl = "http://127.0.0.1:4723/",
                AppPath = "/tmp/fallback.apk",
                PlatformName = "iOS",
                DeviceName = "Simulator",
                CommandTimeoutSeconds = 180
            };

            var options = AndroidE2ETestOptions.From(baseConfig);

            Assert.That(options.PlatformName, Is.EqualTo("Android"));
            Assert.That(options.AutomationName, Is.EqualTo("UiAutomator2"));
            Assert.That(options.DeviceName, Is.EqualTo("Pixel_8_API_35"));
            Assert.That(options.AppPath, Is.EqualTo("/tmp/openksef-android.apk"));
            Assert.That(options.AppPackage, Is.EqualTo("com.openksef.mobile"));
            Assert.That(options.AppActivity, Is.EqualTo(".MainActivity"));
            Assert.That(options.CommandTimeoutSeconds, Is.EqualTo(300));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANDROID_APP_PATH", null);
            Environment.SetEnvironmentVariable("ANDROID_DEVICE_NAME", null);
            Environment.SetEnvironmentVariable("ANDROID_APP_PACKAGE", null);
            Environment.SetEnvironmentVariable("ANDROID_APP_ACTIVITY", null);
        }
    }

    [Test]
    public void FromMobileConfig_UsesSharedAppPathWhenAndroidPathNotProvided()
    {
        var baseConfig = new MobileTestConfiguration
        {
            AppiumServerUrl = "http://127.0.0.1:4723/",
            AppPath = "/tmp/shared.apk",
            PlatformName = "Android",
            DeviceName = "Android Emulator",
            CommandTimeoutSeconds = 180
        };

        var options = AndroidE2ETestOptions.From(baseConfig);

        Assert.That(options.AppPath, Is.EqualTo("/tmp/shared.apk"));
    }
}
