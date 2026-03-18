using OpenQA.Selenium;
using OpenQA.Selenium.Appium;

namespace OpenKSeF.Mobile.E2E.Shared.Infrastructure;

/// <summary>
/// Maps MAUI AutomationId to Android resource-id selector.
/// MAUI AutomationId becomes "com.openksef.mobile:id/{AutomationId}" on Android.
/// Uses MobileBy.Id which sends the Appium 'id' strategy (resource-id lookup),
/// not Selenium By.Id which uses CSS selector strategy.
/// </summary>
public static class AndroidSelectors
{
    private const string PackagePrefix = "com.openksef.mobile:id/";

    public static By ByAutoId(string automationId) => MobileBy.Id($"{PackagePrefix}{automationId}");
}
