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
}

public sealed record StatusFilter(string Label, string Value);

public partial class IssuedInvoiceListViewModel : ObservableObject
{
    private const int PageSize = 20;
    private readonly IApiService _apiService;
    private int _currentPage = 1;

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

    partial void OnSelectedStatusChanged(string value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            IsNoTenantSelected = false;

            if (SelectedTenantId() is not { } tenantId)
            {
                IsNoTenantSelected = true;
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
            var result = await _apiService.GetIssuedInvoicesAsync(tenantId, _currentPage, PageSize, Status(), q: Search);
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
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsLoadingMore || !HasMorePages || SelectedTenantId() is not { } tenantId)
            return;

        try
        {
            IsLoadingMore = true;
            var result = await _apiService.GetIssuedInvoicesAsync(tenantId, _currentPage + 1, PageSize, Status(), q: Search);
            _currentPage++;
            foreach (var item in result.Items)
                Invoices.Add(new IssuedInvoiceRow(item));
            HasMorePages = _currentPage < result.TotalPages;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
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
