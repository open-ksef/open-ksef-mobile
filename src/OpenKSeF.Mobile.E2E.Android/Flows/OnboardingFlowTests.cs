using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("Onboarding")]
public sealed class OnboardingFlowTests : AndroidTestBase
{
    private E2EBackend.User _user = null!;

    [SetUp]
    public async Task CreateUserWithoutTenant() => _user = await E2EBackend.CreateUserAsync();

    [TearDown]
    public async Task SignOutAndDeleteUser()
    {
        new AndroidOidcLoginFlow(AndroidDriver).SignOut();
        await E2EBackend.DeleteUserAsync(_user);
    }

    [Test]
    public void OnboardingFlow_NewUser_FullFlowWithToken()
    {
        new AndroidOidcLoginFlow(AndroidDriver).Login(_user.Username, _user.Password);

        var onboarding = new AndroidOnboardingFlow(AndroidDriver);
        onboarding.WaitForOnboardingPage();

        // Step 1: Company data
        onboarding.FillCompanyData(
            nip: "1111111111",
            displayName: "Mobile E2E Company",
            email: "mobile@test.open-ksef.pl");

        // Step 2: KSeF token
        onboarding.FillKsefToken("e2e-mobile-fake-token");

        // Step 3: Success -- then finish
        onboarding.WaitForSuccessStep();

        var successLabel = AndroidDriver.FindElement(ByAutoId("OnboardingLabelSuccess"));
        Assert.That(successLabel.Displayed, Is.True, "Success label should be visible after onboarding");

        onboarding.FinishOnboarding();

        // Verify we reach the main invoice list (no Step 4 notification prompt)
        onboarding.WaitForMainApp(TimeSpan.FromSeconds(60));

        var mainPageVisible = AndroidDriver.FindElements(ByAutoId("InvoiceListPageLabelTitle")).Count > 0
            || AndroidDriver.FindElements(ByAutoId("InvoiceListPageCollectionViewInvoices")).Count > 0;
        Assert.That(mainPageVisible, Is.True, "Expected main invoice page after completing onboarding");
    }

    [Test]
    public void OnboardingFlow_SkipToken()
    {
        new AndroidOidcLoginFlow(AndroidDriver).Login(_user.Username, _user.Password);

        var onboarding = new AndroidOnboardingFlow(AndroidDriver);
        onboarding.WaitForOnboardingPage();

        // Step 1: Company data
        onboarding.FillCompanyData(
            nip: "1111111111",
            displayName: "Skip Token Co",
            email: "skip@test.open-ksef.pl");

        // Step 2: Skip token
        onboarding.SkipKsefToken();

        // Step 3: Success (with warning about missing token) -- then finish
        onboarding.WaitForSuccessStep();

        var successLabel = AndroidDriver.FindElement(ByAutoId("OnboardingLabelSuccess"));
        Assert.That(successLabel.Displayed, Is.True, "Success label should be visible even when token skipped");

        onboarding.FinishOnboarding();

        onboarding.WaitForMainApp(TimeSpan.FromSeconds(60));
    }

    [Test]
    public void OnboardingFlow_NipValidation()
    {
        new AndroidOidcLoginFlow(AndroidDriver).Login(_user.Username, _user.Password);

        var onboarding = new AndroidOnboardingFlow(AndroidDriver);
        onboarding.WaitForOnboardingPage();

        // Enter invalid NIP (too short)
        onboarding.FillCompanyData(
            nip: "123",
            displayName: "Bad NIP Co",
            email: "bad@test.open-ksef.pl");

        // Should still be on step 1 (NIP validation failed)
        Assert.That(onboarding.IsStepIndicatorVisible(), Is.True, "Should still be on onboarding page");
        Assert.That(onboarding.IsNipEntryVisible(), Is.True, "NIP entry should still be visible after validation error");
    }
}
