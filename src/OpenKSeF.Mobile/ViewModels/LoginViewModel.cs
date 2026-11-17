using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IServerSettingsService _serverSettings;
    private readonly IPostLoginNavigationService _postLoginNav;
    private readonly IApiService _api;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isRestoringSession = true;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _serverUrl;

    [ObservableProperty]
    private bool _isServerVisible;

    [ObservableProperty]
    private bool _isRegistrationAllowed;

    public string ActiveServerHost => ServerUrlRules.DisplayHost(_serverSettings.ServerUrl);

    public bool HasActiveServerHost => ActiveServerHost.Length > 0;

    public string LoginButtonText => IsBusy ? "Logowanie..." : "Zaloguj się";

    public LoginViewModel(IAuthService authService, IServerSettingsService serverSettings, IPostLoginNavigationService postLoginNav, IApiService api)
    {
        _authService = authService;
        _serverSettings = serverSettings;
        _postLoginNav = postLoginNav;
        _api = api;
        _serverUrl = serverSettings.ServerUrl;
        _isServerVisible = !serverSettings.IsConfigured;
    }

    public void RefreshServerSettings()
    {
        ServerUrl = _serverSettings.ServerUrl;
        IsServerVisible = !_serverSettings.IsConfigured;
        OnPropertyChanged(nameof(ActiveServerHost));
        OnPropertyChanged(nameof(HasActiveServerHost));
    }

    public async Task LoadRegistrationAllowedAsync()
    {
        var serverUrl = _serverSettings.ServerUrl;
        bool allowed;
        try
        {
            allowed = serverUrl.Length > 0 && (await _api.GetSetupStatusAsync())?.RegistrationAllowed == true;
        }
        catch
        {
            allowed = false;
        }

        if (serverUrl == _serverSettings.ServerUrl)
            IsRegistrationAllowed = allowed;
    }

    public async Task RestoreSessionAsync()
    {
        try
        {
            if (!IsBusy)
                await _postLoginNav.RestoreSessionAsync();
        }
        finally
        {
            IsRestoringSession = false;
        }
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(LoginButtonText));

    [RelayCommand]
    private void ToggleServer()
    {
        IsServerVisible = !IsServerVisible;
        if (!IsServerVisible)
            ServerUrl = _serverSettings.ServerUrl;
    }

    [RelayCommand]
    private Task LoginAsync() =>
        RunBrowserLoginAsync(() => _authService.LoginInBrowserAsync(), "Logowanie nie powiodło się.");

    [RelayCommand]
    private Task RegisterAsync() =>
        RunBrowserLoginAsync(() => _authService.LoginInBrowserAsync(register: true), "Rejestracja nie powiodła się.");

    [RelayCommand]
    private Task LoginWithGoogleAsync() =>
        RunBrowserLoginAsync(_authService.LoginWithGoogleAsync, "Logowanie przez Google nie powiodło się.");

    [RelayCommand]
    private async Task PairAsync()
    {
        if (IsBusy) return;

        ErrorMessage = null;
        if (!EnsureServerConfigured())
            return;

        try { await Shell.Current.GoToAsync("pairing"); } catch { }
    }

    private async Task RunBrowserLoginAsync(Func<Task<AuthResult>> login, string rejectedMessage)
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            if (!EnsureServerConfigured())
                return;

            var result = await login();

            if (result == AuthResult.Success)
                await _postLoginNav.NavigateAsync();
            else if (result == AuthResult.ServerUnreachable)
                ShowServerUnreachable();
            else if (result == AuthResult.ClientRejected)
                ShowClientRejected();
            else if (result == AuthResult.GoogleRequiresHttps)
                ErrorMessage = "Logowanie przez Google wymaga serwera z adresem https://. Użyj przycisku Zaloguj się.";
            else if (result != AuthResult.Cancelled)
                ErrorMessage = rejectedMessage;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Błąd logowania: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveServerAsync()
    {
        ErrorMessage = null;
        if (EnsureServerConfigured())
            await LoadRegistrationAllowedAsync();
    }

    [RelayCommand]
    private async Task ScanQrAsync()
    {
        try { await Shell.Current.GoToAsync("scanSetupQr"); } catch { }
    }

    private void ShowServerUnreachable()
    {
        ErrorMessage = $"Nie można połączyć się z serwerem {ActiveServerHost}. Sprawdź adres serwera i połączenie z siecią.";
        IsServerVisible = true;
    }

    private void ShowClientRejected()
    {
        ErrorMessage = $"Serwer {ActiveServerHost} odrzucił aplikację mobilną. Skontaktuj się z administratorem serwera.";
        IsServerVisible = true;
    }

    private bool EnsureServerConfigured()
    {
        if (!_serverSettings.TryUpdateServerUrl(ServerUrl, out var normalizedUrl, out var validationError))
        {
            ErrorMessage = validationError;
            IsServerVisible = true;
            return false;
        }

        ServerUrl = normalizedUrl;
        OnPropertyChanged(nameof(ActiveServerHost));
        OnPropertyChanged(nameof(HasActiveServerHost));
        return true;
    }
}
