using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

public partial class OnboardingViewModel : ObservableObject
{
    private readonly IApiService _apiService;
    private readonly IDeviceTokenService _deviceTokenService;

    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    // Step 1
    [ObservableProperty]
    private string _nip = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _notificationEmail = string.Empty;

    // Step 2
    [ObservableProperty]
    private string _ksefToken = string.Empty;

    [ObservableProperty]
    private bool _tokenSkipped;

    // Step 3
    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string? _syncStatusText;

    private Guid? _tenantId;
    private string _tenantLabel = string.Empty;

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;

    public string StepTitle => CurrentStep switch
    {
        1 => "Dane firmy",
        2 => "Połącz z KSeF",
        3 => "Gotowe!",
        _ => ""
    };

    private static readonly Color ActiveColor = Color.FromArgb("#6366f1");
    private static readonly Color DoneColor = Color.FromArgb("#059669");
    private static readonly Color PendingColor = Color.FromArgb("#e5e7eb");

    public Color Step1Color => CurrentStep > 1 ? DoneColor : CurrentStep == 1 ? ActiveColor : PendingColor;
    public Color Step2Color => CurrentStep > 2 ? DoneColor : CurrentStep == 2 ? ActiveColor : PendingColor;
    public Color Step3Color => CurrentStep == 3 ? ActiveColor : PendingColor;

    public string Step1Label => CurrentStep > 1 ? "✓" : "1";
    public string Step2Label => CurrentStep > 2 ? "✓" : "2";
    public string Step3Label => "3";

    public string NextButtonText => IsBusy ? "Zapisywanie…" : "Dalej";
    public string SuccessMessage => $"Firma {_tenantLabel} (NIP: {Nip}) została skonfigurowana.";

    public OnboardingViewModel(IApiService apiService, IDeviceTokenService deviceTokenService)
    {
        _apiService = apiService;
        _deviceTokenService = deviceTokenService;
    }

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(StepTitle));
        OnPropertyChanged(nameof(Step1Color));
        OnPropertyChanged(nameof(Step2Color));
        OnPropertyChanged(nameof(Step3Color));
        OnPropertyChanged(nameof(Step1Label));
        OnPropertyChanged(nameof(Step2Label));
        OnPropertyChanged(nameof(Step3Label));
        OnPropertyChanged(nameof(SuccessMessage));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(NextButtonText));
    }

    [RelayCommand]
    private async Task SubmitStep1Async()
    {
        if (IsBusy) return;

        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Nip) || Nip.Trim().Length != 10 || !Nip.Trim().All(char.IsDigit))
        {
            ErrorMessage = "NIP musi zawierać dokładnie 10 cyfr.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(NotificationEmail) &&
            !NotificationEmail.Contains('@'))
        {
            ErrorMessage = "Adres e-mail do powiadomień jest nieprawidłowy.";
            return;
        }

        try
        {
            IsBusy = true;

            var tenant = await _apiService.CreateTenantAsync(new CreateTenantRequest
            {
                Nip = Nip.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim(),
                NotificationEmail = string.IsNullOrWhiteSpace(NotificationEmail) ? null : NotificationEmail.Trim()
            });

            _tenantId = Guid.Parse(tenant.Id.ToString());
            _tenantLabel = tenant.DisplayName ?? tenant.Nip;
            CurrentStep = 2;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Błąd tworzenia firmy: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SubmitStep2Async()
    {
        if (IsBusy || _tenantId is null) return;

        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(KsefToken))
        {
            ErrorMessage = "Token KSeF jest wymagany.";
            return;
        }

        try
        {
            IsBusy = true;

            await _apiService.AddOrUpdateCredentialAsync(_tenantId.Value, KsefToken.Trim());
            TokenSkipped = false;
            CurrentStep = 3;

            await RunSyncAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Błąd zapisywania tokenu: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SkipToken()
    {
        TokenSkipped = true;
        CurrentStep = 3;
    }

    [RelayCommand]
    private async Task GoToInvoicesAsync()
    {
        if (_tenantId.HasValue)
        {
            Preferences.Default.Set("SelectedTenantId", _tenantId.Value.ToString());
        }

        try { await _deviceTokenService.EnsureDeviceRegisteredAsync(); } catch { }

        await Shell.Current.GoToAsync("//main/invoices");
    }

    private async Task RunSyncAsync()
    {
        if (_tenantId is null) return;

        try
        {
            IsSyncing = true;
            SyncStatusText = "Trwa pierwsza synchronizacja faktur…";

            var result = await _apiService.ForceCredentialSyncAsync(_tenantId.Value);

            SyncStatusText = $"Synchronizacja zakończona — pobrano {result.FetchedInvoices} faktur" +
                (result.NewInvoices > 0 ? $", w tym {result.NewInvoices} nowych." : ".");
        }
        catch
        {
            SyncStatusText = "Synchronizacja nie powiodła się. Możesz spróbować później.";
        }
        finally
        {
            IsSyncing = false;
        }
    }
}
