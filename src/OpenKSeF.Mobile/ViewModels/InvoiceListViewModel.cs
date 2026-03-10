using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

public partial class InvoiceListViewModel : ObservableObject
{
    private readonly IApiService _apiService;
    private readonly LocalDbService _localDb;
    private int _currentPage = 1;
    private int _totalPages;
    private bool _autoSelectAttempted;

    [ObservableProperty]
    private ObservableCollection<InvoiceDto> _invoices = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _hasMorePages;

    [ObservableProperty]
    private bool _isLoadingMore;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _tenantNip;

    [ObservableProperty]
    private bool _isOfflineMode;

    [ObservableProperty]
    private bool _isNoTenantSelected;

    public InvoiceListViewModel(IApiService apiService, LocalDbService localDb)
    {
        _apiService = apiService;
        _localDb = localDb;
    }

    [RelayCommand]
    private async Task LoadInvoicesAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            IsOfflineMode = false;
            IsNoTenantSelected = false;
            _currentPage = 1;

            var tenantId = GetSelectedTenantId();
            if (tenantId is null && !_autoSelectAttempted)
            {
                _autoSelectAttempted = true;
                tenantId = await TryAutoSelectFirstTenantAsync();
            }

            if (tenantId is null)
            {
                IsNoTenantSelected = true;
                return;
            }

            TenantNip = Preferences.Default.Get("SelectedTenantNip", string.Empty);

            try
            {
                var result = await _apiService.GetInvoicesAsync(tenantId.Value, _currentPage);
                Invoices = new ObservableCollection<InvoiceDto>(result.Items);
                _totalPages = result.TotalPages;
                HasMorePages = _currentPage < _totalPages;
                IsEmpty = Invoices.Count == 0;

                if (result.Items.Count > 0)
                    await _localDb.SaveInvoicesAsync(tenantId.Value, result.Items);
            }
            catch (Exception)
            {
                if (!await TryLoadFromCacheAsync(tenantId.Value))
                    throw;
            }
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nie udało się załadować faktur: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    private async Task<bool> TryLoadFromCacheAsync(Guid tenantId)
    {
        try
        {
            var cached = await _localDb.GetInvoicesAsync(tenantId);
            if (cached.Count == 0)
                return false;

            Invoices = new ObservableCollection<InvoiceDto>(cached);
            HasMorePages = false;
            IsEmpty = false;
            IsOfflineMode = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await LoadInvoicesAsync();
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsLoadingMore || !HasMorePages)
            return;

        try
        {
            IsLoadingMore = true;
            _currentPage++;

            var tenantId = GetSelectedTenantId();
            if (tenantId is null)
                return;

            var result = await _apiService.GetInvoicesAsync(tenantId.Value, _currentPage);
            foreach (var invoice in result.Items)
                Invoices.Add(invoice);

            _totalPages = result.TotalPages;
            HasMorePages = _currentPage < _totalPages;
        }
        catch (ApiException ex)
        {
            _currentPage--; // Revert on failure
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    [RelayCommand]
    private async Task SelectInvoiceAsync(InvoiceDto invoice)
    {
        var tenantId = GetSelectedTenantId();
        if (tenantId is null)
            return;

        await Shell.Current.GoToAsync($"invoiceDetails?tenantId={tenantId}&invoiceId={invoice.Id}");
    }

    private async Task<Guid?> TryAutoSelectFirstTenantAsync()
    {
        try
        {
            var tenants = await _apiService.GetTenantsAsync();
            if (tenants.Count == 0)
                return null;

            var first = tenants[0];
            Preferences.Default.Set("SelectedTenantId", first.Id.ToString());
            Preferences.Default.Set("SelectedTenantNip", first.Nip);
            return first.Id;
        }
        catch
        {
            return null;
        }
    }

    private static Guid? GetSelectedTenantId()
    {
        var idStr = Preferences.Default.Get("SelectedTenantId", string.Empty);
        return Guid.TryParse(idStr, out var id) ? id : null;
    }
}
