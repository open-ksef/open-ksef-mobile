using System.Text.RegularExpressions;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Views;

public class PlatformHardeningTests
{
    private static readonly string AppDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../OpenKSeF.Mobile"));

    private static string Read(string path) => File.ReadAllText(Path.Combine(AppDir, path));

    [Fact]
    public void PdfPage_HasCspAndNoInlineScript()
    {
        var html = Read("Resources/Raw/pdf/index.html");

        var csp = Regex.Match(html, @"<meta http-equiv=""Content-Security-Policy"" content=""([^""]+)""").Groups[1].Value;
        Assert.Equal(["default-src 'none'", "script-src 'self'", "connect-src 'self'"], csp.Split(';', StringSplitOptions.TrimEntries));
        Assert.Empty(Regex.Matches(html, @"<script(?![^>]*\ssrc=)[^>]*>"));
        Assert.True(File.Exists(Path.Combine(AppDir, "Resources/Raw/pdf/index.js")));
    }

    [Fact]
    public void AndroidManifest_DeclaresVibrateForCopyHaptics() =>
        Assert.Contains("<uses-permission android:name=\"android.permission.VIBRATE\" />", Read("Platforms/Android/AndroidManifest.xml"));

    [Fact]
    public void TouchBehaviors_BindToAttachedViewBindingContext()
    {
        var xaml = string.Concat(Directory.EnumerateFiles(AppDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText));
        var behaviors = Regex.Matches(xaml, @"<toolkit:TouchBehavior[^>]*>");

        Assert.NotEmpty(behaviors);
        Assert.All(behaviors, m => Assert.Matches(@"BindingContext=""\{Binding BindingContext, Source=\{x:Reference \w+\}", m.Value));
    }

    [Fact]
    public void ReleaseNetworkConfig_HasNoCleartextDomains()
    {
        var config = Read("Platforms/Android/Resources/xml/network_security_config.xml");

        Assert.DoesNotContain("domain-config", config);
        Assert.DoesNotContain("cleartextTrafficPermitted=\"true\"", config);
        Assert.Contains("10.0.2.2", Read("Platforms/Android/DebugResources/xml/network_security_config.xml"));
    }

    [Fact]
    public void SearchBar_TextFollowsAppTheme()
    {
        var style = Regex.Match(Read("Resources/Styles/Styles.xaml"), @"<Style TargetType=""SearchBar"">(.*?)</Style>", RegexOptions.Singleline).Groups[1].Value;

        foreach (var property in new[] { "TextColor", "PlaceholderColor", "CancelButtonColor", "SearchIconColor" })
            Assert.Matches($@"Property=""{property}"" Value=""\{{AppThemeBinding Light=", style);
    }
}
