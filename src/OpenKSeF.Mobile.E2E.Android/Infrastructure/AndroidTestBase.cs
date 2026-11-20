using OpenKSeF.Mobile.E2E.Android.Configuration;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenKSeF.Mobile.E2E.Shared.Configuration;
using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium;
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
        new AndroidOidcLoginFlow(AndroidDriver).ClearStaleServerUrl();
        if (UsesSeededUser)
            SeededUser = await E2EBackend.CreateSeededUserAsync();
    }

    protected void LoginAsSeededUser(bool? pushConsent = false) =>
        new AndroidOidcLoginFlow(AndroidDriver).Login(SeededUser.Username, SeededUser.Password, pushConsent);

    [SetUp]
    public async Task ResetAppBeforeTest()
    {
        var flow = new AndroidOidcLoginFlow(AndroidDriver);
        if (AutoGrantPermissions)
            flow.Grant("android.permission.POST_NOTIFICATIONS");

        try
        {
            flow.SignOut();
        }
        catch (WebDriverException ex)
        {
            TestContext.Progress.WriteLine($"UI sign-out failed, clearing app state: {ex.Message}");
            flow.ClearAppState();
            foreach (var user in new[] { UsesSeededUser ? SeededUser : null, E2EBackend.EnvironmentUser() }.OfType<E2EBackend.User>())
                await E2EBackend.EndKeycloakSessionsAsync(user);
        }
    }

    [OneTimeTearDown]
    public async Task SignOutAndAssertNoDeviceRows()
    {
        try
        {
            new AndroidOidcLoginFlow(AndroidDriver).SignOut();

            if (UsesSeededUser && SeededUser is not null)
                Assert.That(await E2EBackend.WaitForNoDevicesAsync(SeededUser, TimeSpan.FromSeconds(30)), Is.Zero,
                    $"Device token rows left for {SeededUser.Username} after sign-out.");

            var user = E2EBackend.EnvironmentUser();
            if (user is not null)
                Assert.That(await E2EBackend.WaitForNoDevicesAsync(user, TimeSpan.FromSeconds(30)), Is.Zero,
                    $"Device token rows left for {user.Username} after sign-out.");
        }
        finally
        {
            if (UsesSeededUser && SeededUser is not null)
                await E2EBackend.DeleteUserAsync(SeededUser);
        }
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
