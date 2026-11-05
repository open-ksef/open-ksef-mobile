using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Views;

public class ThemeCoverageTests
{
    private static readonly string AppDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../OpenKSeF.Mobile"));
    private static readonly string ColorsXaml = File.ReadAllText(Path.Combine(AppDir, "Resources/Styles/Colors.xaml"));

    private static string Color(string key) =>
        Regex.Match(ColorsXaml, $"<Color x:Key=\"{key}\">(#[0-9a-fA-F]{{6}})</Color>").Groups[1].Value;

    [Fact]
    public void Views_HaveNoColourStaticResourceOutsideAppThemeBinding()
    {
        var colourKeys = Regex.Matches(ColorsXaml, "x:Key=\"(\\w+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        var offenders = new List<string>();

        foreach (var path in Directory.GetFiles(Path.Combine(AppDir, "Views"), "*.xaml"))
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("<GradientStop"))
                    continue;

                var bare = Regex.Replace(lines[i], @"\{AppThemeBinding [^""]*\}", string.Empty);
                foreach (Match m in Regex.Matches(bare, @"\{StaticResource (\w+)\}"))
                {
                    if (colourKeys.Contains(m.Groups[1].Value))
                        offenders.Add($"{Path.GetFileName(path)}:{i + 1} {m.Value}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryLightToken_HasADarkPartner()
    {
        var keys = Regex.Matches(ColorsXaml, "<Color x:Key=\"(Ui\\w+)\">").Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(keys);
        Assert.Empty(keys.Where(k => !k.EndsWith("Dark") && !keys.Contains(k + "Dark")));
    }

    [Fact]
    public void QrCode_StaysOnLiteralWhite()
    {
        var page = File.ReadAllText(Path.Combine(AppDir, "Views/QrCodePage.xaml"));

        Assert.Matches(new Regex(@"BackgroundColor=""White""\s+HorizontalOptions=""Center"">\s+<Image Source=""\{Binding QrImage\}"""), page);
    }

    [Theory]
    [InlineData("UiPrimary", "UiOnPrimary")]
    [InlineData("UiPrimaryDark", "UiOnPrimaryDark")]
    public void PrimaryButton_MeetsAaContrast(string background, string foreground)
    {
        Assert.True(Contrast(Color(background), Color(foreground)) >= 4.5);
    }

    private static double Contrast(string a, string b)
    {
        var (hi, lo) = (Luminance(a), Luminance(b));
        if (hi < lo)
            (hi, lo) = (lo, hi);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance(string hex)
    {
        double Channel(int start)
        {
            var c = int.Parse(hex.Substring(start, 2), NumberStyles.HexNumber) / 255d;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }
}
