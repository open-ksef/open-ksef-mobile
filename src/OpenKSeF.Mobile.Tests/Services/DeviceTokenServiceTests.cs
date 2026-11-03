using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

[Collection("Shell")]
public class DeviceTokenServiceTests
{
    private const string TokenKey = "device_push_token";
    private const string RegisteredKey = "device_registered";
    private const string DeviceIdKey = "device_stable_id";
    private const string PushDisabledKey = "push_disabled_by_user";

    private readonly IApiService _api = Substitute.For<IApiService>();
    private readonly DeviceTokenService _service;

    public DeviceTokenServiceTests()
    {
        Preferences.Default.Clear();
        Permissions.Reset();
        DeviceInfo.Platform = DevicePlatform.Android;
        _api.GetDevicesAsync().Returns(new List<DeviceTokenDto>());
        _service = new DeviceTokenService(_api);
    }

    private void ServerHas(params string[] tokens) =>
        _api.GetDevicesAsync().Returns(tokens.Select(t => new DeviceTokenDto { Id = Guid.NewGuid(), Token = t }).ToList());

    [Fact]
    public async Task IsDeviceRegistered_True_WhenServerHasStoredToken()
    {
        Preferences.Default.Set(TokenKey, "fcm-token-123");
        ServerHas("other", "fcm-token-123");

        Assert.True(await _service.IsDeviceRegisteredAsync());
        Assert.True(Preferences.Default.Get(RegisteredKey, false));
    }

    [Fact]
    public async Task IsDeviceRegistered_IgnoresCachedFlag_WhenServerTokenMissing()
    {
        Preferences.Default.Set(TokenKey, "missing-token");
        Preferences.Default.Set(RegisteredKey, true);
        ServerHas("other");

        Assert.False(await _service.IsDeviceRegisteredAsync());
        Assert.False(Preferences.Default.Get(RegisteredKey, true));
    }

    [Fact]
    public async Task IsDeviceRegistered_MatchesStableDeviceId()
    {
        Preferences.Default.Set(DeviceIdKey, "device-abc");
        ServerHas("device-abc");

        Assert.True(await _service.IsDeviceRegisteredAsync());
        Assert.Equal("device-abc", Preferences.Default.Get(TokenKey, ""));
    }

    [Fact]
    public async Task IsDeviceRegistered_ApiFailure_ReturnsFalse()
    {
        _api.GetDevicesAsync().ThrowsAsync(new HttpRequestException("offline"));

        Assert.False(await _service.IsDeviceRegisteredAsync());
    }

    [Fact]
    public async Task RegisterToken_SkipsApi_WhenStoredTokenOnServer()
    {
        Preferences.Default.Set(TokenKey, "fcm-abc");
        ServerHas("fcm-abc");

        await _service.RegisterTokenAsync("fcm-abc", "Android");

        await _api.DidNotReceiveWithAnyArgs().RegisterDeviceTokenAsync(default!);
    }

    [Fact]
    public async Task RegisterToken_ReRegisters_WhenStoredTokenMissingOnServer()
    {
        Preferences.Default.Set(TokenKey, "fcm-abc");

        await _service.RegisterTokenAsync("fcm-abc", "iOS");

        await _api.Received(1).RegisterDeviceTokenAsync(Arg.Is<RegisterDeviceTokenRequest>(r => r.Token == "fcm-abc" && r.Platform == 1 && r.TenantId == null));
    }

    [Fact]
    public async Task RegisterToken_RefreshedToken_RegistersAndStores()
    {
        Preferences.Default.Set(TokenKey, "fcm-old");

        await _service.RegisterTokenAsync("fcm-new", "Android");

        await _api.Received(1).RegisterDeviceTokenAsync(Arg.Is<RegisterDeviceTokenRequest>(r => r.Token == "fcm-new" && r.Platform == 0));
        Assert.Equal("fcm-new", Preferences.Default.Get(TokenKey, ""));
    }

    [Fact]
    public async Task RegisterToken_ApiFailure_KeepsOldTokenForRetry()
    {
        Preferences.Default.Set(TokenKey, "fcm-old");
        _api.RegisterDeviceTokenAsync(default!).ThrowsAsyncForAnyArgs(new HttpRequestException());

        await _service.RegisterTokenAsync("fcm-new", "Android");

        Assert.Equal("fcm-old", Preferences.Default.Get(TokenKey, ""));
    }

    [Fact]
    public async Task EnsureDeviceRegistered_SkipsRegistration_WhenAlreadyRegistered()
    {
        Preferences.Default.Set(DeviceIdKey, "device-abc");
        ServerHas("device-abc");

        await _service.EnsureDeviceRegisteredAsync();

        await _api.DidNotReceiveWithAnyArgs().RegisterDeviceTokenAsync(default!);
    }

    [Fact]
    public async Task EnsureDeviceRegistered_RegistersStableDeviceIdForAllCompanies()
    {
        DeviceInfo.Platform = DevicePlatform.iOS;

        await _service.EnsureDeviceRegisteredAsync();

        var deviceId = Preferences.Default.Get(DeviceIdKey, "");
        Assert.StartsWith("device-", deviceId);
        Assert.Equal(deviceId, Preferences.Default.Get(TokenKey, ""));
        await _api.Received(1).RegisterDeviceTokenAsync(Arg.Is<RegisterDeviceTokenRequest>(r => r.Token == deviceId && r.Platform == 1 && r.TenantId == null));
    }

    [Fact]
    public async Task EnableNotifications_PermissionDenied_ReturnsFalseWithoutRegistering()
    {
        Permissions.Status = PermissionStatus.Denied;
        Permissions.RequestResult = PermissionStatus.Denied;

        Assert.False(await _service.EnableNotificationsAsync());

        Assert.Equal(1, Permissions.RequestCount);
        await _api.DidNotReceiveWithAnyArgs().RegisterDeviceTokenAsync(default!);
    }

    [Fact]
    public async Task EnableNotifications_RegistersStoredToken_WhenGranted()
    {
        Preferences.Default.Set(TokenKey, "fcm-abc");

        Assert.True(await _service.EnableNotificationsAsync());

        Assert.Equal(0, Permissions.RequestCount);
        await _api.Received(1).RegisterDeviceTokenAsync(Arg.Is<RegisterDeviceTokenRequest>(r => r.Token == "fcm-abc" && r.TenantId == null));
    }

    [Fact]
    public async Task EnableNotifications_NoStoredToken_RegistersAndPersistsDeviceId()
    {
        Assert.True(await _service.EnableNotificationsAsync());

        var deviceId = Preferences.Default.Get(DeviceIdKey, "");
        Assert.Equal(deviceId, Preferences.Default.Get(TokenKey, ""));
        await _api.Received(1).RegisterDeviceTokenAsync(Arg.Is<RegisterDeviceTokenRequest>(r => r.Token == deviceId));
    }

    [Fact]
    public async Task EnableNotifications_ApiFailure_ReturnsFalse()
    {
        _api.RegisterDeviceTokenAsync(default!).ThrowsAsyncForAnyArgs(new HttpRequestException());

        Assert.False(await _service.EnableNotificationsAsync());
    }

    [Fact]
    public async Task UnregisterCurrentDevice_DeletesMatchingServerDevicesAndClearsToken()
    {
        Preferences.Default.Set(TokenKey, "fcm-abc");
        Preferences.Default.Set(DeviceIdKey, "device-abc");
        var mine = new DeviceTokenDto { Id = Guid.NewGuid(), Token = "fcm-abc" };
        var mineById = new DeviceTokenDto { Id = Guid.NewGuid(), Token = "device-abc" };
        var other = new DeviceTokenDto { Id = Guid.NewGuid(), Token = "someone-else" };
        _api.GetDevicesAsync().Returns([mine, mineById, other]);

        await _service.UnregisterCurrentDeviceAsync();

        await _api.Received(1).UnregisterDeviceAsync(mine.Id);
        await _api.Received(1).UnregisterDeviceAsync(mineById.Id);
        await _api.DidNotReceive().UnregisterDeviceAsync(other.Id);
        Assert.Equal("", Preferences.Default.Get(TokenKey, ""));
    }

    private static void TurnedOff() => Preferences.Default.Set(PushDisabledKey, true);

    [Fact]
    public async Task ExistingInstall_NoKey_StaysOnAndRegisters()
    {
        Assert.False(_service.PushDisabledByUser);
        Assert.True(await _service.AreNotificationsEnabledAsync());

        await _service.EnsureDeviceRegisteredAsync();

        await _api.ReceivedWithAnyArgs(1).RegisterDeviceTokenAsync(default!);
    }

    [Fact]
    public async Task Disable_DeletesOnlyThisDevice_ClearsTokenKeys_KeepsStableId()
    {
        Preferences.Default.Set(TokenKey, "fcm-abc");
        Preferences.Default.Set(RegisteredKey, true);
        Preferences.Default.Set(DeviceIdKey, "device-abc");
        var mine = new DeviceTokenDto { Id = Guid.NewGuid(), Token = "fcm-abc" };
        var mineById = new DeviceTokenDto { Id = Guid.NewGuid(), Token = "device-abc" };
        var other = new DeviceTokenDto { Id = Guid.NewGuid(), Token = "other-phone" };
        _api.GetDevicesAsync().Returns([mine, mineById, other]);

        Assert.True(await _service.DisableNotificationsAsync());

        await _api.Received(1).UnregisterDeviceAsync(mine.Id);
        await _api.Received(1).UnregisterDeviceAsync(mineById.Id);
        await _api.DidNotReceive().UnregisterDeviceAsync(other.Id);
        Assert.Equal("", Preferences.Default.Get(TokenKey, ""));
        Assert.False(Preferences.Default.Get(RegisteredKey, false));
        Assert.Equal("device-abc", Preferences.Default.Get(DeviceIdKey, ""));
        Assert.True(_service.PushDisabledByUser);
        Assert.False(_service.PushRemovalPending);
    }

    [Fact]
    public async Task EnsureDeviceRegistered_WhileOff_DoesNotRegister()
    {
        TurnedOff();

        await _service.EnsureDeviceRegisteredAsync();

        await _api.DidNotReceiveWithAnyArgs().RegisterDeviceTokenAsync(default!);
    }

    [Fact]
    public async Task RegisterToken_WhileOff_DoesNothing()
    {
        TurnedOff();

        await _service.RegisterTokenAsync("fcm-rotated", "Android");

        await _api.DidNotReceiveWithAnyArgs().RegisterDeviceTokenAsync(default!);
        await _api.DidNotReceive().GetDevicesAsync();
        Assert.Equal("", Preferences.Default.Get(TokenKey, ""));
    }

    [Fact]
    public async Task Logout_WhileOff_KeepsChoice_NextLoginDoesNotRegister()
    {
        Preferences.Default.Set(DeviceIdKey, "device-abc");
        await _service.DisableNotificationsAsync();

        await _service.UnregisterCurrentDeviceAsync();
        await _service.EnsureDeviceRegisteredAsync();

        Assert.True(_service.PushDisabledByUser);
        await _api.DidNotReceiveWithAnyArgs().RegisterDeviceTokenAsync(default!);
    }

    [Fact]
    public async Task Disable_Offline_ReturnsFalse_KeepsChoiceAndToken()
    {
        Preferences.Default.Set(TokenKey, "fcm-abc");
        _api.GetDevicesAsync().ThrowsAsync(new HttpRequestException("offline"));

        Assert.False(await _service.DisableNotificationsAsync());

        Assert.True(_service.PushDisabledByUser);
        Assert.True(_service.PushRemovalPending);
        Assert.Equal("fcm-abc", Preferences.Default.Get(TokenKey, ""));
    }

    [Fact]
    public async Task EnsureDeviceRegistered_AfterOfflineDisable_RetriesDeleteAndClearsKeys()
    {
        Preferences.Default.Set(TokenKey, "fcm-abc");
        _api.GetDevicesAsync().ThrowsAsync(new HttpRequestException("offline"));
        await _service.DisableNotificationsAsync();
        var mine = new DeviceTokenDto { Id = Guid.NewGuid(), Token = "fcm-abc" };
        _api.GetDevicesAsync().Returns([mine]);

        await _service.EnsureDeviceRegisteredAsync();

        await _api.Received(1).UnregisterDeviceAsync(mine.Id);
        await _api.DidNotReceiveWithAnyArgs().RegisterDeviceTokenAsync(default!);
        Assert.Equal("", Preferences.Default.Get(TokenKey, ""));
        Assert.False(_service.PushRemovalPending);
    }

    [Fact]
    public async Task Disable_PartialDeleteFailure_ReturnsFalse_RetryFinishes()
    {
        Preferences.Default.Set(TokenKey, "fcm-abc");
        Preferences.Default.Set(DeviceIdKey, "device-abc");
        var first = new DeviceTokenDto { Id = Guid.NewGuid(), Token = "fcm-abc" };
        var second = new DeviceTokenDto { Id = Guid.NewGuid(), Token = "device-abc" };
        _api.GetDevicesAsync().Returns([first, second]);
        _api.UnregisterDeviceAsync(second.Id).ThrowsAsync(new HttpRequestException("offline"));

        Assert.False(await _service.DisableNotificationsAsync());
        Assert.Equal("fcm-abc", Preferences.Default.Get(TokenKey, ""));

        _api.UnregisterDeviceAsync(second.Id).Returns(Task.CompletedTask);
        _api.GetDevicesAsync().Returns([second]);
        await _service.EnsureDeviceRegisteredAsync();

        await _api.Received(2).UnregisterDeviceAsync(second.Id);
        Assert.Equal("", Preferences.Default.Get(TokenKey, ""));
    }

    [Fact]
    public async Task AreNotificationsEnabled_WhileOff_False_EvenWithPermissionGranted()
    {
        TurnedOff();
        Permissions.Status = PermissionStatus.Granted;

        Assert.False(await _service.AreNotificationsEnabledAsync());
    }

    [Fact]
    public async Task EnableNotifications_Success_RemovesChoice()
    {
        TurnedOff();

        Assert.True(await _service.EnableNotificationsAsync());

        Assert.False(_service.PushDisabledByUser);
        Assert.True(await _service.AreNotificationsEnabledAsync());
    }

    [Fact]
    public async Task EnableNotifications_PermissionDenied_KeepsChoice()
    {
        TurnedOff();
        Permissions.Status = PermissionStatus.Denied;
        Permissions.RequestResult = PermissionStatus.Denied;

        Assert.False(await _service.EnableNotificationsAsync());

        Assert.True(_service.PushDisabledByUser);
    }

    [Fact]
    public async Task EnableNotifications_ApiFailure_KeepsChoice()
    {
        TurnedOff();
        _api.RegisterDeviceTokenAsync(default!).ThrowsAsyncForAnyArgs(new HttpRequestException());

        Assert.False(await _service.EnableNotificationsAsync());

        Assert.True(_service.PushDisabledByUser);
    }
}
