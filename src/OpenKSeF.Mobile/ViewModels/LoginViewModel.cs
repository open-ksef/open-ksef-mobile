using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    /// <summary>Off: in-app email/password form (ROPC). On: system-browser login, browser registration and "Sparuj z portalem" (T-137/T-138), kept for a later release.</summary>
    public const bool BrowserLoginEnabled = false;

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
    private bool _isBrowserLogin = BrowserLoginEnabled;

    [ObservableProperty]
    private bool _isLoginMode = true;

    [ObservableProperty]
    private bool _isServerVisible;

    [ObservableProperty]
    private bool _isRegistrationAllowed;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _regFirstName = string.Empty;

    [ObservableProperty]
    private string _regLastName = string.Empty;

    [ObservableProperty]
    private string _regEmail = string.Empty;

    [ObservableProperty]
    private string _regPassword = string.Empty;

    [ObservableProperty]
    private string _regConfirmPassword = string.Empty;

    public string SubtitleText => IsLoginMode
        ? "Zaloguj się do portalu OpenKSeF"
        : "Utwórz nowe konto";

    public string ActiveServerHost => ServerUrlRules.DisplayHost(_serverSettings.ServerUrl);

    public bool HasActiveServerHost => ActiveServerHost.Length > 0;

    public string LoginButtonText => IsBusy ? "Logowanie..." : "Zaloguj się";
    public string RegisterButtonText => IsBusy ? "Tworzenie konta..." : "Utwórz konto";
    public string SwitchPromptText => IsLoginMode ? "Nie masz konta?" : "Masz już konto?";
    public string SwitchLinkText => IsLoginMode ? "Zarejestruj się" : "Zaloguj się";
    public string SwitchButtonAutomationId => IsLoginMode
        ? "LoginPageButtonSwitchToRegister"
        : "LoginPageButtonSwitchToLogin";

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

    partial void OnIsRegistrationAllowedChanged(bool value)
    {
        if (!value)
            IsLoginMode = true;
    }

    partial void OnIsLoginModeChanged(bool value)
    {
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(SwitchPromptText));
        OnPropertyChanged(nameof(SwitchLinkText));
        OnPropertyChanged(nameof(SwitchButtonAutomationId));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(LoginButtonText));
        OnPropertyChanged(nameof(RegisterButtonText));
    }

    [RelayCommand]
    private void ToggleMode()
    {
        IsLoginMode = !IsLoginMode;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void ToggleServer()
    {
        IsServerVisible = !IsServerVisible;
        if (!IsServerVisible)
            ServerUrl = _serverSettings.ServerUrl;
    }

    [RelayCommand]
    private async Task LoginWithCredentialsAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            if (!EnsureServerConfigured())
                return;

            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Podaj email i hasło.";
                return;
            }

            var result = await _authService.LoginWithCredentialsAsync(Email.Trim(), Password);

            if (result == AuthResult.Success)
                await _postLoginNav.NavigateAsync();
            else if (result == AuthResult.ServerUnreachable)
                ShowServerUnreachable();
            else if (result == AuthResult.ClientRejected)
                ShowClientRejected();
            else if (result == AuthResult.AccountNotSetUp)
                ErrorMessage = "Konto wymaga dokończenia konfiguracji. Potwierdź adres e-mail linkiem z wiadomości albo zaloguj się raz w portalu i wykonaj wskazane kroki, potem zaloguj się ponownie.";
            else if (result == AuthResult.AccountDisabled)
                ErrorMessage = "Konto jest wyłączone. Skontaktuj się z administratorem serwera.";
            else
                ErrorMessage = "Nieprawidłowy email lub hasło.";
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
    private async Task RegisterAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            if (!EnsureServerConfigured())
                return;

            if (string.IsNullOrWhiteSpace(RegEmail) || string.IsNullOrWhiteSpace(RegPassword))
            {
                ErrorMessage = "Podaj email i hasło.";
                return;
            }

            if (RegPassword.Length < 8)
            {
                ErrorMessage = "Hasło musi mieć co najmniej 8 znaków.";
                return;
            }

            if (RegPassword != RegConfirmPassword)
            {
                ErrorMessage = "Hasła nie są identyczne.";
                return;
            }

            var result = await _authService.RegisterAsync(
                RegEmail.Trim(),
                RegPassword,
                string.IsNullOrWhiteSpace(RegFirstName) ? null : RegFirstName.Trim(),
                string.IsNullOrWhiteSpace(RegLastName) ? null : RegLastName.Trim());

            if (result == AuthResult.Success)
                await _postLoginNav.NavigateAsync();
            else if (result == AuthResult.ServerUnreachable)
                ShowServerUnreachable();
            else
            {
                ErrorMessage = "Rejestracja nie powiodła się. Sprawdź adres serwera.";
                IsServerVisible = true;
            }
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
            if (ex.Message.Contains("Konto zostało utworzone"))
            {
                Email = RegEmail;
                Password = string.Empty;
                IsLoginMode = true;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Błąd rejestracji: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task LoginInBrowserAsync() =>
        RunBrowserLoginAsync(() => _authService.LoginInBrowserAsync(), "Logowanie nie powiodło się.");

    [RelayCommand]
    private Task RegisterInBrowserAsync() =>
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
                ErrorMessage = IsBrowserLogin
                    ? "Logowanie przez Google wymaga serwera z adresem https://. Użyj przycisku Zaloguj się."
                    : "Logowanie przez Google wymaga serwera z adresem https://. Zaloguj się emailem i hasłem.";
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
