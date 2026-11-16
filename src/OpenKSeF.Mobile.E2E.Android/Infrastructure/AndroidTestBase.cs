using OpenKSeF.Mobile.E2E.Android.Configuration;
using OpenKSeF.Mobile.E2E.Android.Support;
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

    protected virtual bool AutoGrantPermissions => true;

    protected virtual bool UsesSeededUser => false;

    protected E2EBackend.User SeededUser { get; private set; } = null!;

    protected override AppiumDriver CreateDriver(MobileTestConfiguration configuration)
    {
        AndroidOptions = AndroidE2ETestOptions.From(configuration);
        return AndroidDriverFactory.CreateDriver(AndroidOptions, AutoGrantPermissions);
    }

    [OneTimeSetUp]
    public async Task CreateSeededUser()
    {
        if (UsesSeededUser)
            SeededUser = await E2EBackend.CreateSeededUserAsync();
    }

    protected void LoginAsSeededUser(bool? pushConsent = false) =>
        new AndroidOidcLoginFlow(AndroidDriver).Login(SeededUser.Username, SeededUser.Password, pushConsent);

    [SetUp]
    public void SignOutBeforeTest() => new AndroidOidcLoginFlow(AndroidDriver).SignOut();

    [OneTimeTearDown]
    public async Task SignOutAndAssertNoDeviceRows()
    {
        new AndroidOidcLoginFlow(AndroidDriver).SignOut();

        if (UsesSeededUser && SeededUser is not null)
        {
            try
            {
                Assert.That(await E2EBackend.WaitForNoDevicesAsync(SeededUser, TimeSpan.FromSeconds(30)), Is.Zero,
                    $"Device token rows left for {SeededUser.Username} after sign-out.");
            }
            finally
            {
                await E2EBackend.DeleteUserAsync(SeededUser);
            }
        }

        var user = E2EBackend.EnvironmentUser();
        if (user is null)
            return;

        var left = await E2EBackend.WaitForNoDevicesAsync(user, TimeSpan.FromSeconds(30));
        Assert.That(left, Is.Zero, $"Device token rows left for {user.Username} after sign-out.");
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
