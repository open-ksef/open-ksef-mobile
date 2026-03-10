using System.Text;
using System.Text.RegularExpressions;
using QRCoder;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

public class QrCodeTests
{
    private static readonly Dictionary<char, char> PolishDiacritics = new()
    {
        ['ą'] = 'a', ['ć'] = 'c', ['ę'] = 'e', ['ł'] = 'l',
        ['ń'] = 'n', ['ó'] = 'o', ['ś'] = 's', ['ź'] = 'z', ['ż'] = 'z',
        ['Ą'] = 'A', ['Ć'] = 'C', ['Ę'] = 'E', ['Ł'] = 'L',
        ['Ń'] = 'N', ['Ó'] = 'O', ['Ś'] = 'S', ['Ź'] = 'Z', ['Ż'] = 'Z',
    };

    private static string Sanitize(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (PolishDiacritics.TryGetValue(ch, out var replacement))
                sb.Append(replacement);
            else
                sb.Append(ch);
        }

        var cleaned = Regex.Replace(sb.ToString(), "[^a-zA-Z0-9 .\\-]", "");
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    private static string AmountToGrosze(decimal amount)
    {
        if (amount <= 0m)
            return "0";
        var grosze = Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        return ((long)grosze).ToString();
    }

    private static string BuildZbpPayload(string vendorName, decimal amount, string invoiceNumber, string? account = null)
    {
        var sanitizedAccount = string.Empty;
        if (!string.IsNullOrWhiteSpace(account))
        {
            var digitsOnly = Regex.Replace(account, "[^0-9]", "");
            if (digitsOnly.Length == 26) sanitizedAccount = digitsOnly;
        }

        var name = Sanitize(vendorName, 20);
        var title = Sanitize($"Faktura {invoiceNumber}", 32);
        var grosze = AmountToGrosze(amount);

        return $"|PL|{sanitizedAccount}|{grosze}|{name}|{title}|||";
    }

    [Fact]
    public void BuildPayload_ProducesZbpFormat_WithEightSegments()
    {
        var payload = BuildZbpPayload("Test Vendor", 500m, "FV/2026/001");

        var segments = payload.Split('|');
        Assert.Equal(9, segments.Length); // leading empty + 8 segments = 9 parts from Split
        Assert.Empty(segments[0]);
        Assert.Equal("PL", segments[1]);
    }

    [Fact]
    public void BuildPayload_AmountInGrosze()
    {
        var payload = BuildZbpPayload("Vendor", 12.34m, "FV/001");
        var segments = payload.Split('|');
        Assert.Equal("1234", segments[3]);
    }

    [Fact]
    public void BuildPayload_WholeAmountInGrosze()
    {
        var payload = BuildZbpPayload("Vendor", 500m, "FV/001");
        var segments = payload.Split('|');
        Assert.Equal("50000", segments[3]);
    }

    [Fact]
    public void BuildPayload_ZeroAmount_ProducesZero()
    {
        var payload = BuildZbpPayload("Vendor", 0m, "FV/001");
        var segments = payload.Split('|');
        Assert.Equal("0", segments[3]);
    }

    [Fact]
    public void BuildPayload_PolishDiacritics_Transliterated()
    {
        var payload = BuildZbpPayload("Łódź Sp. z o.o.", 100m, "FV/001");
        var segments = payload.Split('|');
        Assert.Equal("Lodz Sp. z o.o.", segments[4]);
    }

    [Fact]
    public void BuildPayload_NameTruncatedTo20Chars()
    {
        var longName = "ABCDEFGHIJ KLMNOPQRST Extra";
        var payload = BuildZbpPayload(longName, 100m, "FV/001");
        var segments = payload.Split('|');
        Assert.Equal(20, segments[4].Length);
    }

    [Fact]
    public void BuildPayload_TitleTruncatedTo32Chars()
    {
        var longNumber = "FV/2026/000000000000000000000001";
        var payload = BuildZbpPayload("V", 100m, longNumber);
        var segments = payload.Split('|');
        Assert.True(segments[5].Length <= 32);
    }

    [Fact]
    public void BuildPayload_WithAccount_Includes26DigitNrb()
    {
        var account = "12345678901234567890123456";
        var payload = BuildZbpPayload("Vendor", 100m, "FV/001", account);
        var segments = payload.Split('|');
        Assert.Equal(account, segments[2]);
    }

    [Fact]
    public void BuildPayload_WithoutAccount_EmptySegment()
    {
        var payload = BuildZbpPayload("Vendor", 100m, "FV/001");
        var segments = payload.Split('|');
        Assert.Empty(segments[2]);
    }

    [Fact]
    public void BuildPayload_InvalidAccount_EmptySegment()
    {
        var payload = BuildZbpPayload("Vendor", 100m, "FV/001", "123");
        var segments = payload.Split('|');
        Assert.Empty(segments[2]);
    }

    [Fact]
    public void BuildPayload_TrailingSegmentsEmpty()
    {
        var payload = BuildZbpPayload("Vendor", 100m, "FV/001");
        var segments = payload.Split('|');
        Assert.Empty(segments[6]);
        Assert.Empty(segments[7]);
        Assert.Empty(segments[8]);
    }

    [Fact]
    public void BuildPayload_SpecialCharsStripped()
    {
        var payload = BuildZbpPayload("Firma & Co. (PL)", 100m, "FV/001");
        var segments = payload.Split('|');
        Assert.DoesNotContain("&", segments[4]);
        Assert.DoesNotContain("(", segments[4]);
        Assert.DoesNotContain(")", segments[4]);
        Assert.Contains("Co.", segments[4]);
    }

    [Fact]
    public void QrCodeGeneration_ProducesValidPng()
    {
        var payload = "|PL||50000|Test Vendor|Faktura FV-001|||";

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(qrCodeData);
        var bytes = qrCode.GetGraphic(10);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);

        // PNG magic bytes
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal(0x50, bytes[1]); // P
        Assert.Equal(0x4E, bytes[2]); // N
        Assert.Equal(0x47, bytes[3]); // G
    }
}
