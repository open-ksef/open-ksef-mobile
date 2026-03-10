using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Support.UI;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("Regression")]
public sealed class LayoutVerificationTests : AndroidTestBase
{
    private string ScreenshotDir =>
        Path.Combine(TestContext.CurrentContext.WorkDirectory, "TestResults", "LayoutScreenshots");

    private void CaptureLayoutScreenshot(string name)
    {
        ScreenshotHelper.Capture(AndroidDriver, ScreenshotDir, $"Layout_{name}");
        TestContext.Progress.WriteLine($"Layout screenshot captured: {name}");
    }

    [Test]
    [Order(1)]
    public void LoginPage_LayoutElementsAreVisible()
    {
        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(30));

        var logo = wait.UntilVisible(MobileBy.AccessibilityId("LoginPageLabelLogo"));
        Assert.That(logo.Displayed, Is.True, "Logo should be visible on login page");

        var title = AndroidDriver.FindElement(MobileBy.AccessibilityId("LoginPageLabelTitle"));
        Assert.That(title.Displayed, Is.True, "Title 'OpenKSeF' should be visible");
        Assert.That(title.Text, Is.EqualTo("OpenKSeF"), "Title text should be 'OpenKSeF'");

        var emailEntry = AndroidDriver.FindElement(MobileBy.AccessibilityId("LoginPageEntryEmail"));
        Assert.That(emailEntry.Displayed, Is.True, "Email entry should be visible");

        var passwordEntry = AndroidDriver.FindElement(MobileBy.AccessibilityId("LoginPageEntryPassword"));
        Assert.That(passwordEntry.Displayed, Is.True, "Password entry should be visible");

        var loginButton = AndroidDriver.FindElement(MobileBy.AccessibilityId("LoginPageButtonLogin"));
        Assert.That(loginButton.Displayed, Is.True, "Login button should be visible");

        var googleButton = AndroidDriver.FindElement(MobileBy.AccessibilityId("LoginPageButtonGoogle"));
        Assert.That(googleButton.Displayed, Is.True, "Google button should be visible");

        CaptureLayoutScreenshot("LoginPage_LoginMode");
    }

    [Test]
    [Order(2)]
    public void LoginPage_RegisterModeLayoutIsCorrect()
    {
        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(15));

        wait.UntilVisible(MobileBy.AccessibilityId("LoginPageLabelLogo"));

        var switchLink = AndroidDriver.FindElement(MobileBy.AccessibilityId("LoginPageSwitchToRegister"));
        switchLink.Click();

        Thread.Sleep(500);

        var firstNameEntry = AndroidDriver.FindElements(MobileBy.AccessibilityId("LoginPageEntryRegFirstName"));
        Assert.That(firstNameEntry.Count, Is.GreaterThan(0), "First name entry should be visible in register mode");

        var lastNameEntry = AndroidDriver.FindElements(MobileBy.AccessibilityId("LoginPageEntryRegLastName"));
        Assert.That(lastNameEntry.Count, Is.GreaterThan(0), "Last name entry should be visible in register mode");

        var regEmailEntry = AndroidDriver.FindElements(MobileBy.AccessibilityId("LoginPageEntryRegEmail"));
        Assert.That(regEmailEntry.Count, Is.GreaterThan(0), "Email entry should be visible in register mode");

        var regButton = AndroidDriver.FindElements(MobileBy.AccessibilityId("LoginPageButtonRegister"));
        Assert.That(regButton.Count, Is.GreaterThan(0), "Register button should be visible");

        CaptureLayoutScreenshot("LoginPage_RegisterMode");

        var switchBack = AndroidDriver.FindElement(MobileBy.AccessibilityId("LoginPageSwitchToLogin"));
        switchBack.Click();
        Thread.Sleep(300);
    }

    [Test]
    [Order(3)]
    public void OnboardingPage_Step1LayoutElementsAreVisible()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        authFlow.LoginWithKeycloakFromEnvironment();

        var onboarding = new AndroidOnboardingFlow(AndroidDriver);
        onboarding.WaitForOnboardingPage();

        var stepIndicator = AndroidDriver.FindElement(MobileBy.AccessibilityId("OnboardingStepIndicator"));
        Assert.That(stepIndicator.Displayed, Is.True, "Step indicator should be visible on onboarding");

        var nipEntry = AndroidDriver.FindElement(MobileBy.AccessibilityId("OnboardingEntryNip"));
        Assert.That(nipEntry.Displayed, Is.True, "NIP entry should be visible on step 1");

        var nameEntry = AndroidDriver.FindElement(MobileBy.AccessibilityId("OnboardingEntryDisplayName"));
        Assert.That(nameEntry.Displayed, Is.True, "Display name entry should be visible on step 1");

        var emailEntry = AndroidDriver.FindElement(MobileBy.AccessibilityId("OnboardingEntryNotificationEmail"));
        Assert.That(emailEntry.Displayed, Is.True, "Notification email entry should be visible on step 1");

        var nextButton = AndroidDriver.FindElement(MobileBy.AccessibilityId("OnboardingButtonNext"));
        Assert.That(nextButton.Displayed, Is.True, "Next button should be visible on step 1");

        CaptureLayoutScreenshot("OnboardingPage_Step1");
    }

    [Test]
    [Order(4)]
    public void OnboardingPage_Step2LayoutElementsAreVisible()
    {
        var onboarding = new AndroidOnboardingFlow(AndroidDriver);

        if (!onboarding.IsStepIndicatorVisible())
        {
            Assert.Ignore("Not on onboarding page -- previous test may have failed.");
        }

        onboarding.FillCompanyData(
            nip: "7777777777",
            displayName: "Layout Test Co",
            email: "layout@test.open-ksef.pl");

        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(15));
        var tokenEntry = wait.UntilVisible(MobileBy.AccessibilityId("OnboardingEntryKsefToken"));
        Assert.That(tokenEntry.Displayed, Is.True, "Token editor should be visible on step 2");

        var skipLink = AndroidDriver.FindElement(MobileBy.AccessibilityId("OnboardingButtonSkipToken"));
        Assert.That(skipLink.Displayed, Is.True, "Skip link should be visible on step 2");

        CaptureLayoutScreenshot("OnboardingPage_Step2_KsefToken");
    }

    [Test]
    [Order(5)]
    public void OnboardingPage_Step3SuccessLayoutElementsAreVisible()
    {
        var onboarding = new AndroidOnboardingFlow(AndroidDriver);
        onboarding.SkipKsefToken();
        onboarding.WaitForSuccessStep();

        var successLabel = AndroidDriver.FindElement(MobileBy.AccessibilityId("OnboardingLabelSuccess"));
        Assert.That(successLabel.Displayed, Is.True, "Success checkmark should be visible on step 3");

        CaptureLayoutScreenshot("OnboardingPage_Step3_Success");

        onboarding.FinishOnboarding();
    }

    [Test]
    [Order(6)]
    public void InvoiceListPage_LayoutElementsAreVisible()
    {
        var wait = new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(60));
        wait.Until(d =>
            d.FindElements(MobileBy.AccessibilityId("InvoiceListPageLabelTitle")).Count > 0);

        var titleLabel = AndroidDriver.FindElement(MobileBy.AccessibilityId("InvoiceListPageLabelTitle"));
        Assert.That(titleLabel.Displayed, Is.True, "Invoice list title should be visible");
        Assert.That(titleLabel.Text, Does.Contain("Faktury"), "Title should contain 'Faktury'");

        CaptureLayoutScreenshot("InvoiceListPage");
    }

    [Test]
    [Order(7)]
    public void TenantsPage_LayoutElementsAreVisible()
    {
        var tenantsTab = AndroidDriver.FindElements(By.XPath("//*[contains(@text, 'Firmy')]"));
        if (tenantsTab.Count > 0)
        {
            tenantsTab[0].Click();
            Thread.Sleep(1000);
        }
        else
        {
            Assert.Ignore("Cannot navigate to Tenants tab.");
        }

        var tenantList = AndroidDriver.FindElements(MobileBy.AccessibilityId("TenantsPageCollectionViewTenants"));
        var addButton = AndroidDriver.FindElements(MobileBy.AccessibilityId("TenantsPageButtonAdd"));

        Assert.That(
            tenantList.Count > 0 || addButton.Count > 0,
            Is.True,
            "Tenants page should show either a tenant list or add button");

        CaptureLayoutScreenshot("TenantsPage");
    }

    [Test]
    [Order(8)]
    public void TenantsPage_TenantFormLayoutIsCorrect()
    {
        var addButton = AndroidDriver.FindElements(MobileBy.AccessibilityId("TenantsPageButtonAdd"));
        if (addButton.Count == 0)
        {
            addButton = AndroidDriver.FindElements(MobileBy.AccessibilityId("TenantsPageButtonAddEmptyState"));
        }

        if (addButton.Count == 0)
        {
            Assert.Ignore("Cannot find add tenant button.");
        }

        addButton[0].Click();
        Thread.Sleep(1000);

        var nipEntry = AndroidDriver.FindElements(MobileBy.AccessibilityId("TenantFormPageEntryNip"));
        Assert.That(nipEntry.Count, Is.GreaterThan(0), "NIP entry should be visible on tenant form");

        var nameEntry = AndroidDriver.FindElements(MobileBy.AccessibilityId("TenantFormPageEntryDisplayName"));
        Assert.That(nameEntry.Count, Is.GreaterThan(0), "Display name entry should be visible on tenant form");

        var saveButton = AndroidDriver.FindElements(MobileBy.AccessibilityId("TenantFormPageButtonSave"));
        Assert.That(saveButton.Count, Is.GreaterThan(0), "Save button should be visible on tenant form");

        var cancelButton = AndroidDriver.FindElements(MobileBy.AccessibilityId("TenantFormPageButtonCancel"));
        Assert.That(cancelButton.Count, Is.GreaterThan(0), "Cancel button should be visible on tenant form");

        CaptureLayoutScreenshot("TenantFormPage");

        cancelButton[0].Click();
        Thread.Sleep(500);
    }
}
