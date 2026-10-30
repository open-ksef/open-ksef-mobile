using System.Globalization;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

/// <summary>Polish display and input rules for the issuing screens, independent of device culture.</summary>
public static class IssuingFormat
{
    public static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static string Money(decimal amount, string currency = "PLN") => $"{amount.ToString("N2", Pl)} {currency}";

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string VatRateLabel(string rate) => rate switch
    {
        "0 KR" => "0% (kraj)",
        "zw" => "zw",
        _ => $"{rate}%"
    };

    public static string VatRateCode(string label) => IssuedInvoiceTotals.VatRates.FirstOrDefault(r => VatRateLabel(r) == label) ?? label;

    public static string StatusLabel(string? status, bool shortForm = false) => status switch
    {
        "Draft" => "Szkic",
        "Sending" or "Approved" => "Wysyłanie",
        "Accepted" => shortForm ? "Przyjęta" : "Przyjęta w KSeF",
        "Rejected" => "Odrzucona",
        _ => status ?? string.Empty
    };

    public static string StatusTone(string? status) => status switch
    {
        "Sending" or "Approved" => "Info",
        "Accepted" => "Success",
        "Rejected" => "Error",
        _ => "Neutral"
    };

    public static bool IsSending(string? status) => status is "Sending" or "Approved";

    public static bool IsProduction(string? environment) => string.Equals(environment, "production", StringComparison.OrdinalIgnoreCase);

    public static string EnvironmentLabel(string? environment) => environment?.ToLowerInvariant() switch
    {
        "production" => "PRODUKCJA",
        "demo" => "DEMO",
        "test" => "TEST",
        _ => string.Empty
    };

    public static string EnvironmentTone(string? environment) => environment?.ToLowerInvariant() switch
    {
        "production" => "Production",
        "test" => "Warning",
        _ => "Neutral"
    };

    public static bool TryParseDecimal(string? text, out decimal value) =>
        decimal.TryParse(
            (text ?? string.Empty).Replace(" ", string.Empty).Replace(" ", string.Empty).Replace(',', '.'),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out value);

    public static string Number(decimal value) => value.ToString("0.######", Pl);

    public static string Digits(string? text) => new((text ?? string.Empty).Where(char.IsDigit).ToArray());

    public static string Nrb(string? account)
    {
        var digits = Digits(account);
        if (digits.Length != 26)
            return digits;
        return string.Join(' ', new[] { digits[..2] }.Concat(Enumerable.Range(0, 6).Select(i => digits.Substring(2 + i * 4, 4))));
    }

    public static string NumberPreview(string? pattern, int number, DateOnly date) =>
        (pattern ?? string.Empty)
            .Replace("{nr}", number.ToString(CultureInfo.InvariantCulture))
            .Replace("{MM}", date.ToString("MM", CultureInfo.InvariantCulture))
            .Replace("{RRRR}", date.ToString("yyyy", CultureInfo.InvariantCulture));

    public static string LookupStatus(NipLookupDto result, DateTime at) => result.VatStatus == "Czynny"
        ? $"Czynny podatnik VAT · biała lista {at:dd.MM.yyyy, HH:mm}"
        : $"{result.VatStatus} · biała lista {at:dd.MM.yyyy, HH:mm}";

    public const string LookupNotFound = "Nie ma na białej liście — wpisz dane ręcznie";
    public const string LookupDown = "Biała lista nie odpowiada, wpisz dane ręcznie";
}
