using Microsoft.Extensions.Configuration;

namespace OpenKSeF.Mobile.E2E.Shared.Configuration;

public sealed class MobileTestConfiguration
{
    public string AppiumServerUrl { get; init; } = "http://127.0.0.1:4723/";
    public string AppPath { get; init; } = string.Empty;
    public string PlatformName { get; init; } = "Android";
    public string DeviceName { get; init; } = "Android Emulator";
    public int CommandTimeoutSeconds { get; init; } = 180;

    public static MobileTestConfiguration Load()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Test.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        return new MobileTestConfiguration
        {
            AppiumServerUrl = ReadString(configuration, "APPIUM_SERVER_URL", "Appium:ServerUrl", "http://127.0.0.1:4723/"),
            AppPath = ReadString(configuration, "APP_PATH", "Appium:AppPath", string.Empty),
            PlatformName = ReadString(configuration, "PLATFORM_NAME", "Appium:PlatformName", "Android"),
            DeviceName = ReadString(configuration, "DEVICE_NAME", "Appium:DeviceName", "Android Emulator"),
            CommandTimeoutSeconds = ReadInt(configuration, "COMMAND_TIMEOUT_SECONDS", "Appium:CommandTimeoutSeconds", 180)
        };
    }

    private static string ReadString(IConfiguration configuration, string envKey, string jsonKey, string defaultValue)
    {
        return configuration[envKey] ?? configuration[jsonKey] ?? defaultValue;
    }

    private static int ReadInt(IConfiguration configuration, string envKey, string jsonKey, int defaultValue)
    {
        if (int.TryParse(configuration[envKey], out var envValue))
        {
            return envValue;
        }

        if (int.TryParse(configuration[jsonKey], out var jsonValue))
        {
            return jsonValue;
        }

        return defaultValue;
    }
}
