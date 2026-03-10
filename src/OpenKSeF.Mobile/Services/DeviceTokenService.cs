using OpenKSeF.Mobile.Models;

namespace OpenKSeF.Mobile.Services;

public class DeviceTokenService : IDeviceTokenService
{
    private const string TokenStorageKey = "device_push_token";
    private const string PlatformStorageKey = "device_push_platform";
    private const string PermissionRequestedKey = "push_permission_requested";
    private const string DeviceRegisteredKey = "device_registered";

    private readonly IApiService _apiService;

    public DeviceTokenService(IApiService apiService)
    {
        _apiService = apiService;
    }

    public async Task RegisterTokenAsync(string token, string platform)
    {
        var storedToken = Preferences.Default.Get(TokenStorageKey, string.Empty);
        if (storedToken == token)
            return;

        try
        {
            var tenantId = GetSelectedTenantId();

            await _apiService.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
            {
                Token = token,
                Platform = ParsePlatform(platform),
                TenantId = tenantId
            });

            Preferences.Default.Set(TokenStorageKey, token);
            Preferences.Default.Set(PlatformStorageKey, platform);
            Preferences.Default.Set(DeviceRegisteredKey, true);
        }
        catch
        {
            // Best-effort; will retry on next app start.
        }
    }

    public async Task RegisterTokenForTenantsAsync(List<Guid>? tenantIds)
    {
        var token = Preferences.Default.Get(TokenStorageKey, string.Empty);

        if (string.IsNullOrEmpty(token))
            return;

        try
        {
            var platformInt = GetCurrentPlatform();

            if (tenantIds is null || tenantIds.Count == 0)
            {
                await _apiService.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
                {
                    Token = token,
                    Platform = platformInt,
                    TenantId = null
                });
            }
            else
            {
                foreach (var tenantId in tenantIds)
                {
                    await _apiService.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
                    {
                        Token = token,
                        Platform = platformInt,
                        TenantId = tenantId
                    });
                }
            }
        }
        catch
        {
            // Best-effort
        }
    }

    public async Task RequestNotificationPermissionAsync()
    {
        var alreadyRequested = Preferences.Default.Get(PermissionRequestedKey, false);
        if (alreadyRequested)
            return;

        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.PostNotifications>();
        }

        Preferences.Default.Set(PermissionRequestedKey, true);
    }

    public async Task<bool> IsDeviceRegisteredAsync()
    {
        if (Preferences.Default.Get(DeviceRegisteredKey, false))
            return true;

        try
        {
            var devices = await _apiService.GetDevicesAsync();
            var currentPlatform = GetCurrentPlatform();
            var storedToken = Preferences.Default.Get(TokenStorageKey, string.Empty);

            bool found;
            if (!string.IsNullOrEmpty(storedToken))
                found = devices.Any(d => d.Token == storedToken);
            else
                found = devices.Any(d => d.Platform == currentPlatform);

            if (found)
                Preferences.Default.Set(DeviceRegisteredKey, true);

            return found;
        }
        catch
        {
            return false;
        }
    }

    public async Task EnsureDeviceRegisteredAsync()
    {
        try
        {
            if (await IsDeviceRegisteredAsync())
                return;

            var platformInt = GetCurrentPlatform();
            var tenantId = GetSelectedTenantId();

            var deviceId = GetOrCreateDeviceId();

            await _apiService.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
            {
                Token = deviceId,
                Platform = platformInt,
                TenantId = tenantId
            });

            Preferences.Default.Set(DeviceRegisteredKey, true);
        }
        catch
        {
            // Best-effort; don't block login flow.
        }
    }

    public async Task<bool> EnableNotificationsAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.PostNotifications>();
        }

        Preferences.Default.Set(PermissionRequestedKey, true);

        if (status != PermissionStatus.Granted)
            return false;

        var platformInt = GetCurrentPlatform();
        var tenantId = GetSelectedTenantId();

        var storedToken = Preferences.Default.Get(TokenStorageKey, string.Empty);
        var token = !string.IsNullOrEmpty(storedToken) ? storedToken : GetOrCreateDeviceId();

        await _apiService.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
        {
            Token = token,
            Platform = platformInt,
            TenantId = tenantId
        });

        Preferences.Default.Set(DeviceRegisteredKey, true);

        return true;
    }

    public async Task<bool> AreNotificationsEnabledAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        return status == PermissionStatus.Granted;
    }

    private static Guid? GetSelectedTenantId()
    {
        var tenantIdStr = Preferences.Default.Get("SelectedTenantId", string.Empty);
        if (Guid.TryParse(tenantIdStr, out var tenantId))
            return tenantId;
        return null;
    }

    private static int GetCurrentPlatform()
    {
        return DeviceInfo.Platform == DevicePlatform.Android ? 0 : 1;
    }

    private static int ParsePlatform(string platform)
    {
        return platform.Equals("iOS", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private static string GetOrCreateDeviceId()
    {
        const string key = "device_stable_id";
        var existing = Preferences.Default.Get(key, string.Empty);
        if (!string.IsNullOrEmpty(existing))
            return existing;

        var id = $"device-{Guid.NewGuid():N}";
        Preferences.Default.Set(key, id);
        return id;
    }
}
