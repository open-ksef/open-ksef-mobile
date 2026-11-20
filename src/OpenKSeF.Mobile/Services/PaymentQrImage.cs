using System.Globalization;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Services;

/// <summary>Shared payment QR: the server PNG unchanged plus a caption band below it (REQ-0055).</summary>
public static partial class PaymentQrImage
{
    private const string Ellipsis = "…";
    private const int MaxFileNameChars = 100;
    private static readonly char[] InvalidFileNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    public readonly record struct Layout(float FontSize, float Margin, float LineHeight, int Width, int QrHeight, int Height)
    {
        public (float X, float Y, float Width, float Height) Line(int index) =>
            (Margin, QrHeight + Margin / 2 + index * LineHeight, Width - 2 * Margin, LineHeight);
    }

    public static IReadOnlyList<string> Caption(TransferDetailsDto transfer) =>
        new[] { transfer.Title, IssuingFormat.Money(transfer.Amount, transfer.Currency), transfer.RecipientName }
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToList();

    public static string FileName(string? invoiceNumber)
    {
        var name = string.Concat((invoiceNumber ?? string.Empty).Trim()
            .Select(c => char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format || InvalidFileNameChars.Contains(c) ? '_' : c));
        if (name.Length > MaxFileNameChars)
            name = name[..(char.IsHighSurrogate(name[MaxFileNameChars - 1]) ? MaxFileNameChars - 1 : MaxFileNameChars)];
        return name.Length == 0 ? "QR.png" : $"QR {name}.png";
    }

    public static Layout Measure(int qrWidth, int qrHeight, int lineCount)
    {
        var fontSize = MathF.Max(12, qrWidth / 20f);
        var lineHeight = fontSize * 1.5f;
        return new Layout(fontSize, fontSize, lineHeight, qrWidth, qrHeight, qrHeight + (int)MathF.Ceiling(lineCount * lineHeight + fontSize));
    }

    public static string Fit(string text, float maxWidth, Func<string, float> measure)
    {
        if (measure(text) <= maxWidth)
            return text;

        var starts = StringInfo.ParseCombiningCharacters(text);
        for (var count = starts.Length - 1; count > 0; count--)
        {
            var cut = text[..starts[count]].TrimEnd() + Ellipsis;
            if (measure(cut) <= maxWidth)
                return cut;
        }

        return Ellipsis;
    }

    public static partial byte[] Compose(byte[] qrPng, IReadOnlyList<string> lines);
}
