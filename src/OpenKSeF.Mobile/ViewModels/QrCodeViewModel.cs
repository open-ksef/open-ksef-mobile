using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

[QueryProperty(nameof(TenantId), "tenantId")]
[QueryProperty(nameof(InvoiceId), "invoiceId")]
public partial class QrCodeViewModel : ObservableObject
{
    private const string OldServerMessage = "Kod QR wymaga nowszej wersji serwera. Skopiuj dane przelewu.";

    private readonly IApiService _apiService;

    [ObservableProperty]
    private string? _tenantId;

    [ObservableProperty]
    private string? _invoiceId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTransferText))]
    private ImageSource? _qrImage;

    [ObservableProperty]
    private string? _qrMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTransferText))]
    private string? _transferText;

    public bool ShowTransferText => QrImage is null && !string.IsNullOrEmpty(TransferText);

    [ObservableProperty]
    private string? _title;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    private byte[]? _qrBytes;
    private int _loadVersion;

    public QrCodeViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    partial void OnInvoiceIdChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(TenantId))
            _ = GenerateQrAsync().ContinueWith(
                t => ErrorMessage = t.Exception?.InnerException?.Message,
                TaskContinuationOptions.OnlyOnFaulted);
    }

    partial void OnTenantIdChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(InvoiceId))
            _ = GenerateQrAsync().ContinueWith(
                t => ErrorMessage = t.Exception?.InnerException?.Message,
                TaskContinuationOptions.OnlyOnFaulted);
    }

    [RelayCommand]
    private async Task GenerateQrAsync()
    {
        if (TenantId is null || InvoiceId is null)
            return;

        var load = ++_loadVersion;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            Title = null;
            QrMessage = null;
            TransferText = null;
            QrImage = null;
            _qrBytes = null;

            var transfer = await _apiService.GetTransferDetailsAsync(
                Guid.Parse(TenantId),
                Guid.Parse(InvoiceId));

            if (load != _loadVersion)
                return;

            var money = IssuingFormat.Money(transfer.Amount, transfer.Currency);
            Title = string.IsNullOrWhiteSpace(transfer.RecipientName) ? money : $"{transfer.RecipientName} — {money}";
            TransferText = transfer.TransferText;
            QrMessage = transfer.ServerHasQrRules ? transfer.QrMessage : OldServerMessage;

            if (transfer.ServerHasQrRules && !string.IsNullOrEmpty(transfer.QrCodeBase64))
            {
                var bytes = Convert.FromBase64String(transfer.QrCodeBase64[(transfer.QrCodeBase64.IndexOf(',') + 1)..]);
                if (bytes.Length == 0)
                    throw new FormatException();

                _qrBytes = bytes;
                QrImage = ImageSource.FromStream(() => new MemoryStream(bytes));
            }
        }
        catch (ApiException ex)
        {
            if (load == _loadVersion)
                ErrorMessage = ex.Message;
        }
        catch (Exception)
        {
            if (load == _loadVersion)
                ErrorMessage = "Nie udało się wyświetlić kodu QR. Skopiuj dane przelewu albo spróbuj ponownie.";
        }
        finally
        {
            if (load == _loadVersion)
                IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CopyTransferTextAsync()
    {
        if (string.IsNullOrEmpty(TransferText))
            return;

        try
        {
            await Clipboard.Default.SetTextAsync(TransferText);
            await Shell.Current.DisplayAlertAsync("Skopiowano", "Dane przelewu skopiowane do schowka.", "OK");
        }
        catch (Exception)
        {
            await Shell.Current.DisplayAlertAsync("Błąd", "Nie udało się skopiować danych przelewu.", "OK");
        }
    }

    [RelayCommand]
    private async Task ShareQrAsync()
    {
        if (_qrBytes is null)
            return;

        try
        {
            var filePath = Path.Combine(FileSystem.CacheDirectory, "transfer-qr.png");
            await File.WriteAllBytesAsync(filePath, _qrBytes);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Kod QR przelewu",
                File = new ShareFile(filePath)
            });
        }
        catch (Exception)
        {
            await Shell.Current.DisplayAlertAsync("Błąd", "Nie udało się udostępnić kodu QR.", "OK");
        }
    }
}
