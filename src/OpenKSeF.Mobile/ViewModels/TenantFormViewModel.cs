using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

[QueryProperty(nameof(TenantId), "tenantId")]
public partial class TenantFormViewModel : ObservableObject
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    private string? _tenantId;

    [ObservableProperty]
    private string _nip = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string? _nipError;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isEditMode;

    public string Title => IsEditMode ? "Edytuj firmę" : "Dodaj firmę";

    public TenantFormViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    partial void OnTenantIdChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            IsEditMode = true;
            OnPropertyChanged(nameof(Title));
            _ = LoadTenantAsync(Guid.Parse(value));
        }
    }

    private async Task LoadTenantAsync(Guid id)
    {
        try
        {
            IsBusy = true;
            var tenants = await _apiService.GetTenantsAsync();
            var tenant = tenants.FirstOrDefault(t => t.Id == id);
            if (tenant is not null)
            {
                Nip = tenant.Nip;
                DisplayName = tenant.DisplayName ?? string.Empty;
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

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
            return;

        NipError = null;
        ErrorMessage = null;

        if (!ValidateNip())
            return;

        try
        {
            IsBusy = true;

            if (IsEditMode && TenantId is not null)
            {
                await _apiService.UpdateTenantAsync(
                    Guid.Parse(TenantId),
                    new UpdateTenantRequest { DisplayName = DisplayName });
            }
            else
            {
                await _apiService.CreateTenantAsync(
                    new CreateTenantRequest
                    {
                        Nip = Nip,
                        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName
                    });
            }

            await Shell.Current.GoToAsync("..");
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

    [RelayCommand]
    private async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..");
    }

    private bool ValidateNip()
    {
        if (string.IsNullOrWhiteSpace(Nip))
        {
            NipError = "NIP jest wymagany.";
            return false;
        }

        if (Nip.Length != 10 || !Nip.All(char.IsDigit))
        {
            NipError = "NIP musi składać się z 10 cyfr.";
            return false;
        }

        if (!IsValidNipChecksum(Nip))
        {
            NipError = "Nieprawidlowa suma kontrolna NIP.";
            return false;
        }

        return true;
    }

    private static bool IsValidNipChecksum(string nip)
    {
        int[] weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
        var sum = 0;
        for (var i = 0; i < 9; i++)
            sum += (nip[i] - '0') * weights[i];

        return sum % 11 == nip[9] - '0';
    }
}
