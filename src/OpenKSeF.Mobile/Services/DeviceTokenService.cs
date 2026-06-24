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
        if (storedToken == token && await ServerHasTokenAsync(token))
        {
            Preferences.Default.Set(DeviceRegisteredKey, true);
            return;
        }

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

    public async Task<bool> IsDeviceRegisteredAsync()
    {
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
            else
                Preferences.Default.Set(DeviceRegisteredKey, false);

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
            var nativeToken = await TryGetNativePushTokenAsync();
            if (!string.IsNullOrWhiteSpace(nativeToken))
            {
                var nativePlatform = DeviceInfo.Platform == DevicePlatform.Android ? "Android" : "iOS";
                await RegisterTokenAsync(nativeToken, nativePlatform);
                return;
            }

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

            Preferences.Default.Set(TokenStorageKey, deviceId);
            Preferences.Default.Set(PlatformStorageKey, GetCurrentPlatformName());
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

        var registered = false;
        try
        {
            var platformInt = GetCurrentPlatform();
            var tenantId = GetSelectedTenantId();

            var storedToken = Preferences.Default.Get(TokenStorageKey, string.Empty);
            var nativeToken = await TryGetNativePushTokenAsync();
            var token = !string.IsNullOrWhiteSpace(nativeToken)
                ? nativeToken
                : !string.IsNullOrEmpty(storedToken) ? storedToken : GetOrCreateDeviceId();

            await _apiService.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
            {
                Token = token,
                Platform = platformInt,
                TenantId = tenantId
            });

            Preferences.Default.Set(TokenStorageKey, token);
            Preferences.Default.Set(PlatformStorageKey, GetCurrentPlatformName());
            Preferences.Default.Set(DeviceRegisteredKey, true);
            registered = true;
        }
        catch
        {
        }

        return registered;
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

    private static string GetCurrentPlatformName()
    {
        return DeviceInfo.Platform == DevicePlatform.Android ? "Android" : "iOS";
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

    private static Task<string?> TryGetNativePushTokenAsync()
    {
#if ANDROID && FIREBASE_ENABLED
        return OpenKSeF.Mobile.PushNotificationFirebaseService.TryGetCurrentTokenAsync();
#else
        return Task.FromResult<string?>(null);
#endif
    }

    private async Task<bool> ServerHasTokenAsync(string token)
    {
        try
        {
            var devices = await _apiService.GetDevicesAsync();
            return devices.Any(d => d.Token == token);
        }
        catch
        {
            return false;
        }
    }
}
