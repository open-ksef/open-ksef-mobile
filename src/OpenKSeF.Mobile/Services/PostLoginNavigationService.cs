namespace OpenKSeF.Mobile.Services;

public class PostLoginNavigationService : IPostLoginNavigationService
{
    internal static TimeSpan SignOutLimit { get; set; } = TimeSpan.FromSeconds(15);

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
        _ = _deviceTokenService.CompleteSignOutCleanupAsync();
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
            _ = RegisterDeviceAsync();
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

    private async Task RegisterDeviceAsync()
    {
        try { await _deviceTokenService.EnsureDeviceRegisteredAsync(); } catch { }
    }

    private async Task StartHubAsync()
    {
        if (_deviceTokenService.PushDisabledByUser)
            return;

        try { await _notificationHub.StartAsync(); } catch { }
    }

    public async Task HandleSessionEndedAsync()
    {
        try { await _notificationHub.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        ClearCacheDirectory();
        try { await Shell.Current.DisplayAlertAsync("Sesja wygasła", "Zaloguj się ponownie.", "OK"); } catch { }
        if (Application.Current?.Windows is { Count: > 0 } windows)
            windows[0].Page = new AppShell();
    }

    public async Task SignOutAsync()
    {
        _authService.LogoutUnderway = true;
        try
        {
            using var limit = new CancellationTokenSource(SignOutLimit);
            try { await _notificationHub.StopAsync().WaitAsync(limit.Token); } catch { }
            var unregistered = false;
            try
            {
                await _deviceTokenService.UnregisterCurrentDeviceAsync(limit.Token).WaitAsync(limit.Token);
                unregistered = !_deviceTokenService.SignOutCleanupPending;
            }
            catch { }
            try { await _localDb.ClearUserDataAsync(); } catch { }
            ClearCacheDirectory();
            if (unregistered)
            {
                await _authService.LogoutAsync();
            }
            else
            {
                await _authService.DeferLogoutAsync();
                _ = _deviceTokenService.CompleteSignOutCleanupAsync();
            }
        }
        finally
        {
            _authService.LogoutUnderway = false;
        }
    }

    /// <summary>The whole directory, not named files: shared PDF, XML and UPO hold private buyers' data, and the share sheet may keep its own copies here.</summary>
    private static void ClearCacheDirectory()
    {
        try
        {
            foreach (var entry in new DirectoryInfo(FileSystem.CacheDirectory).GetFileSystemInfos())
            {
                try
                {
                    if (entry is DirectoryInfo directory)
                        directory.Delete(recursive: true);
                    else
                        entry.Delete();
                }
                catch { }
            }
        }
        catch { }
    }
}
