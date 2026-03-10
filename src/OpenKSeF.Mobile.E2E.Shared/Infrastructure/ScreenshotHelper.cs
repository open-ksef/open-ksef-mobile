using OpenQA.Selenium;

namespace OpenKSeF.Mobile.E2E.Shared.Infrastructure;

public static class ScreenshotHelper
{
    public static string Capture(IWebDriver driver, string outputDirectory, string fileNamePrefix)
    {
        Directory.CreateDirectory(outputDirectory);

        if (driver is not ITakesScreenshot screenshotDriver)
        {
            throw new InvalidOperationException("Driver does not support screenshots.");
        }

        var filePath = Path.Combine(
            outputDirectory,
            $"{fileNamePrefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png");

        var screenshot = screenshotDriver.GetScreenshot();
        screenshot.SaveAsFile(filePath);
        return filePath;
    }
}
