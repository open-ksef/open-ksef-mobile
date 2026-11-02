namespace OpenKSeF.Mobile.Services;

public class PostLoginNavigationService : IPostLoginNavigationService
{
    private readonly IApiService _apiService;
    private readonly IAuthService _authService;
    private readonly IDeviceTokenService _deviceTokenService;
    private readonly INotificationHubService _notificationHub;
    private readonly IServerSettingsService _serverSettings;
    private readonly LocalDbService _localDb;

    public PostLoginNavigationService(
        IApiService apiService,
        IAuthService authService,
        IDeviceTokenService deviceTokenService,
        INotificationHubService notificationHub,
        IServerSettingsService serverSettings,
        LocalDbService localDb)
    {
        _apiService = apiService;
        _authService = authService;
        _deviceTokenService = deviceTokenService;
        _notificationHub = notificationHub;
        _serverSettings = serverSettings;
        _localDb = localDb;
    }

    public Task NavigateAsync() => NavigateAsync(sameUserAsCache: false);

    public async Task RestoreSessionAsync()
    {
        switch (await _authService.RestoreSessionAsync())
        {
            case SessionRestoreResult.Restored:
                await NavigateAsync(sameUserAsCache: true);
                break;
            case SessionRestoreResult.Unverified:
                _ = StartHubAsync();
                try { await Shell.Current.GoToAsync("//main/invoices"); } catch { }
                break;
        }
    }

    private async Task NavigateAsync(bool sameUserAsCache)
    {
        bool needsOnboarding = false;

        try
        {
            var me = await _apiService.GetMeAsync();
            await _localDb.EnsureCurrentUserAsync($"{_serverSettings.ServerUrl}|{me.UserId}");
        }
        catch
        {
            if (!sameUserAsCache)
                try { await _localDb.ClearUserDataAsync(); } catch { }
        }

        try
        {
            var status = await _apiService.GetOnboardingStatusAsync();
            needsOnboarding = !status.IsComplete;
        }
        catch { }

        if (!needsOnboarding)
        {
            try { await _deviceTokenService.EnsureDeviceRegisteredAsync(); } catch { }
            _ = StartHubAsync();
        }

        try
        {
            if (needsOnboarding)
                await Shell.Current.GoToAsync("//onboarding");
            else
                await Shell.Current.GoToAsync("//main/invoices");
        }
        catch { }
    }

    private async Task StartHubAsync()
    {
        try { await _notificationHub.StartAsync(); } catch { }
    }

    public async Task HandleSessionEndedAsync()
    {
        try { await _notificationHub.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        try { await Shell.Current.DisplayAlertAsync("Sesja wygasła", "Zaloguj się ponownie.", "OK"); } catch { }
        Application.Current!.Windows[0].Page = new AppShell();
    }

    public async Task SignOutAsync()
    {
        try { await _notificationHub.StopAsync(); } catch { }
        try { await _deviceTokenService.UnregisterCurrentDeviceAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        try { await _localDb.ClearUserDataAsync(); } catch { }
        await _authService.LogoutAsync();
    }
}
