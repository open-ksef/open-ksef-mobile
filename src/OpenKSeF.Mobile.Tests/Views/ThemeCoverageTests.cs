using System.Globalization;
using System.Text.RegularExpressions;
using OpenKSeF.Mobile.ViewModels;
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
                if (lines[i].Contains("<GradientStop") || lines[i].Contains("<SwipeItem "))
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

    [Fact]
    public void KodIPng_IsOpaqueGreyscale()
    {
        var png = IssuedInvoiceDetailsViewModel.QrPng("https://qr-test.ksef.mf.gov.pl/invoice/1111111111/05-11-2026/abc");

        const int ihdrColourType = 25;
        Assert.Equal(0, png[ihdrColourType]);
        Assert.Equal(-1, png.AsSpan().IndexOf("tRNS"u8));
    }

    [Theory]
    [InlineData("UiPrimary", "UiOnPrimary")]
    [InlineData("UiPrimaryDark", "UiOnPrimaryDark")]
    public void PrimaryButton_MeetsAaContrast(string background, string foreground)
    {
        Assert.True(Contrast(Color(background), Color(foreground)) >= 4.5);
    }

    [Fact]
    public void Bold_UsesARealBoldFont()
    {
        var files = Directory.GetFiles(Path.Combine(AppDir, "Views"), "*.xaml").Append(Path.Combine(AppDir, "Resources/Styles/Styles.xaml"));

        Assert.Empty(files.Where(f => File.ReadAllText(f).Contains("FontAttributes")));
    }

    [Fact]
    public void FontFamilies_AreRegisteredAndBundled()
    {
        var registered = Regex.Matches(File.ReadAllText(Path.Combine(AppDir, "MauiProgram.cs")), @"AddFont\(""([^""]+)"", ""(\w+)""\)")
            .ToDictionary(m => m.Groups[2].Value, m => m.Groups[1].Value);
        Assert.All(registered.Values, file => Assert.True(File.Exists(Path.Combine(AppDir, "Resources/Fonts", file)), file));

        var files = Directory.GetFiles(Path.Combine(AppDir, "Views"), "*.xaml").Append(Path.Combine(AppDir, "Resources/Styles/Styles.xaml"));
        var used = files.SelectMany(f => Regex.Matches(File.ReadAllText(f), @"(?:FontFamily=|Property=""(?:Label\.)?FontFamily"" Value=)""([^""]+)""").Select(m => m.Groups[1].Value)).ToHashSet();

        Assert.Contains("InterBold", used);
        Assert.Empty(used.Where(family => !registered.ContainsKey(family)));
    }

    [Fact]
    public void BundledFonts_HaveOflNotice()
    {
        var notices = File.ReadAllText(Path.Combine(AppDir, "Resources/Raw/pdf/THIRD-PARTY-NOTICES.txt"));

        Assert.Contains("Inter 4.1", notices);
        Assert.Contains("JetBrains Mono 2.304", notices);
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
