using OpenKSeF.Mobile.E2E.Android.Configuration;
using OpenKSeF.Mobile.E2E.Shared.Configuration;
using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using NUnit.Framework.Interfaces;

namespace OpenKSeF.Mobile.E2E.Android.Infrastructure;

[NonParallelizable]
[Timeout(300_000)]
public abstract class AndroidTestBase : BaseMobileTest
{
    protected AndroidE2ETestOptions AndroidOptions { get; private set; } = null!;
    protected AndroidDriver AndroidDriver => (AndroidDriver)Driver;

    protected override AppiumDriver CreateDriver(MobileTestConfiguration configuration)
    {
        AndroidOptions = AndroidE2ETestOptions.From(configuration);
        return AndroidDriverFactory.CreateDriver(AndroidOptions);
    }

    protected void InstallApp(string appPath)
    {
        AndroidDriver.InstallApp(appPath);
    }

    protected void UninstallApp(string appPackage)
    {
        AndroidDriver.RemoveApp(appPackage);
    }

    protected void ResetApp()
    {
        AndroidDriver.TerminateApp(AndroidOptions.AppPackage);
        AndroidDriver.ActivateApp(AndroidOptions.AppPackage);
    }

    [TearDown]
    public void CaptureScreenshotOnFailure()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status != TestStatus.Failed)
        {
            return;
        }

        var screenshotDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "TestResults",
            "Screenshots");

        var testName = TestContext.CurrentContext.Test.Name.Replace(' ', '_');
        var screenshotPath = ScreenshotHelper.Capture(AndroidDriver, screenshotDirectory, testName);
        TestContext.Progress.WriteLine($"Screenshot captured: {screenshotPath}");
    }
}
