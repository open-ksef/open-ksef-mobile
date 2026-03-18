using OpenQA.Selenium;
using OpenQA.Selenium.Appium;

namespace OpenKSeF.Mobile.E2E.Shared.Infrastructure;

/// <summary>
/// Maps MAUI AutomationId to Android resource-id selector.
/// MAUI AutomationId becomes "{package}:id/{AutomationId}" on Android.
/// Uses MobileBy.Id which sends the Appium 'id' strategy (resource-id lookup),
/// not Selenium By.Id which uses CSS selector strategy.
/// </summary>
public static class AndroidSelectors
{
    private const string DefaultPackage = "com.openksef.mobile";

    private static readonly string PackagePrefix =
        (Environment.GetEnvironmentVariable("ANDROID_APP_PACKAGE") ?? DefaultPackage) + ":id/";

    public static By ByAutoId(string automationId) => MobileBy.Id($"{PackagePrefix}{automationId}");
}
