using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Support.UI;

namespace OpenKSeF.Mobile.E2E.Android.Support;

public sealed class AndroidOnboardingFlow(AndroidDriver driver)
{
    private readonly AndroidDriver _driver = driver;

    public void WaitForOnboardingPage(TimeSpan? timeout = null)
    {
        var wait = new WaitHelper(_driver, timeout ?? TimeSpan.FromSeconds(30));
        wait.UntilVisible(ByAutoId("OnboardingStepIndicator"));
    }

    public void FillCompanyData(string nip, string displayName, string email)
    {
        var wait = new WaitHelper(_driver, TimeSpan.FromSeconds(15));

        var nipEntry = wait.UntilVisible(ByAutoId("OnboardingEntryNip"));
        nipEntry.Clear();
        nipEntry.SendKeys(nip);

        var nameEntry = _driver.FindElement(ByAutoId("OnboardingEntryDisplayName"));
        nameEntry.Clear();
        nameEntry.SendKeys(displayName);

        var emailEntry = _driver.FindElement(ByAutoId("OnboardingEntryNotificationEmail"));
        emailEntry.Clear();
        emailEntry.SendKeys(email);

        _driver.FindElement(ByAutoId("OnboardingButtonNext")).Click();
    }

    public void FillKsefToken(string token)
    {
        var wait = new WaitHelper(_driver, TimeSpan.FromSeconds(15));
        var tokenEntry = wait.UntilVisible(ByAutoId("OnboardingEntryKsefToken"));
        tokenEntry.Clear();
        tokenEntry.SendKeys(token);
        _driver.FindElement(ByAutoId("OnboardingButtonNext")).Click();
    }

    public void SkipKsefToken()
    {
        var wait = new WaitHelper(_driver, TimeSpan.FromSeconds(15));
        var skipButton = wait.UntilVisible(ByAutoId("OnboardingButtonSkipToken"));
        skipButton.Click();
    }

    public void WaitForSuccessStep(TimeSpan? timeout = null)
    {
        var wait = new WaitHelper(_driver, timeout ?? TimeSpan.FromSeconds(30));
        wait.UntilVisible(ByAutoId("OnboardingLabelSuccess"));
    }

    public void FinishOnboarding()
    {
        var wait = new WaitHelper(_driver, TimeSpan.FromSeconds(10));
        var finishButton = wait.UntilVisible(ByAutoId("OnboardingButtonFinish"));
        finishButton.Click();
    }

    public void WaitForMainApp(TimeSpan? timeout = null)
    {
        var wait = new WebDriverWait(_driver, timeout ?? TimeSpan.FromSeconds(30));
        wait.Until(d =>
            d.FindElements(ByAutoId("InvoiceListPageLabelTitle")).Count > 0 ||
            d.FindElements(ByAutoId("InvoiceListPageCollectionViewInvoices")).Count > 0);
    }

    public bool IsStepIndicatorVisible()
    {
        return _driver.FindElements(ByAutoId("OnboardingStepIndicator")).Count > 0;
    }

    public bool IsNipEntryVisible()
    {
        return _driver.FindElements(ByAutoId("OnboardingEntryNip")).Count > 0;
    }

    public bool IsErrorVisible()
    {
        return _driver.FindElements(By.XPath("//*[contains(@text, 'NIP')]")).Count > 0;
    }
}
