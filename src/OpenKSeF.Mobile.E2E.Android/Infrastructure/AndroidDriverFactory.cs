using System.Diagnostics;
using OpenKSeF.Mobile.E2E.Android.Configuration;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace OpenKSeF.Mobile.E2E.Android.Infrastructure;

public static class AndroidDriverFactory
{
    public static AndroidDriver CreateDriver(AndroidE2ETestOptions options, bool autoGrantPermissions = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureAppAvailable(options);
        RestartBloatedEmulator();

        var appiumOptions = BuildAppiumOptions(options, autoGrantPermissions);
        return new AndroidDriver(
            new Uri(options.AppiumServerUrl),
            appiumOptions,
            TimeSpan.FromSeconds(options.CommandTimeoutSeconds));
    }

    public static AppiumOptions BuildAppiumOptions(AndroidE2ETestOptions options, bool autoGrantPermissions = true)
    {
        ArgumentNullException.ThrowIfNull(options);

        var appiumOptions = new AppiumOptions
        {
            PlatformName = options.PlatformName,
            AutomationName = options.AutomationName,
            DeviceName = options.DeviceName
        };

        if (File.Exists(options.AppPath))
            appiumOptions.App = options.AppPath;

        appiumOptions.AddAdditionalAppiumOption("appPackage", options.AppPackage);
        appiumOptions.AddAdditionalAppiumOption("appActivity", options.AppActivity);
        appiumOptions.AddAdditionalAppiumOption("noReset", true);
        appiumOptions.AddAdditionalAppiumOption("autoGrantPermissions", autoGrantPermissions);
        return appiumOptions;
    }

    // The host GPU renderer leaks 40-80 MB of qemu private memory per killed app process (T-835); a cold boot between fixtures bounds it.
    private static void RestartBloatedEmulator()
    {
        var limitMb = int.TryParse(Environment.GetEnvironmentVariable("E2E_EMULATOR_RESTART_MB"), out var mb) ? mb : 6144;
        var qemu = Process.GetProcessesByName("qemu-system-x86_64").FirstOrDefault();
        var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
        if (limitMb <= 0 || qemu is null || string.IsNullOrEmpty(androidHome) || qemu.PrivateMemorySize64 / (1024 * 1024) < limitMb)
            return;

        var adb = Path.Combine(androidHome, "platform-tools", "adb");
        var avd = Run(adb, "emu", "avd", "name").Split('\n')[0].Trim();
        TestContext.Progress.WriteLine($"Emulator {avd} at {qemu.PrivateMemorySize64 / (1024 * 1024)} MB private, cold booting it.");
        Run(adb, "emu", "kill");
        if (!qemu.WaitForExit(TimeSpan.FromSeconds(60)))
            qemu.Kill();

        Process.Start(new ProcessStartInfo(Path.Combine(androidHome, "emulator", "emulator"), $"-avd {avd} -no-snapshot -no-boot-anim -no-audio") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized });
        Run(adb, "wait-for-device");
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (Run(adb, "shell", "getprop", "sys.boot_completed").Trim() != "1")
        {
            if (DateTime.UtcNow > deadline)
                throw new InvalidOperationException($"Emulator {avd} did not boot within 3 minutes after restart.");
            Thread.Sleep(2000);
        }

        Run(adb, "shell", "settings", "put", "secure", "autofill_service", "null");
    }

    private static string Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    private static void EnsureAppAvailable(AndroidE2ETestOptions options)
    {
        if (File.Exists(options.AppPath))
            return;

        if (!string.IsNullOrWhiteSpace(options.AppPackage))
            return;

        throw new FileNotFoundException(
            $"Android app binary was not found at '{options.AppPath}' and no appPackage specified. Build the MAUI app or set ANDROID_APP_PATH to a valid .apk/.aab file.",
            options.AppPath);
    }
}
