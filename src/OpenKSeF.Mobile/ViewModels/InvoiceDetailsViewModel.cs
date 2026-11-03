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
    [NotifyPropertyChangedFor(nameof(HasLines))]
    private InvoiceDto? _invoice;

    public bool HasLines => _invoice?.Lines is { Count: > 0 };

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
        if (Invoice is null || TenantId is null)
            return;

        try
        {
            var transfer = await _apiService.GetTransferDetailsAsync(Guid.Parse(TenantId), Invoice.Id);
            await Clipboard.Default.SetTextAsync(transfer.TransferText);
            await Shell.Current.DisplayAlertAsync("Skopiowano", "Dane przelewu skopiowane do schowka.", "OK");
        }
        catch (ApiException ex)
        {
            await Shell.Current.DisplayAlertAsync("Błąd", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Błąd", $"Nie udało się skopiować: {ex.Message}", "OK");
        }
    }

    [RelayCommand]
    private async Task ShowQrCodeAsync()
    {
        if (Invoice is null || TenantId is null)
            return;

        try { await Shell.Current.GoToAsync($"qrCode?tenantId={TenantId}&invoiceId={Invoice.Id}"); } catch { }
    }
}
