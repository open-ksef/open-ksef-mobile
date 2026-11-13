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
    private int _loadVersion;
    private bool? _loadedIsPaid;
    private bool _filterChangedDuringLoad;

    public IReadOnlyList<StatusFilter> Filters { get; } = [new("Wszystkie", ""), new("Nieopłacone", "unpaid")];

    [ObservableProperty]
    private string _selectedFilter = "";

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

    partial void OnSelectedFilterChanged(string value)
    {
        if (IsBusy)
            _filterChangedDuringLoad = true;
        else
            _ = LoadInvoicesAsync();
    }

    private bool? IsPaidFilter() => SelectedFilter == "unpaid" ? false : null;

    [RelayCommand]
    private async Task LoadInvoicesAsync()
    {
        if (IsBusy)
            return;

        (Guid? TenantId, Guid InvoiceId)? openInvoice = null;
        try
        {
            IsBusy = true;
            _loadVersion++;
            ErrorMessage = null;
            IsOfflineMode = false;
            IsNoTenantSelected = false;
            IsEmpty = false;
            _currentPage = 1;

            var tapped = NotificationHubService.TakeTapped();
            var tappedTenant = tapped is null ? null : await OwnedTenantAsync(tapped.Value.TenantId);
            if (tapped?.InvoiceId is { } tappedInvoice)
            {
                if (tappedTenant is { } owner && !await InvoiceAvailableAsync(owner.Id, tappedInvoice))
                    tappedTenant = null;
                openInvoice = (tappedTenant?.Id, tappedInvoice);
            }

            if (tappedTenant is { } selected)
            {
                Preferences.Default.Set("SelectedTenantId", selected.Id.ToString());
                Preferences.Default.Set("SelectedTenantNip", selected.Nip);
            }

            var tenantId = tappedTenant?.Id ?? GetSelectedTenantId();
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

            _loadedIsPaid = IsPaidFilter();
            try
            {
                var result = await _apiService.GetInvoicesAsync(tenantId.Value, _currentPage, isPaid: _loadedIsPaid);
                Invoices = new ObservableCollection<InvoiceDto>(result.Items);
                _totalPages = result.TotalPages;
                HasMorePages = _currentPage < _totalPages;
                IsEmpty = Invoices.Count == 0;

                if (result.Items.Count > 0)
                    await _localDb.SaveInvoicesAsync(tenantId.Value, result.Items);
            }
            catch (Exception ex) when (ApiException.IsNetworkFailure(ex))
            {
                if (!await TryLoadFromCacheAsync(tenantId.Value, _loadedIsPaid))
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
            if (openInvoice is { } open && !NotificationHubService.HasTappedTenant)
                await OpenTappedInvoiceAsync(open.TenantId, open.InvoiceId);
            if (NotificationHubService.HasTappedTenant)
                await LoadInvoicesAsync();
            else if (_filterChangedDuringLoad)
            {
                _filterChangedDuringLoad = false;
                if (IsPaidFilter() != _loadedIsPaid)
                    await LoadInvoicesAsync();
            }
        }
    }

    private async Task<(Guid Id, string Nip)?> OwnedTenantAsync(Guid tapped)
    {
        if (tapped == GetSelectedTenantId())
            return (tapped, Preferences.Default.Get("SelectedTenantNip", string.Empty));

        try
        {
            var tenant = (await _apiService.GetTenantsAsync()).FirstOrDefault(t => t.Id == tapped);
            return tenant is null ? null : (tenant.Id, tenant.Nip);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> InvoiceAvailableAsync(Guid tenantId, Guid invoiceId)
    {
        try
        {
            await _apiService.GetInvoiceDetailsAsync(tenantId, invoiceId);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[OpenKSeF] Notification tap invoice pre-check failed: {(ex is ApiException api ? $"HTTP {(int)api.StatusCode}" : ex.GetType().Name)}");
            return false;
        }
    }

    private async Task OpenTappedInvoiceAsync(Guid? tenantId, Guid invoiceId)
    {
        if (tenantId is not null)
        {
            try { await Shell.Current.GoToAsync($"invoiceDetails?tenantId={tenantId}&invoiceId={invoiceId}"); } catch { }
            return;
        }

        await Shell.Current.DisplayAlertAsync("Faktura niedostępna", "Nie udało się otworzyć faktury z powiadomienia.", "OK");
    }

    private async Task<bool> TryLoadFromCacheAsync(Guid tenantId, bool? isPaid)
    {
        try
        {
            var cached = await _localDb.GetInvoicesAsync(tenantId, isPaid);
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
        if (IsLoadingMore || IsBusy || !HasMorePages)
            return;

        var load = _loadVersion;
        try
        {
            IsLoadingMore = true;

            var tenantId = GetSelectedTenantId();
            if (tenantId is null)
                return;

            var result = await _apiService.GetInvoicesAsync(tenantId.Value, _currentPage + 1, isPaid: _loadedIsPaid);
            if (load != _loadVersion || tenantId != GetSelectedTenantId())
                return;

            _currentPage++;
            foreach (var invoice in result.Items)
                Invoices.Add(invoice);

            _totalPages = result.TotalPages;
            HasMorePages = _currentPage < _totalPages;
            ErrorMessage = null;
        }
        catch (ApiException ex) when (load == _loadVersion)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (load == _loadVersion)
        {
            ErrorMessage = $"Nie udało się załadować faktur: {ex.Message}";
        }
        catch
        {
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

        try { await Shell.Current.GoToAsync($"invoiceDetails?tenantId={tenantId}&invoiceId={invoice.Id}"); } catch { }
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
