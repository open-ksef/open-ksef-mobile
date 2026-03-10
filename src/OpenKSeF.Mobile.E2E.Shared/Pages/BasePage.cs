using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium.Appium;

namespace OpenKSeF.Mobile.E2E.Shared.Pages;

public abstract class BasePage(AppiumDriver driver)
{
    protected AppiumDriver Driver { get; } = driver;
    protected WaitHelper Wait { get; } = new(driver);
}
