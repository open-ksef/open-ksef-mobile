namespace OpenKSeF.Mobile.Services;

public class PostLoginNavigationService : IPostLoginNavigationService
{
    private readonly IApiService _apiService;
    private readonly IDeviceTokenService _deviceTokenService;
    private readonly INotificationHubService _notificationHub;

    public PostLoginNavigationService(
        IApiService apiService,
        IDeviceTokenService deviceTokenService,
        INotificationHubService notificationHub)
    {
        _apiService = apiService;
        _deviceTokenService = deviceTokenService;
        _notificationHub = notificationHub;
    }

    public async Task NavigateAsync()
    {
        bool needsOnboarding = false;

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
