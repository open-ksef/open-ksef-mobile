using System.Text;
using System.Text.RegularExpressions;
using OpenKSeF.Mobile.Models;
using QRCoder;

namespace OpenKSeF.Mobile.Services;

/// <summary>
/// Client-side QR code generation using the Polish ZBP 2D standard.
/// Payload: |PL|ACCOUNT|AMOUNT|NAME|TITLE|||
/// </summary>
public static partial class TransferQrGenerator
{
    private const int MaxNameLength = 20;
    private const int MaxTitleLength = 32;

    private static readonly Dictionary<char, char> PolishDiacritics = new()
    {
        ['ą'] = 'a', ['ć'] = 'c', ['ę'] = 'e', ['ł'] = 'l',
        ['ń'] = 'n', ['ó'] = 'o', ['ś'] = 's', ['ź'] = 'z', ['ż'] = 'z',
        ['Ą'] = 'A', ['Ć'] = 'C', ['Ę'] = 'E', ['Ł'] = 'L',
        ['Ń'] = 'N', ['Ó'] = 'O', ['Ś'] = 'S', ['Ź'] = 'Z', ['Ż'] = 'Z',
    };

    public static byte[] Generate(InvoiceDto invoice)
    {
        var payload = BuildPayload(invoice);

        using var qrGenerator = new QRCoder.QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCoder.QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(20);
    }

    internal static string BuildPayload(InvoiceDto invoice)
    {
        var amount = AmountToGrosze(invoice.AmountGross);
        var name = Sanitize(invoice.VendorName, MaxNameLength);
        var title = Sanitize($"Faktura {invoice.KSeFInvoiceNumber}", MaxTitleLength);

        return $"|PL||{amount}|{name}|{title}|||";
    }

    internal static string AmountToGrosze(decimal amount)
    {
        if (amount <= 0m)
            return "0";
        var grosze = Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        return ((long)grosze).ToString();
    }

    internal static string Sanitize(string value, int maxLength)
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

        var transliterated = sb.ToString();
        var cleaned = AllowedCharsRegex().Replace(transliterated, "");
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    [GeneratedRegex("[^a-zA-Z0-9 .\\-]")]
    private static partial Regex AllowedCharsRegex();
}
