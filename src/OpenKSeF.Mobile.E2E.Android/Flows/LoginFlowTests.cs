using OpenKSeF.Mobile.E2E.Android.Infrastructure;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenQA.Selenium.Appium;

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

        var authenticatedElement = AndroidDriver.FindElements(MobileBy.AccessibilityId("InvoiceListPageLabelTitle"))
            .FirstOrDefault()
            ?? AndroidDriver.FindElements(MobileBy.AccessibilityId("InvoiceListPageCollectionViewInvoices")).FirstOrDefault()
            ?? AndroidDriver.FindElements(MobileBy.AccessibilityId("OnboardingStepIndicator")).FirstOrDefault();

        Assert.That(authenticatedElement, Is.Not.Null, "Expected authenticated page elements after ROPC login.");
    }
}
