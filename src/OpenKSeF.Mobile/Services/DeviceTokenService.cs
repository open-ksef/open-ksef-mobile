using System.Net;
using System.Text.Json;
using OpenKSeF.Mobile.Models;

namespace OpenKSeF.Mobile.Services;

public class DeviceTokenService : IDeviceTokenService
{
    internal static TimeSpan ServerRemovalLimit { get; set; } = TimeSpan.FromSeconds(15);

    internal static TimeSpan LateRegistrationWait { get; set; } = TimeSpan.FromSeconds(90);

    private const int MaxPendingSignOuts = 5;

    private const string TokenStorageKey = "device_push_token";
    private const string PlatformStorageKey = "device_push_platform";
    private const string PermissionRequestedKey = "push_permission_requested";
    private const string DeviceRegisteredKey = "device_registered";
    private const string DeviceIdKey = "device_stable_id";
    private const string PushDisabledKey = "push_disabled_by_user";
    private const string PendingSignOutKey = "pending_signout_sessions";

    private readonly IApiService _apiService;
    private readonly IAuthService _authService;
    private readonly object _gate = new();
    private readonly HashSet<string> _posting = [];
    private Task _registration = Task.CompletedTask;
    private Task _cleanup = Task.CompletedTask;
    private CancellationTokenSource? _session;
    private readonly HashSet<CancellationToken> _abandonedSessions = [];
    private DateTimeOffset _abandonedAt = DateTimeOffset.MinValue;

    public DeviceTokenService(IApiService apiService, IAuthService authService)
    {
        _apiService = apiService;
        _authService = authService;
    }

    public bool PushDisabledByUser => Preferences.Default.Get(PushDisabledKey, false);

    public bool PushRemovalPending =>
        PushDisabledByUser && !string.IsNullOrEmpty(Preferences.Default.Get(TokenStorageKey, string.Empty));

    internal bool SignOutCleanupPending => PendingSignOuts().Count > 0;

    public bool ShowHubNotification(bool? nativePush) =>
        !PushDisabledByUser
        && (nativePush != true
            || Preferences.Default.Get(TokenStorageKey, string.Empty) is not { Length: > 0 } token
            || token == Preferences.Default.Get(DeviceIdKey, string.Empty));

    public Task RegisterTokenAsync(string token, string platform) =>
        Enqueue(async ct =>
        {
            await RegisterTokenAsync(token, platform, ct);
            return true;
        });

    private async Task RegisterTokenAsync(string token, string platform, CancellationToken ct)
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
            await PostAsync(new RegisterDeviceTokenRequest
            {
                Token = token,
                Platform = ParsePlatform(platform),
                TenantId = null
            }, ct);

            await SaveRegistrationAsync(token, platform);
        }
        catch
        {
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

    public Task EnsureDeviceRegisteredAsync() =>
        Enqueue(async ct =>
        {
            await EnsureRegisteredAsync(ct);
            return true;
        });

    private async Task EnsureRegisteredAsync(CancellationToken ct)
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
                await RegisterTokenAsync(nativeToken, nativePlatform, ct);
                return;
            }

            if (await IsDeviceRegisteredAsync())
                return;

            var platformInt = GetCurrentPlatform();

            var deviceId = GetOrCreateDeviceId();

            await PostAsync(new RegisterDeviceTokenRequest
            {
                Token = deviceId,
                Platform = platformInt,
                TenantId = null
            }, ct);

            await SaveRegistrationAsync(deviceId, GetCurrentPlatformName());
        }
        catch
        {
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

        return await Enqueue(async ct =>
        {
            var platformInt = GetCurrentPlatform();

            var storedToken = Preferences.Default.Get(TokenStorageKey, string.Empty);
            var nativeToken = await TryGetNativePushTokenAsync();
            var token = !string.IsNullOrWhiteSpace(nativeToken)
                ? nativeToken
                : !string.IsNullOrEmpty(storedToken) ? storedToken : GetOrCreateDeviceId();

            await PostAsync(new RegisterDeviceTokenRequest
            {
                Token = token,
                Platform = platformInt,
                TenantId = null
            }, ct);

            Preferences.Default.Set(TokenStorageKey, token);
            Preferences.Default.Set(PlatformStorageKey, GetCurrentPlatformName());
            Preferences.Default.Set(DeviceRegisteredKey, true);
            Preferences.Default.Remove(PushDisabledKey);
            return true;
        });
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

    /// <summary>Saves this sign-out's device tokens under <paramref name="signOutId"/> until removal is confirmed: a POST cancelled, timed out or still running may land on the server after the delete.</summary>
    public async Task<bool> UnregisterCurrentDeviceAsync(string signOutId, CancellationToken cancellationToken)
    {
        HashSet<string> tokens;
        CancellationToken session;
        lock (_gate)
        {
            session = _session?.Token ?? CancellationToken.None;
            _session?.Cancel();
            _session = null;
            tokens = StoredTokens();
            tokens.UnionWith(_posting);
            _posting.Clear();
            tokens.Remove(string.Empty);
        }
        AddPendingSignOut(new PendingSignOut(signOutId, [.. tokens]));

        try { await _registration.WaitAsync(cancellationToken); } catch { }
        var settled = _registration.IsCompleted;
        RemoveTokenKeys();

        if (!await TryDeleteServerDevicesAsync(tokens, cancellationToken) || !settled)
            return false;

        lock (_gate)
        {
            if (_abandonedSessions.Contains(session))
                return false;
        }
        UpdatePendingSignOuts(list => list.RemoveAll(p => p.Id == signOutId));
        return true;
    }

    public Task CompleteSignOutCleanupAsync()
    {
        lock (_gate)
            return _cleanup = RunSignOutCleanupAsync(_cleanup);
    }

    private async Task RunSignOutCleanupAsync(Task previous)
    {
        await Task.Yield();
        try { await previous; } catch { }

        try { await _registration.WaitAsync(LateRegistrationWait); } catch { }
        if (!_registration.IsCompleted)
            return;

        var lateRowWait = _abandonedAt + LateRegistrationWait - DateTimeOffset.UtcNow;
        if (lateRowWait > TimeSpan.Zero)
            await Task.Delay(lateRowWait);

        // Read before the flag: an entry written by a sign-out still underway has no deferred refresh token yet.
        var pending = PendingSignOuts();
        if (_authService.LogoutUnderway)
            return;

        foreach (var signOut in pending)
        {
            try
            {
                using var limit = new CancellationTokenSource(ServerRemovalLimit);
                var accessToken = await _authService.GetDeferredLogoutAccessTokenAsync(signOut.Id, limit.Token).WaitAsync(limit.Token);
                if (accessToken is not null)
                    await DeleteServerDevicesAsync([.. signOut.Tokens], limit.Token, accessToken).WaitAsync(limit.Token);

                if (!await _authService.CompleteDeferredLogoutAsync(signOut.Id))
                    continue;

                UpdatePendingSignOuts(list => list.RemoveAll(p => p.Id == signOut.Id));
            }
            catch
            {
                // Offline or timed out: app resume or the next start retries.
            }
        }
    }

    private sealed record PendingSignOut(string Id, string[] Tokens);

    private static List<PendingSignOut> PendingSignOuts()
    {
        try
        {
            return JsonSerializer.Deserialize<List<PendingSignOut>>(Preferences.Default.Get(PendingSignOutKey, "[]")) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void UpdatePendingSignOuts(Action<List<PendingSignOut>> change)
    {
        lock (_gate)
        {
            var list = PendingSignOuts();
            change(list);
            if (list.Count == 0)
                Preferences.Default.Remove(PendingSignOutKey);
            else
                Preferences.Default.Set(PendingSignOutKey, JsonSerializer.Serialize(list));
        }
    }

    private void AddPendingSignOut(PendingSignOut signOut)
    {
        List<PendingSignOut> evicted = [];
        UpdatePendingSignOuts(list =>
        {
            list.Add(signOut);
            evicted = list.GetRange(0, Math.Max(0, list.Count - MaxPendingSignOuts));
            list.RemoveRange(0, evicted.Count);
        });
        foreach (var old in evicted)
            _ = _authService.CompleteDeferredLogoutAsync(old.Id, dropOnFailure: true);
    }

    private Task<T> Enqueue<T>(Func<CancellationToken, Task<T>> work)
    {
        lock (_gate)
        {
            var ct = (_session ??= new CancellationTokenSource()).Token;
            var task = RunAfterAsync(_registration, work, ct);
            _registration = task;
            return task;
        }
    }

    private static async Task<T> RunAfterAsync<T>(Task previous, Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        try { await previous; } catch { }
        ct.ThrowIfCancellationRequested();
        return await work(ct);
    }

    private async Task PostAsync(RegisterDeviceTokenRequest request, CancellationToken ct)
    {
        lock (_gate)
            _posting.Add(request.Token);

        try
        {
            await _apiService.RegisterDeviceTokenAsync(request, ct);
        }
        // Timeout, network error, cancel or 5xx: the server may still insert the row, so it stays unknown for this process.
        catch (Exception ex) when (ct.IsCancellationRequested || ex is not ApiException { StatusCode: < HttpStatusCode.InternalServerError })
        {
            lock (_gate)
            {
                _abandonedSessions.Add(ct);
                _abandonedAt = DateTimeOffset.UtcNow;
            }
            throw;
        }

        ct.ThrowIfCancellationRequested();
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

    private async Task DeleteServerDevicesAsync(HashSet<string> tokens, CancellationToken limit, string? accessToken = null)
    {
        tokens.Remove(string.Empty);
        if (tokens.Count == 0 && await TryGetNativePushTokenAsync().WaitAsync(limit) is { Length: > 0 } nativeToken)
            tokens.Add(nativeToken);

        if (tokens.Count == 0)
            return;

        var devices = await _apiService.GetDevicesAsync(accessToken, limit);
        foreach (var device in devices.Where(d => tokens.Contains(d.Token)))
        {
            limit.ThrowIfCancellationRequested();
            await _apiService.UnregisterDeviceAsync(device.Id, accessToken, limit);
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
