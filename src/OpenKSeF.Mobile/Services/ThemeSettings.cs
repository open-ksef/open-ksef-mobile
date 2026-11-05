namespace OpenKSeF.Mobile.Services;

/// <summary>The theme picked in Konto. Default decides what a phone with no stored choice gets, so it is also the rollback switch for dark mode.</summary>
public static class ThemeSettings
{
    public const string Default = "System";

    private const string Key = "app_theme";

    public static string Load() => Preferences.Default.Get(Key, Default);

    public static void Save(string choice) => Preferences.Default.Set(Key, choice);

    public static AppTheme ToAppTheme(string choice) => choice switch
    {
        "Light" => AppTheme.Light,
        "Dark" => AppTheme.Dark,
        _ => AppTheme.Unspecified,
    };
}
