using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

public partial class AccountViewModel : ObservableObject
{
    private const string PushOffText = "Powiadomienia push są wyłączone.";
    private const string PushOffPendingText =
        "Powiadomienia wyłączone, ale serwer jeszcze o tym nie wie. Powiadomienie może dotrzeć, dopóki nie uruchomisz aplikacji ponownie z dostępem do sieci.";

    private readonly IServerSettingsService _serverSettings;
    private readonly IDeviceTokenService _deviceTokenService;
    private readonly INotificationHubService _notificationHub;
    private readonly IPostLoginNavigationService _session;

    [ObservableProperty]
    private string _serverHost = "";

    [ObservableProperty]
    private string _appVersion = "";

    [ObservableProperty]
    private bool _notificationsEnabled;

    [ObservableProperty]
    private string _notificationStatusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public AccountViewModel(
        IServerSettingsService serverSettings,
        IDeviceTokenService deviceTokenService,
        INotificationHubService notificationHub,
        IPostLoginNavigationService session)
    {
        _serverSettings = serverSettings;
        _deviceTokenService = deviceTokenService;
        _notificationHub = notificationHub;
        _session = session;
        ServerHost = ServerUrlRules.DisplayHost(_serverSettings.ServerUrl);
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
                : _deviceTokenService.PushRemovalPending ? PushOffPendingText : PushOffText;
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
                var disable = _deviceTokenService.DisableNotificationsAsync();
                try { await _notificationHub.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
                var unregistered = await disable;
                NotificationsEnabled = false;
                NotificationStatusText = unregistered ? PushOffText : PushOffPendingText;
                return;
            }

            var granted = await _deviceTokenService.EnableNotificationsAsync();
            if (granted)
                _ = _notificationHub.StartAsync();

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

        await SignOutAsync();
    }

    [RelayCommand]
    private async Task ChangeServerAsync()
    {
        await SignOutAsync();
    }

    private async Task SignOutAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            await _session.SignOutAsync();
        }
        finally
        {
            IsBusy = false;
        }

        Application.Current!.Windows[0].Page = new AppShell();
    }

    [RelayCommand]
    private async Task OpenThirdPartyNoticesAsync()
    {
        try
        {
            await Browser.Default.OpenAsync(
                "https://github.com/open-ksef/open-ksef-mobile/blob/main/src/OpenKSeF.Mobile/Resources/Raw/pdf/THIRD-PARTY-NOTICES.txt",
                BrowserLaunchMode.SystemPreferred);
        }
        catch { }
    }

    [RelayCommand]
    private async Task OpenPrivacyPolicyAsync()
    {
        try
        {
            await Browser.Default.OpenAsync(
                "https://open-ksef.pl/docs/polityka-prywatnosci",
                BrowserLaunchMode.SystemPreferred);
        }
        catch { }
    }
}
