using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenKSeF.Mobile.E2E.Shared.Infrastructure;
using OpenQA.Selenium.Support.UI;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("Regression")]
public sealed class LayoutVerificationTests : AndroidTestBase
{
    private readonly List<E2EBackend.User> _onboardingUsers = [];

    protected override bool UsesSeededUser => true;

    [OneTimeTearDown]
    public async Task SignOutAndDeleteOnboardingUsers()
    {
        new AndroidOidcLoginFlow(AndroidDriver).SignOut();
        try
        {
            foreach (var user in _onboardingUsers)
                Assert.That(await E2EBackend.WaitForNoDevicesAsync(user, TimeSpan.FromSeconds(30)), Is.Zero,
                    $"Device token rows left for {user.Username} after sign-out.");
        }
        finally
        {
            foreach (var user in _onboardingUsers)
                await E2EBackend.DeleteUserAsync(user);
        }
    }

    private string ScreenshotDir =>
        Path.Combine(TestContext.CurrentContext.WorkDirectory, "TestResults", "LayoutScreenshots");

    private void CaptureLayoutScreenshot(string name)
    {
        ScreenshotHelper.Capture(AndroidDriver, ScreenshotDir, $"Layout_{name}");
        TestContext.Progress.WriteLine($"Layout screenshot captured: {name}");
    }

    private async Task<AndroidOnboardingFlow> StartOnboardingAsync()
    {
        var user = await E2EBackend.CreateUserAsync();
        _onboardingUsers.Add(user);
        new AndroidOidcLoginFlow(AndroidDriver).Login(user.Username, user.Password);
        var onboarding = new AndroidOnboardingFlow(AndroidDriver);
        onboarding.WaitForOnboardingPage();
        return onboarding;
    }

    private void OpenSeededTenantList()
    {
        LoginAsSeededUser();
        new AndroidOidcLoginFlow(AndroidDriver).WaitForAuthenticatedInvoiceList(TimeSpan.FromSeconds(60));
        new AndroidTenantNavigationFlow(AndroidDriver).OpenTenantList();
    }

    [Test]
    public void LoginPage_LayoutElementsAreVisible()
    {
        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(30));

        var logo = wait.UntilVisible(ByAutoId("LoginPageLabelLogo"));
        Assert.That(logo.Displayed, Is.True, "Logo should be visible on login page");

        var title = AndroidDriver.FindElement(ByAutoId("LoginPageLabelTitle"));
        Assert.That(title.Displayed, Is.True, "Title 'OpenKSeF' should be visible");
        Assert.That(title.Text, Is.EqualTo("OpenKSeF"), "Title text should be 'OpenKSeF'");

        foreach (var id in new[] { "LoginPageButtonLogin", "LoginPageButtonPair", "LoginPageButtonGoogle", "LoginPageButtonChangeServer" })
            Assert.That(AndroidDriver.FindElement(ByAutoId(id)).Displayed, Is.True, $"{id} should be visible");

        Assert.That(AndroidDriver.FindElements(ByAutoId("LoginPageEntryPassword")), Is.Empty, "No password entry in the app");

        CaptureLayoutScreenshot("LoginPage_LoginMode");
    }

    [Test]
    public async Task LoginPage_RegisterButtonMatchesServerSetting()
    {
        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(15));
        wait.UntilVisible(ByAutoId("LoginPageLabelLogo"));

        var register = AndroidDriver.FindElements(ByAutoId("LoginPageButtonRegister"));
        if (await E2EBackend.RegistrationAllowedAsync())
            Assert.That(register, Is.Not.Empty, "Register button should show when setup-status reports registrationAllowed=true");
        else
            Assert.That(register, Is.Empty, "Register button must stay hidden when setup-status reports registrationAllowed=false");
    }

    [Test]
    public async Task OnboardingPage_Step1LayoutElementsAreVisible()
    {
        await StartOnboardingAsync();

        var stepIndicator = AndroidDriver.FindElement(ByAutoId("OnboardingStepIndicator"));
        Assert.That(stepIndicator.Displayed, Is.True, "Step indicator should be visible on onboarding");

        var nipEntry = AndroidDriver.FindElement(ByAutoId("OnboardingEntryNip"));
        Assert.That(nipEntry.Displayed, Is.True, "NIP entry should be visible on step 1");

        var nameEntry = AndroidDriver.FindElement(ByAutoId("OnboardingEntryDisplayName"));
        Assert.That(nameEntry.Displayed, Is.True, "Display name entry should be visible on step 1");

        var emailEntry = AndroidDriver.FindElement(ByAutoId("OnboardingEntryNotificationEmail"));
        Assert.That(emailEntry.Displayed, Is.True, "Notification email entry should be visible on step 1");

        var nextButton = AndroidDriver.FindElement(ByAutoId("OnboardingButtonNext"));
        Assert.That(nextButton.Displayed, Is.True, "Next button should be visible on step 1");

        CaptureLayoutScreenshot("OnboardingPage_Step1");
    }

    [Test]
    public async Task OnboardingPage_Step2LayoutElementsAreVisible()
    {
        var onboarding = await StartOnboardingAsync();
        onboarding.FillCompanyData(
            nip: FakeNip.Next(),
            displayName: "Layout Test Co",
            email: "layout@test.open-ksef.pl");

        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(15));
        var tokenEntry = wait.UntilVisible(ByAutoId("OnboardingEntryKsefToken"));
        Assert.That(tokenEntry.Displayed, Is.True, "Token editor should be visible on step 2");

        var skipLink = AndroidDriver.FindElement(ByAutoId("OnboardingButtonSkipToken"));
        Assert.That(skipLink.Displayed, Is.True, "Skip link should be visible on step 2");

        CaptureLayoutScreenshot("OnboardingPage_Step2_KsefToken");
    }

    [Test]
    public async Task OnboardingPage_Step3SuccessLayoutElementsAreVisible()
    {
        var onboarding = await StartOnboardingAsync();
        onboarding.FillCompanyData(
            nip: FakeNip.Next(),
            displayName: "Layout Test Co",
            email: "layout@test.open-ksef.pl");
        onboarding.SkipKsefToken();
        onboarding.WaitForSuccessStep();

        var successLabel = AndroidDriver.FindElement(ByAutoId("OnboardingLabelSuccess"));
        Assert.That(successLabel.Displayed, Is.True, "Success checkmark should be visible on step 3");

        CaptureLayoutScreenshot("OnboardingPage_Step3_Success");

        onboarding.FinishOnboarding();
        onboarding.WaitForMainApp(TimeSpan.FromSeconds(60));
    }

    [Test]
    public void InvoiceListPage_LayoutElementsAreVisible()
    {
        LoginAsSeededUser();
        new WebDriverWait(AndroidDriver, TimeSpan.FromSeconds(60)).Until(d =>
            d.FindElements(ByAutoId("InvoiceListPageLabelTitle")).Count > 0);

        var titleLabel = AndroidDriver.FindElement(ByAutoId("InvoiceListPageLabelTitle"));
        Assert.That(titleLabel.Displayed, Is.True, "Invoice list title should be visible");
        Assert.That(titleLabel.Text, Does.Contain("Otrzymane"), "Title should contain 'Otrzymane'");

        CaptureLayoutScreenshot("InvoiceListPage");
    }

    [Test]
    public void TenantsPage_LayoutElementsAreVisible()
    {
        OpenSeededTenantList();

        Assert.That(AndroidDriver.FindElements(ByAutoId("TenantsPageFrameTenantItem")), Has.Count.GreaterThanOrEqualTo(2),
            "Tenants page should list the seeded tenants");
        Assert.That(AndroidDriver.FindElements(ByAutoId("TenantsPageButtonAdd")), Is.Not.Empty,
            "Tenants page should show the add button");

        CaptureLayoutScreenshot("TenantsPage");
    }

    [Test]
    public void TenantsPage_TenantFormLayoutIsCorrect()
    {
        OpenSeededTenantList();

        var wait = new WaitHelper(AndroidDriver, TimeSpan.FromSeconds(15));
        wait.UntilVisible(ByAutoId("TenantsPageButtonAdd")).Click();

        var nipEntry = wait.UntilVisible(ByAutoId("TenantFormPageEntryNip"));
        Assert.That(nipEntry.Displayed, Is.True, "NIP entry should be visible on tenant form");

        var nameEntry = AndroidDriver.FindElements(ByAutoId("TenantFormPageEntryDisplayName"));
        Assert.That(nameEntry.Count, Is.GreaterThan(0), "Display name entry should be visible on tenant form");

        var saveButton = AndroidDriver.FindElements(ByAutoId("TenantFormPageButtonSave"));
        Assert.That(saveButton.Count, Is.GreaterThan(0), "Save button should be visible on tenant form");

        var cancelButton = AndroidDriver.FindElements(ByAutoId("TenantFormPageButtonCancel"));
        Assert.That(cancelButton.Count, Is.GreaterThan(0), "Cancel button should be visible on tenant form");

        CaptureLayoutScreenshot("TenantFormPage");

        cancelButton[0].Click();
        wait.UntilVisible(ByAutoId("TenantsPageCollectionViewTenants"));
    }
}
