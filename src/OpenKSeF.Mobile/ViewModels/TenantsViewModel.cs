using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

public partial class TenantsViewModel : ObservableObject
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    private ObservableCollection<TenantDto> _tenants = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string? _errorMessage;

    public TenantsViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    [RelayCommand]
    private async Task LoadTenantsAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            var result = await _apiService.GetTenantsAsync();
            Tenants = new ObservableCollection<TenantDto>(result);
            IsEmpty = Tenants.Count == 0;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nie udało się załadować NIP-ów: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SelectTenantAsync(TenantDto tenant)
    {
        Preferences.Default.Set("SelectedTenantId", tenant.Id.ToString());
        Preferences.Default.Set("SelectedTenantNip", tenant.Nip);
        try { await Shell.Current.GoToAsync("//main/invoices"); } catch { }
    }

    [RelayCommand]
    private async Task AddTenantAsync()
    {
        try { await Shell.Current.GoToAsync("tenantForm"); } catch { }
    }

    [RelayCommand]
    private async Task EditTenantAsync(TenantDto tenant)
    {
        try { await Shell.Current.GoToAsync($"tenantForm?tenantId={tenant.Id}"); } catch { }
    }

    [RelayCommand]
    private async Task DeleteTenantAsync(TenantDto tenant)
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            await _apiService.DeleteTenantAsync(tenant.Id);
            Tenants.Remove(tenant);
            IsEmpty = Tenants.Count == 0;

            var selectedId = Preferences.Default.Get("SelectedTenantId", string.Empty);
            if (selectedId == tenant.Id.ToString())
            {
                Preferences.Default.Remove("SelectedTenantId");
                Preferences.Default.Remove("SelectedTenantNip");
            }
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
