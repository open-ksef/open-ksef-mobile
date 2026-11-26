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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTransfer))]
    private IReadOnlyList<TransferFieldRow> _transferRows = [];

    public bool HasTransfer => TransferRows.Count > 0;

    [ObservableProperty]
    private string? _transferError;

    public InvoiceDetailsViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    partial void OnInvoiceIdChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(TenantId))
            _ = LoadInvoiceAsync();
    }

    partial void OnTenantIdChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(InvoiceId))
            _ = LoadInvoiceAsync();
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

        await LoadTransferAsync();
    }

    [RelayCommand]
    private async Task LoadTransferAsync()
    {
        if (Invoice is not { } invoice || TenantId is null)
            return;

        try
        {
            TransferError = null;
            var transfer = await _apiService.GetTransferDetailsAsync(Guid.Parse(TenantId), invoice.Id);
            var rows = TransferFieldRow.From(transfer, "InvoiceDetailsPage").ToList();
            if (invoice.DueDate is not null)
                rows.Add(new TransferFieldRow { Caption = "Termin płatności", Value = invoice.DueText, AutomationId = "InvoiceDetailsPageRowDue" });
            TransferRows = rows;
        }
        catch (Exception)
        {
            TransferRows = [];
            TransferError = "Nie udało się pobrać danych przelewu.";
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
            await CopyFeedback.ShowAsync("Skopiowano dane przelewu");
        }
        catch (ApiException ex)
        {
            await Shell.Current.DisplayAlertAsync("Błąd", ex.Message, "OK");
        }
        catch (Exception)
        {
            await Shell.Current.DisplayAlertAsync("Błąd", "Nie udało się skopiować danych przelewu.", "OK");
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
