using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

[QueryProperty(nameof(TenantId), "tenantId")]
[QueryProperty(nameof(InvoiceId), "invoiceId")]
public partial class QrCodeViewModel : ObservableObject
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    private string? _tenantId;

    [ObservableProperty]
    private string? _invoiceId;

    [ObservableProperty]
    private ImageSource? _qrImage;

    [ObservableProperty]
    private string? _title;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    private byte[]? _qrBytes;

    public QrCodeViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    partial void OnInvoiceIdChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(TenantId))
            _ = GenerateQrAsync();
    }

    partial void OnTenantIdChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(InvoiceId))
            _ = GenerateQrAsync();
    }

    [RelayCommand]
    private async Task GenerateQrAsync()
    {
        if (IsBusy || TenantId is null || InvoiceId is null)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            var invoice = await _apiService.GetInvoiceDetailsAsync(
                Guid.Parse(TenantId),
                Guid.Parse(InvoiceId));

            Title = $"{invoice.VendorName} — {invoice.AmountGross:N2} {invoice.Currency}";

            _qrBytes = TransferQrGenerator.Generate(invoice);
            QrImage = ImageSource.FromStream(() => new MemoryStream(_qrBytes));
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nie udało się wygenerować kodu QR: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
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
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Błąd", $"Nie udało się udostępnić: {ex.Message}", "OK");
        }
    }
}
