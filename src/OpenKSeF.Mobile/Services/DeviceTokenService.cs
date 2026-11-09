using OpenKSeF.Mobile.Models;

namespace OpenKSeF.Mobile.Services;

public class DeviceTokenService : IDeviceTokenService
{
    internal static TimeSpan ServerRemovalLimit { get; set; } = TimeSpan.FromSeconds(15);

    private const string TokenStorageKey = "device_push_token";
    private const string PlatformStorageKey = "device_push_platform";
    private const string PermissionRequestedKey = "push_permission_requested";
    private const string DeviceRegisteredKey = "device_registered";
    private const string DeviceIdKey = "device_stable_id";
    private const string PushDisabledKey = "push_disabled_by_user";

    private readonly IApiService _apiService;
    private Task _registration = Task.CompletedTask;

    public DeviceTokenService(IApiService apiService)
    {
        _apiService = apiService;
    }

    public bool PushDisabledByUser => Preferences.Default.Get(PushDisabledKey, false);

    public bool PushRemovalPending =>
        PushDisabledByUser && !string.IsNullOrEmpty(Preferences.Default.Get(TokenStorageKey, string.Empty));

    public bool HasNativePushToken =>
        Preferences.Default.Get(TokenStorageKey, string.Empty) is { Length: > 0 } token
        && token != Preferences.Default.Get(DeviceIdKey, string.Empty);

    public async Task RegisterTokenAsync(string token, string platform)
    {
        if (PushDisabledByUser)
            return;

        var storedToken = Preferences.Default.Get(TokenStorageKey, string.Empty);
        if (storedToken == token && await ServerHasTokenAsync(token))
        {
            Preferences.Default.Set(DeviceRegisteredKey, true);
            return;
        }

        try
        {

            // TenantId = null: the device gets pushes for all of the user's companies.
            // Per-company scoping is an explicit choice made in the portal.
            await _apiService.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
            {
                Token = token,
                Platform = ParsePlatform(platform),
                TenantId = null
            });

            await SaveRegistrationAsync(token, platform);
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
            var storedToken = Preferences.Default.Get(TokenStorageKey, string.Empty);
            var deviceId = Preferences.Default.Get(DeviceIdKey, string.Empty);

            var match = devices.FirstOrDefault(d =>
                d.Token.Length > 0 && (d.Token == storedToken || d.Token == deviceId));

            if (match is null)
            {
                Preferences.Default.Set(DeviceRegisteredKey, false);
                return false;
            }

            Preferences.Default.Set(TokenStorageKey, match.Token);
            Preferences.Default.Set(DeviceRegisteredKey, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task EnsureDeviceRegisteredAsync() => _registration = EnsureRegisteredAsync();

    private async Task EnsureRegisteredAsync()
    {
        if (PushDisabledByUser)
        {
            await DisableNotificationsAsync();
            return;
        }

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

            var deviceId = GetOrCreateDeviceId();

            await _apiService.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
            {
                Token = deviceId,
                Platform = platformInt,
                TenantId = null
            });

            await SaveRegistrationAsync(deviceId, GetCurrentPlatformName());
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

        var storedToken = Preferences.Default.Get(TokenStorageKey, string.Empty);
        var nativeToken = await TryGetNativePushTokenAsync();
        var token = !string.IsNullOrWhiteSpace(nativeToken)
            ? nativeToken
            : !string.IsNullOrEmpty(storedToken) ? storedToken : GetOrCreateDeviceId();

        await _apiService.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
        {
            Token = token,
            Platform = platformInt,
            TenantId = null
        });

        Preferences.Default.Set(TokenStorageKey, token);
        Preferences.Default.Set(PlatformStorageKey, GetCurrentPlatformName());
        Preferences.Default.Set(DeviceRegisteredKey, true);
        Preferences.Default.Remove(PushDisabledKey);
        return true;
    }

    public async Task<bool> AreNotificationsEnabledAsync()
    {
        if (PushDisabledByUser)
            return false;

        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        return status == PermissionStatus.Granted;
    }

    public async Task<bool> DisableNotificationsAsync()
    {
        Preferences.Default.Set(PushDisabledKey, true);

        if (!await TryDeleteServerDevicesAsync(StoredTokens()))
            return false;

        RemoveTokenKeys();
        return true;
    }

    public async Task UnregisterCurrentDeviceAsync(CancellationToken cancellationToken)
    {
        try { await _registration.WaitAsync(cancellationToken); } catch { }
        var tokens = StoredTokens();
        RemoveTokenKeys();
        await TryDeleteServerDevicesAsync(tokens, cancellationToken);
    }

    // Registration no longer blocks login, so the user can turn push off while the POST is in flight.
    private async Task SaveRegistrationAsync(string token, string platform)
    {
        Preferences.Default.Set(TokenStorageKey, token);
        Preferences.Default.Set(PlatformStorageKey, platform);
        Preferences.Default.Set(DeviceRegisteredKey, true);
        if (PushDisabledByUser)
            await DisableNotificationsAsync();
    }

    private static HashSet<string> StoredTokens() =>
    [
        Preferences.Default.Get(TokenStorageKey, string.Empty),
        Preferences.Default.Get(DeviceIdKey, string.Empty)
    ];

    private static void RemoveTokenKeys()
    {
        Preferences.Default.Remove(TokenStorageKey);
        Preferences.Default.Remove(PlatformStorageKey);
        Preferences.Default.Remove(DeviceRegisteredKey);
    }

    private async Task<bool> TryDeleteServerDevicesAsync(HashSet<string> tokens, CancellationToken cancellationToken = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(ServerRemovalLimit);
        try
        {
            await DeleteServerDevicesAsync(tokens, limit.Token).WaitAsync(limit.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task DeleteServerDevicesAsync(HashSet<string> tokens, CancellationToken limit)
    {
        tokens.Remove(string.Empty);
        if (tokens.Count == 0 && await TryGetNativePushTokenAsync().WaitAsync(limit) is { Length: > 0 } nativeToken)
            tokens.Add(nativeToken);

        if (tokens.Count == 0)
            return;

        var devices = await _apiService.GetDevicesAsync();
        foreach (var device in devices.Where(d => tokens.Contains(d.Token)))
        {
            limit.ThrowIfCancellationRequested();
            await _apiService.UnregisterDeviceAsync(device.Id);
        }
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
        var existing = Preferences.Default.Get(DeviceIdKey, string.Empty);
        if (!string.IsNullOrEmpty(existing))
            return existing;

        var id = $"device-{Guid.NewGuid():N}";
        Preferences.Default.Set(DeviceIdKey, id);
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
