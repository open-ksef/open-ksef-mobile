using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using static OpenKSeF.Mobile.E2E.Shared.Infrastructure.AndroidSelectors;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

[Category("Login")]
public sealed class LoginFlowTests : AndroidTestBase
{
    [Test]
    public void RopcLoginFlow_CanAuthenticateAndReturnToApp()
    {
        var authFlow = new AndroidOidcLoginFlow(AndroidDriver);
        authFlow.LoginWithKeycloakFromEnvironment();
        authFlow.WaitForAuthenticatedPage(TimeSpan.FromSeconds(60));

        var authenticatedElement = AndroidDriver.FindElements(ByAutoId("InvoiceListPageLabelTitle"))
            .FirstOrDefault()
            ?? AndroidDriver.FindElements(ByAutoId("InvoiceListPageCollectionViewInvoices")).FirstOrDefault()
            ?? AndroidDriver.FindElements(ByAutoId("OnboardingStepIndicator")).FirstOrDefault();

        Assert.That(authenticatedElement, Is.Not.Null, "Expected authenticated page elements after ROPC login.");
    }
}
