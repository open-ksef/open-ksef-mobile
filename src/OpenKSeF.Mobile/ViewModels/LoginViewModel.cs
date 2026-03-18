using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IServerSettingsService _serverSettings;
    private readonly IPostLoginNavigationService _postLoginNav;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _serverUrl;

    [ObservableProperty]
    private bool _isLoginMode = true;

    [ObservableProperty]
    private bool _isServerVisible;

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

    public string LoginButtonText => IsBusy ? "Logowanie..." : "Zaloguj się";
    public string RegisterButtonText => IsBusy ? "Tworzenie konta..." : "Utwórz konto";
    public string SwitchPromptText => IsLoginMode ? "Nie masz konta?" : "Masz już konto?";
    public string SwitchLinkText => IsLoginMode ? "Zarejestruj się" : "Zaloguj się";
    public string SwitchButtonAutomationId => IsLoginMode
        ? "LoginPageButtonSwitchToRegister"
        : "LoginPageButtonSwitchToLogin";

    public LoginViewModel(IAuthService authService, IServerSettingsService serverSettings, IPostLoginNavigationService postLoginNav)
    {
        _authService = authService;
        _serverSettings = serverSettings;
        _postLoginNav = postLoginNav;
        _serverUrl = serverSettings.ServerUrl;
        _isServerVisible = !serverSettings.IsConfigured;
    }

    public void RefreshServerSettings()
    {
        ServerUrl = _serverSettings.ServerUrl;
        IsServerVisible = !_serverSettings.IsConfigured;
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
    }

    [RelayCommand]
    private async Task LoginWithCredentialsAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            EnsureServerConfigured();

            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Podaj email i hasło.";
                return;
            }

            var success = await _authService.LoginWithCredentialsAsync(Email.Trim(), Password);

            if (success)
                await _postLoginNav.NavigateAsync();
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
    private async Task LoginWithGoogleAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            EnsureServerConfigured();

            var success = await _authService.LoginWithGoogleAsync();

            if (success)
                await _postLoginNav.NavigateAsync();
            else
                ErrorMessage = "Logowanie przez Google nie powiodło się.";
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

            EnsureServerConfigured();

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

            var success = await _authService.RegisterAsync(
                RegEmail.Trim(),
                RegPassword,
                string.IsNullOrWhiteSpace(RegFirstName) ? null : RegFirstName.Trim(),
                string.IsNullOrWhiteSpace(RegLastName) ? null : RegLastName.Trim());

            if (success)
                await _postLoginNav.NavigateAsync();
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
    private void SaveServer()
    {
        ErrorMessage = null;

        if (!_serverSettings.TryUpdateServerUrl(ServerUrl, out var normalizedUrl, out var validationError))
        {
            ErrorMessage = validationError;
            return;
        }

        ServerUrl = normalizedUrl;
    }

    [RelayCommand]
    private async Task ScanQrAsync()
    {
        try { await Shell.Current.GoToAsync("scanSetupQr"); } catch { }
    }

    private void EnsureServerConfigured()
    {
        if (!_serverSettings.TryUpdateServerUrl(ServerUrl, out var normalizedUrl, out var validationError))
        {
            throw new InvalidOperationException(validationError);
        }

        ServerUrl = normalizedUrl;
    }
}
