using OpenKSeF.Mobile.E2E.Shared.Configuration;
using OpenQA.Selenium.Appium;

namespace OpenKSeF.Mobile.E2E.Shared.Infrastructure;

[NonParallelizable]
public abstract class BaseMobileTest
{
    protected MobileTestConfiguration Config { get; private set; } = null!;
    protected AppiumDriver Driver { get; private set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Config = MobileTestConfiguration.Load();
        Driver = CreateDriver(Config);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        Driver?.Quit();
        Driver?.Dispose();
    }

    protected abstract AppiumDriver CreateDriver(MobileTestConfiguration configuration);
}
