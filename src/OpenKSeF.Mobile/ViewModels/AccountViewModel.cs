using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

public partial class AccountViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IServerSettingsService _serverSettings;
    private readonly IDeviceTokenService _deviceTokenService;
    private readonly INotificationHubService _notificationHub;

    [ObservableProperty]
    private string _serverUrl = "";

    [ObservableProperty]
    private string _appVersion = "";

    [ObservableProperty]
    private bool _notificationsEnabled;

    [ObservableProperty]
    private string _notificationStatusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public AccountViewModel(
        IAuthService authService,
        IServerSettingsService serverSettings,
        IDeviceTokenService deviceTokenService,
        INotificationHubService notificationHub)
    {
        _authService = authService;
        _serverSettings = serverSettings;
        _deviceTokenService = deviceTokenService;
        _notificationHub = notificationHub;
        ServerUrl = _serverSettings.ServerUrl;
        AppVersion = $"v{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
    }

    [RelayCommand]
    public async Task LoadSettingsAsync()
    {
        try
        {
            NotificationsEnabled = await _deviceTokenService.AreNotificationsEnabledAsync();

            var hubStatus = _notificationHub.IsConnected ? " (SignalR połączony)" : "";
            NotificationStatusText = NotificationsEnabled
                ? $"Powiadomienia push są włączone.{hubStatus}"
                : "Powiadomienia push są wyłączone.";
        }
        catch
        {
            NotificationStatusText = "Nie udało się sprawdzić statusu powiadomień.";
        }
    }

    [RelayCommand]
    public async Task ToggleNotificationsAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;

            var alreadyEnabled = await _deviceTokenService.AreNotificationsEnabledAsync();

            if (alreadyEnabled)
            {
                NotificationsEnabled = true;
                NotificationStatusText = "Aby wyłączyć powiadomienia, zmień ustawienia w systemie.";
                return;
            }

            var granted = await _deviceTokenService.EnableNotificationsAsync();

            NotificationsEnabled = granted;
            NotificationStatusText = granted
                ? "Powiadomienia push są włączone."
                : "Brak uprawnień. Włącz powiadomienia w ustawieniach systemu.";
        }
        catch
        {
            NotificationsEnabled = false;
            NotificationStatusText = "Wystąpił błąd podczas włączania powiadomień.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        bool confirmed = await Shell.Current.DisplayAlertAsync(
            "Wyloguj", "Czy na pewno chcesz się wylogować?", "Wyloguj", "Anuluj");

        if (!confirmed)
            return;

        try { await _notificationHub.StopAsync(); } catch { }
        await _authService.LogoutAsync();
        await Shell.Current.GoToAsync("//login");
    }

    [RelayCommand]
    private async Task ChangeServerAsync()
    {
        await Shell.Current.GoToAsync("//login");
    }
}
