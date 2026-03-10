using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace OpenKSeF.Mobile.E2E.Shared.Infrastructure;

public sealed class WaitHelper(IWebDriver driver, TimeSpan? timeout = null)
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(20);

    public IWebElement UntilVisible(By by)
    {
        var wait = new WebDriverWait(driver, _timeout);
        return wait.Until(drv =>
        {
            var element = drv.FindElement(by);
            return element.Displayed ? element : null;
        });
    }
}
