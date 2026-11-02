using System.Globalization;
using System.Text.RegularExpressions;
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

    private static readonly Regex DecimalInput = new(@"^-?(\d+|\d{1,3}( \d{3})+)?([.,]\d*)?$");
    private static readonly Regex ThousandsLike = new(@"^-?[1-9]\d{0,2}[.,]\d{3}$");

    /// <summary>
    /// Either '.' or ',' is the decimal separator, at most one of them; spaces group thousands. "1,000" or "12.345" could
    /// mean thousands, so they are refused: an accepted invoice cannot be changed. A leading 0 or 4+ digits is not thousands.
    /// </summary>
    public static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0;
        var input = NormalizeDecimal(text);
        return DecimalInput.IsMatch(input)
            && !ThousandsLike.IsMatch(input)
            && !TooManyDigits(input)
            && decimal.TryParse(
                input.Replace(" ", string.Empty).Replace(',', '.'),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out value);
    }

    /// <summary>Beyond these limits decimal silently rounds the input, and 1000 lines of quantity x price could overflow the totals.</summary>
    private static bool TooManyDigits(string input)
    {
        var parts = input.Replace(" ", string.Empty).TrimStart('-').Split('.', ',');
        return parts[0].Length > 12 || parts.Length > 1 && parts[^1].Length > 6;
    }

    public static string? AmbiguousDecimalError(string? text)
    {
        var input = NormalizeDecimal(text);
        if (!ThousandsLike.IsMatch(input))
            return null;

        var parts = input.Split('.', ',');
        return $"Nie wiadomo, czy „{input}” to {parts[0]}{parts[1]} czy {parts[0]},{parts[1]}. Wpisz {parts[0]}{parts[1]} albo {parts[0]},{parts[1]}0.";
    }

    /// <summary>Live check for one field: never flags a half-typed number, so a keystroke like "-" or "5," stays quiet.</summary>
    public static string? DecimalInputError(string? text)
    {
        var input = NormalizeDecimal(text);
        if (!input.All(c => char.IsAsciiDigit(c) || c is '.' or ',' or '-' or ' '))
            return "Ilość i cena mogą zawierać tylko cyfry, przecinek lub kropkę, spację i minus. Popraw wartość.";
        return TooManyDigits(input)
            ? "Za dużo cyfr: najwyżej 12 przed przecinkiem i 6 po nim."
            : AmbiguousDecimalError(input);
    }

    private static string NormalizeDecimal(string? text) => (text ?? string.Empty).Replace(' ', ' ').Replace(' ', ' ').Trim();

    public static string Number(decimal value) => value.ToString("0.######", Pl);

    public static string EditableNumber(decimal value) => TryParseDecimal(Number(value), out _) ? Number(value) : value.ToString("0.0000##", Pl);

    public static string FileStem(string name) => Regex.Replace(name, @"[^\w.-]+", "_");

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

    private static readonly Regex KsefDetailsNoise = new(@"\s*\(traceId:[^)]*\)|\s*\([^()]*:\s*[{\[].*?[}\]]\)|(?<=^|;\s)[a-z]+(?:-[a-z]+)+:\s*");

    private static readonly Regex KsefRawText = new(@"[{\[]\s*""|traceId|^HTTP \d{3}", RegexOptions.IgnoreCase);

    /// <summary>KSeF details mix a readable sentence with a machine code, JSON and a traceId. Text that still looks raw after cleaning is dropped.</summary>
    public static string? CleanKsefDetails(string? details)
    {
        var clean = KsefDetailsNoise.Replace(details ?? string.Empty, string.Empty).Trim();
        return clean.Length == 0 || KsefRawText.IsMatch(clean) ? null : clean;
    }

    public const string LookupNotFound = "Nie ma na białej liście — wpisz dane ręcznie";
    public const string LookupDown = "Biała lista nie odpowiada, wpisz dane ręcznie";
}
