using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

public sealed record IssuedInvoiceRow(IssuedInvoiceListItemDto Invoice)
{
    public string Buyer => Invoice.BuyerName ?? "—";
    public string Gross => IssuingFormat.Money(Invoice.TotalGross, Invoice.Currency);
    public string Subtitle => $"{Invoice.Number ?? "— (szkic)"} · {IssuingFormat.Date(Invoice.IssueDate)}";
    public string Status => Invoice.Status;

    public string? ForeignText => Invoice.BuyerCountryCode is not { Length: > 0 } country ? null
        : Invoice.Status == "Accepted" || IssuingFormat.IsSending(Invoice.Status)
            ? $"{country} · {(Invoice.BuyerIdType == "VatUe" ? "np II" : "np I")} · VAT {IssuingFormat.Money(0, Invoice.Currency)}"
            : country;
}

public sealed record StatusFilter(string Label, string Value);

public partial class IssuedInvoiceListViewModel : ObservableObject, IQueryAttributable
{
    private const int PageSize = 20;
    private readonly IApiService _apiService;
    private int _currentPage = 1;
    private int _loadVersion;
    private string? _loadedStatus;
    private string? _loadedSearch;
    private bool _statusChangedDuringLoad;

    public IReadOnlyList<StatusFilter> Filters { get; } =
    [
        new("Wszystkie", ""), new("Szkice", "Draft"), new("Wysyłane", "Sending"), new("Przyjęte", "Accepted"), new("Odrzucone", "Rejected")
    ];

    [ObservableProperty]
    private ObservableCollection<IssuedInvoiceRow> _invoices = [];

    [ObservableProperty]
    private string _selectedStatus = "";

    [ObservableProperty]
    private string? _search;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _isLoadingMore;

    [ObservableProperty]
    private bool _hasMorePages;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanIssue))]
    private bool _isUnsupported;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanIssue))]
    private bool _isNoTenantSelected;

    [ObservableProperty]
    private string? _environment;

    [ObservableProperty]
    private string? _errorMessage;

    public bool CanIssue => !IsUnsupported && !IsNoTenantSelected;

    public IssuedInvoiceListViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    partial void OnSelectedStatusChanged(string value)
    {
        if (IsBusy)
            _statusChangedDuringLoad = true;
        else
            _ = LoadAsync();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("status", out var status) && status is string value && Filters.Any(f => f.Value == value))
            SelectedStatus = value;
        query.Clear();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            _loadVersion++;
            ErrorMessage = null;
            IsNoTenantSelected = false;
            IsEmpty = false;

            if (SelectedTenantId() is not { } tenantId)
            {
                IsNoTenantSelected = true;
                IsUnsupported = false;
                Invoices = [];
                return;
            }

            var profile = await _apiService.GetSellerProfileAsync(tenantId);
            IsUnsupported = profile is null;
            if (profile is null)
            {
                Invoices = [];
                IsEmpty = false;
                return;
            }

            Environment = profile.KsefEnvironment;
            _currentPage = 1;
            _loadedStatus = Status();
            _loadedSearch = Search;
            var result = await _apiService.GetIssuedInvoicesAsync(tenantId, _currentPage, PageSize, _loadedStatus, q: _loadedSearch);
            Invoices = new ObservableCollection<IssuedInvoiceRow>(result.Items.Select(i => new IssuedInvoiceRow(i)));
            HasMorePages = _currentPage < result.TotalPages;
            IsEmpty = Invoices.Count == 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nie udało się załadować faktur: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
            if (_statusChangedDuringLoad)
            {
                _statusChangedDuringLoad = false;
                if (Status() != _loadedStatus)
                    await LoadAsync();
            }
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsLoadingMore || IsBusy || !HasMorePages || SelectedTenantId() is not { } tenantId)
            return;

        var load = _loadVersion;
        try
        {
            IsLoadingMore = true;
            var result = await _apiService.GetIssuedInvoicesAsync(tenantId, _currentPage + 1, PageSize, _loadedStatus, q: _loadedSearch);
            if (load != _loadVersion || tenantId != SelectedTenantId())
                return;

            _currentPage++;
            foreach (var item in result.Items)
                Invoices.Add(new IssuedInvoiceRow(item));
            HasMorePages = _currentPage < result.TotalPages;
            ErrorMessage = null;
        }
        catch (Exception ex) when (load == _loadVersion)
        {
            ErrorMessage = ex.Message;
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
    private async Task OpenAsync(IssuedInvoiceRow row)
    {
        try { await Shell.Current.GoToAsync($"issuedInvoiceDetails?tenantId={SelectedTenantId()}&invoiceId={row.Invoice.Id}"); } catch { }
    }

    [RelayCommand]
    private async Task NewInvoiceAsync()
    {
        if (SelectedTenantId() is not { } tenantId)
            return;

        try
        {
            var profile = await _apiService.GetSellerProfileAsync(tenantId);
            if (profile is null)
            {
                IsUnsupported = true;
                return;
            }

            if (!profile.IsComplete)
            {
                await Shell.Current.DisplayAlertAsync("Dane do faktur", SellerProfileViewModel.IncompleteMessage, "OK");
                await Shell.Current.GoToAsync($"sellerProfile?tenantId={tenantId}");
                return;
            }

            await Shell.Current.GoToAsync($"invoiceDraft?tenantId={tenantId}");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task OpenSellerProfileAsync()
    {
        if (SelectedTenantId() is { } tenantId)
            try { await Shell.Current.GoToAsync($"sellerProfile?tenantId={tenantId}"); } catch { }
    }

    private string? Status() => SelectedStatus.Length == 0 ? null : SelectedStatus;

    private static Guid? SelectedTenantId() =>
        Guid.TryParse(Preferences.Default.Get("SelectedTenantId", string.Empty), out var id) ? id : null;
}
