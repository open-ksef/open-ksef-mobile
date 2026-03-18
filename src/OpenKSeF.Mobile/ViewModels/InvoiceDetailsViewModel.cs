using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

[QueryProperty(nameof(TenantId), "tenantId")]
[QueryProperty(nameof(InvoiceId), "invoiceId")]
public partial class InvoiceDetailsViewModel : ObservableObject
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    private string? _tenantId;

    [ObservableProperty]
    private string? _invoiceId;

    [ObservableProperty]
    private InvoiceDto? _invoice;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public InvoiceDetailsViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    partial void OnInvoiceIdChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(TenantId))
            _ = LoadInvoiceAsync().ContinueWith(
                t => ErrorMessage = t.Exception?.InnerException?.Message,
                TaskContinuationOptions.OnlyOnFaulted);
    }

    partial void OnTenantIdChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(InvoiceId))
            _ = LoadInvoiceAsync().ContinueWith(
                t => ErrorMessage = t.Exception?.InnerException?.Message,
                TaskContinuationOptions.OnlyOnFaulted);
    }

    [RelayCommand]
    private async Task LoadInvoiceAsync()
    {
        if (IsBusy || TenantId is null || InvoiceId is null)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            Invoice = await _apiService.GetInvoiceDetailsAsync(
                Guid.Parse(TenantId),
                Guid.Parse(InvoiceId));
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nie udało się załadować faktury: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CopyTransferDetailsAsync()
    {
        if (Invoice is null)
            return;

        try
        {
            var details = BuildTransferDetails(Invoice);
            await Clipboard.Default.SetTextAsync(details);
            await Shell.Current.DisplayAlertAsync("Skopiowano", "Dane przelewu skopiowane do schowka.", "OK");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nie udało się skopiować: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ShowQrCodeAsync()
    {
        if (Invoice is null || TenantId is null)
            return;

        try { await Shell.Current.GoToAsync($"qrCode?tenantId={TenantId}&invoiceId={Invoice.Id}"); } catch { }
    }

    internal static string BuildTransferDetails(InvoiceDto invoice)
    {
        return $"Odbiorca: {invoice.VendorName}\n" +
               $"NIP: {invoice.VendorNip}\n" +
               $"Kwota: {invoice.AmountGross:N2} {invoice.Currency}\n" +
               $"Tytul: Faktura {invoice.KSeFInvoiceNumber}";
    }
}
