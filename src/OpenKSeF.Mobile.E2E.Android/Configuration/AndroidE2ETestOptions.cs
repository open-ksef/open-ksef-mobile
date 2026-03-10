using OpenKSeF.Mobile.E2E.Shared.Configuration;

namespace OpenKSeF.Mobile.E2E.Android.Configuration;

public sealed class AndroidE2ETestOptions
{
    public string AppiumServerUrl { get; init; } = "http://127.0.0.1:4723/";
    public string PlatformName { get; init; } = "Android";
    public string AutomationName { get; init; } = "UiAutomator2";
    public string DeviceName { get; init; } = "Android Emulator";
    public string AppPath { get; init; } = string.Empty;
    public string AppPackage { get; init; } = "com.openksef.mobile";
    public string AppActivity { get; init; } = ".MainActivity";
    public int CommandTimeoutSeconds { get; init; } = 300;

    public static AndroidE2ETestOptions From(MobileTestConfiguration baseConfig)
    {
        ArgumentNullException.ThrowIfNull(baseConfig);

        var androidAppPath = ReadString("ANDROID_APP_PATH", string.Empty);

        return new AndroidE2ETestOptions
        {
            AppiumServerUrl = baseConfig.AppiumServerUrl,
            PlatformName = "Android",
            AutomationName = "UiAutomator2",
            DeviceName = ReadString("ANDROID_DEVICE_NAME", baseConfig.DeviceName),
            AppPath = ResolveAppPath(androidAppPath, baseConfig.AppPath),
            AppPackage = ReadString("ANDROID_APP_PACKAGE", "com.openksef.mobile"),
            AppActivity = ReadString("ANDROID_APP_ACTIVITY", ".MainActivity"),
            CommandTimeoutSeconds = ReadInt("ANDROID_COMMAND_TIMEOUT_SECONDS", 300)
        };
    }

    private static string ResolveAppPath(string androidAppPath, string sharedAppPath)
    {
        if (!string.IsNullOrWhiteSpace(androidAppPath))
        {
            return Path.GetFullPath(androidAppPath);
        }

        if (!string.IsNullOrWhiteSpace(sharedAppPath))
        {
            return Path.GetFullPath(sharedAppPath);
        }

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot is null)
        {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "OpenKSeF.Mobile.apk"));
        }

        var androidOutputDir = Path.Combine(repoRoot, "src", "OpenKSeF.Mobile", "bin", "Debug", "net8.0-android");
        if (Directory.Exists(androidOutputDir))
        {
            var latestApk = Directory
                .EnumerateFiles(androidOutputDir, "*.apk", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latestApk is not null)
            {
                return latestApk.FullName;
            }
        }

        return Path.GetFullPath(Path.Combine(androidOutputDir, "com.openksef.mobile-Signed.apk"));
    }

    private static string? FindRepoRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            var marker = Path.Combine(current.FullName, "src", "OpenKSeF.Mobile", "OpenKSeF.Mobile.csproj");
            if (File.Exists(marker))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string ReadString(string key, string defaultValue)
    {
        return Environment.GetEnvironmentVariable(key) ?? defaultValue;
    }

    private static int ReadInt(string key, int defaultValue)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(key), out var value) ? value : defaultValue;
    }
}
