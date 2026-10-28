namespace OpenKSeF.Mobile.Services;

public class PostLoginNavigationService : IPostLoginNavigationService
{
    private readonly IApiService _apiService;
    private readonly IDeviceTokenService _deviceTokenService;
    private readonly INotificationHubService _notificationHub;
    private readonly IServerSettingsService _serverSettings;
    private readonly LocalDbService _localDb;

    public PostLoginNavigationService(
        IApiService apiService,
        IDeviceTokenService deviceTokenService,
        INotificationHubService notificationHub,
        IServerSettingsService serverSettings,
        LocalDbService localDb)
    {
        _apiService = apiService;
        _deviceTokenService = deviceTokenService;
        _notificationHub = notificationHub;
        _serverSettings = serverSettings;
        _localDb = localDb;
    }

    public async Task NavigateAsync()
    {
        bool needsOnboarding = false;

        try
        {
            var me = await _apiService.GetMeAsync();
            await _localDb.EnsureCurrentUserAsync($"{_serverSettings.ServerUrl}|{me.UserId}");
        }
        catch
        {
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
            try { await _notificationHub.StartAsync(); } catch { }
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
}
