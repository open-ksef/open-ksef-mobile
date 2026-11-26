using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

/// <summary>One "Dane do przelewu" row built from /transfer: shown value, and the text a bank form field accepts on paste.</summary>
public partial class TransferFieldRow : ObservableObject
{
    private int _copies;

    public required string Caption { get; init; }
    public required string Value { get; init; }
    public required string AutomationId { get; init; }
    public string? ClipboardText { get; init; }
    public string? CopiedMessage { get; init; }

    public bool IsCopyable => ClipboardText is not null;
    public bool ShowCopyIcon => IsCopyable && !IsCopied;
    public string Description => $"{Caption}: {Value}";
    public string? Hint => IsCopyable ? "Dotknij dwukrotnie, aby skopiować" : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCopyIcon))]
    private bool _isCopied;

    [RelayCommand]
    private Task CopyAsync() => CopyCoreAsync(false);

    [RelayCommand]
    private Task LongPressAsync() => CopyCoreAsync(true);

    private async Task CopyCoreAsync(bool longPress)
    {
        if (ClipboardText is null)
            return;

        try
        {
            await Clipboard.Default.SetTextAsync(ClipboardText);
        }
        catch (Exception)
        {
            await Shell.Current.DisplayAlertAsync("Błąd", "Nie udało się skopiować.", "OK");
            return;
        }

        var copy = ++_copies;
        IsCopied = true;
        await CopyFeedback.ShowAsync(CopiedMessage!, longPress);
        await Task.Delay(1500);
        if (copy == _copies)
            IsCopied = false;
    }

    public static IReadOnlyList<TransferFieldRow> From(TransferDetailsDto transfer, string page)
    {
        var rows = new List<TransferFieldRow>();

        if (!string.IsNullOrWhiteSpace(transfer.RecipientName))
            rows.Add(new() { Caption = "Odbiorca", Value = transfer.RecipientName.Trim(), ClipboardText = transfer.RecipientName.Trim(), CopiedMessage = "Skopiowano odbiorcę", AutomationId = $"{page}RowCopyRecipient" });

        if (!string.IsNullOrWhiteSpace(transfer.RecipientAccount))
            rows.Add(new() { Caption = "Numer konta", Value = AccountDisplay(transfer.RecipientAccount), ClipboardText = AccountClipboard(transfer.RecipientAccount), CopiedMessage = "Skopiowano numer konta", AutomationId = $"{page}RowCopyAccount" });

        rows.Add(new() { Caption = "Kwota", Value = IssuingFormat.Money(transfer.Amount, transfer.Currency), ClipboardText = AmountClipboard(transfer.Amount), CopiedMessage = "Skopiowano kwotę", AutomationId = $"{page}RowCopyAmount" });

        if (!string.IsNullOrWhiteSpace(transfer.Title))
            rows.Add(new() { Caption = "Tytuł", Value = transfer.Title, ClipboardText = transfer.Title, CopiedMessage = "Skopiowano tytuł", AutomationId = $"{page}RowCopyTitle" });

        if (!string.IsNullOrWhiteSpace(transfer.RecipientNip))
            rows.Add(new() { Caption = "NIP odbiorcy", Value = transfer.RecipientNip, ClipboardText = NipClipboard(transfer.RecipientNip), CopiedMessage = "Skopiowano NIP", AutomationId = $"{page}RowCopyNip" });

        return rows;
    }

    public static string AccountClipboard(string account)
    {
        var clean = string.Concat(account.Where(c => !char.IsWhiteSpace(c) && c != '-')).ToUpperInvariant();
        var digits = clean.StartsWith("PL", StringComparison.Ordinal) ? clean[2..] : clean;
        return IsNrb(digits) ? digits : clean;
    }

    public static string AccountDisplay(string account)
    {
        var clean = AccountClipboard(account);
        return IsNrb(clean) ? $"{clean[..2]} {Groups(clean[2..])}" : Groups(clean);
    }

    public static string AmountClipboard(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    public static string NipClipboard(string nip) => string.Concat(nip.Where(char.IsAsciiDigit));

    private static string Groups(string value) => string.Join(' ', value.Chunk(4).Select(c => new string(c)));

    private static bool IsNrb(string digits)
    {
        if (digits.Length != 26 || !digits.All(char.IsAsciiDigit))
            return false;

        var remainder = 0;
        foreach (var c in digits[2..] + "2521" + digits[..2])
            remainder = (remainder * 10 + (c - '0')) % 97;
        return remainder == 1;
    }
}
