using OpenKSeF.Mobile.E2E.Android.Configuration;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace OpenKSeF.Mobile.E2E.Android.Infrastructure;

public static class AndroidDriverFactory
{
    public static AndroidDriver CreateDriver(AndroidE2ETestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureAppBinaryExists(options.AppPath);

        var appiumOptions = BuildAppiumOptions(options);
        return new AndroidDriver(
            new Uri(options.AppiumServerUrl),
            appiumOptions,
            TimeSpan.FromSeconds(options.CommandTimeoutSeconds));
    }

    public static AppiumOptions BuildAppiumOptions(AndroidE2ETestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var appiumOptions = new AppiumOptions
        {
            PlatformName = options.PlatformName,
            AutomationName = options.AutomationName,
            DeviceName = options.DeviceName,
            App = options.AppPath
        };

        appiumOptions.AddAdditionalAppiumOption("appPackage", options.AppPackage);
        appiumOptions.AddAdditionalAppiumOption("appActivity", options.AppActivity);
        return appiumOptions;
    }

    private static void EnsureAppBinaryExists(string appPath)
    {
        if (File.Exists(appPath))
        {
            return;
        }

        throw new FileNotFoundException(
            $"Android app binary was not found at '{appPath}'. Build the MAUI app or set ANDROID_APP_PATH to a valid .apk/.aab file.",
            appPath);
    }
}
