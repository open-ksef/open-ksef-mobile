using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly IAuthService _auth = Substitute.For<IAuthService>();
    private readonly DeviceTokenService _service;

    public DeviceTokenServiceTests()
    {
        Preferences.Default.Clear();
        Permissions.Reset();
        DeviceInfo.Platform = DevicePlatform.Android;
        _api.GetDevicesAsync().Returns(new List<DeviceTokenDto>());
        _service = new DeviceTokenService(_api, _auth);
    }

    private void ServerHas(params string[] tokens) =>
        _api.GetDevicesAsync().Returns(tokens.Select(t => new DeviceTokenDto { Id = Guid.NewGuid(), Token = t }).ToList());

    [Fact]
    public async Task ShowHubNotification_OnlyWithoutNativeTokenAndWithPushOn()
    {
        Assert.True(_service.ShowHubNotification(true));

        await _service.EnsureDeviceRegisteredAsync();
        Assert.StartsWith("device-", Preferences.Default.Get(TokenKey, ""));
        Assert.True(_service.ShowHubNotification(true));

        await _service.RegisterTokenAsync("fcm-token-123", "Android");
        Assert.False(_service.ShowHubNotification(true));

        await _service.UnregisterCurrentDeviceAsync(CancellationToken.None);
        Assert.True(_service.ShowHubNotification(true));
    }

    [Fact]
    public async Task ShowHubNotification_False_WhenUserTurnedPushOff()
    {
        await _service.EnsureDeviceRegisteredAsync();

        Assert.True(await _service.DisableNotificationsAsync());

        Assert.False(_service.ShowHubNotification(true));
    }

    [Theory]
    [InlineData(false, null, false, true)]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(true, null, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, null, true, false)]
    [InlineData(true, null, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    public async Task ShowHubNotification_Matrix(bool nativeToken, bool? nativePush, bool pushOff, bool shown)
    {
        await _service.EnsureDeviceRegisteredAsync();
        if (nativeToken)
            await _service.RegisterTokenAsync("fcm-token-123", "Android");
        if (pushOff)
            TurnedOff();

        Assert.Equal(shown, _service.ShowHubNotification(nativePush));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task HubHandler_NativeTokenStored_GatesOnServerNativePush(bool? nativePush, bool shown)
    {
        await _service.RegisterTokenAsync("fcm-token-123", "Android");
        var hub = new NotificationHubService(Substitute.For<IAuthService>(), Substitute.For<IServerSettingsService>(), _service, NullLogger<NotificationHubService>.Instance);
        var raised = 0;
        hub.NotificationReceived += (_, _, _) => raised++;

        Assert.Equal(shown, hub.Receive(new NotificationHubService.NotificationPayload("t", "b", null, nativePush)));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void HubPayload_OldApiWithoutNativePush_BindsNull_NewApiBindsFlag()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        Assert.Null(JsonSerializer.Deserialize<NotificationHubService.NotificationPayload>("""{"title":"t","body":"b","data":{"invoiceId":"x"}}""", web)!.NativePush);
        Assert.True(JsonSerializer.Deserialize<NotificationHubService.NotificationPayload>("""{"title":"t","body":"b","data":null,"nativePush":true}""", web)!.NativePush);
    }

    private List<DeviceTokenDto> FakeServer()
    {
        var rows = new List<DeviceTokenDto>();
        _api.GetDevicesAsync(Arg.Any<string?>()).Returns(_ => rows.ToList());
        _api.UnregisterDeviceAsync(Arg.Any<Guid>(), Arg.Any<string?>()).Returns(call =>
        {
            rows.RemoveAll(r => r.Id == call.Arg<Guid>());
            return Task.CompletedTask;
        });
        return rows;
    }

    private static void Store(List<DeviceTokenDto> rows, RegisterDeviceTokenRequest request)
    {
        rows.RemoveAll(r => r.Token == request.Token);
        rows.Add(new DeviceTokenDto { Id = Guid.NewGuid(), Token = request.Token });
    }

    private void PostsWaitFor(List<DeviceTokenDto> rows, params TaskCompletionSource[] gates)
    {
        var queue = new Queue<TaskCompletionSource>(gates);
        _api.RegisterDeviceTokenAsync(Arg.Any<RegisterDeviceTokenRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var request = call.Arg<RegisterDeviceTokenRequest>();
            await queue.Dequeue().Task;
            Store(rows, request);
        });
    }

    [Fact]
    public async Task SignOut_CancelsInFlightPost()
    {
        FakeServer();
        CancellationToken posted = default;
        _api.RegisterDeviceTokenAsync(Arg.Any<RegisterDeviceTokenRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            posted = call.Arg<CancellationToken>();
            await Task.Delay(Timeout.Infinite, posted);
        });

        var registration = _service.EnsureDeviceRegisteredAsync();
        using (var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
            await _service.UnregisterCurrentDeviceAsync(budget.Token);

        Assert.True(posted.IsCancellationRequested);
        await registration.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(_service.SignOutCleanupPending);
    }

    [Fact]
    public async Task LateRegistration_AfterBudget_CleanupRemovesRowAndLogsOutOnce()
    {
        var rows = FakeServer();
        var post = new TaskCompletionSource();
        PostsWaitFor(rows, post);
        _auth.GetDeferredLogoutAccessTokenAsync().Returns("old-access");

        var registration = _service.RegisterTokenAsync("fcm-late", "Android");
        using (var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            await _service.UnregisterCurrentDeviceAsync(budget.Token);
        Assert.True(_service.SignOutCleanupPending);

        var cleanup = _service.CompleteSignOutCleanupAsync();
        post.SetResult();
        await registration.WaitAsync(TimeSpan.FromSeconds(5));
        await cleanup.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(rows);
        await _api.Received().UnregisterDeviceAsync(Arg.Any<Guid>(), "old-access");
        await _auth.Received(1).CompleteDeferredLogoutAsync();
        Assert.False(_service.SignOutCleanupPending);
        Assert.Equal("", Preferences.Default.Get(TokenKey, ""));
    }

    [Fact]
    public async Task OfflineSignOut_KeepsPending_NextStartRemovesRowAndLogsOut()
    {
        var rows = FakeServer();
        var post = new TaskCompletionSource();
        post.SetResult();
        PostsWaitFor(rows, post);
        await _service.RegisterTokenAsync("fcm-abc", "Android");
        _api.GetDevicesAsync(Arg.Any<string?>()).ThrowsAsync(new HttpRequestException("offline"));

        await _service.UnregisterCurrentDeviceAsync(CancellationToken.None);

        Assert.True(_service.SignOutCleanupPending);
        Assert.Single(rows);

        _api.GetDevicesAsync(Arg.Any<string?>()).Returns(_ => rows.ToList());
        _auth.GetDeferredLogoutAccessTokenAsync().Returns("old-access");
        var nextStart = new DeviceTokenService(_api, _auth);
        await nextStart.CompleteSignOutCleanupAsync();

        Assert.Empty(rows);
        await _auth.Received(1).CompleteDeferredLogoutAsync();
        Assert.False(nextStart.SignOutCleanupPending);
    }

    [Fact]
    public async Task Cleanup_StillOffline_KeepsPendingAndDeferredLogout()
    {
        Preferences.Default.Set(TokenKey, "fcm-abc");
        _api.GetDevicesAsync(Arg.Any<string?>()).ThrowsAsync(new HttpRequestException("offline"));
        await _service.UnregisterCurrentDeviceAsync(CancellationToken.None);
        _auth.GetDeferredLogoutAccessTokenAsync().ThrowsAsync(new HttpRequestException("offline"));

        await _service.CompleteSignOutCleanupAsync();

        Assert.True(_service.SignOutCleanupPending);
        await _auth.DidNotReceive().CompleteDeferredLogoutAsync();
    }

    [Fact]
    public async Task Cleanup_SessionGone_ClearsPendingWithoutDelete()
    {
        Preferences.Default.Set(TokenKey, "fcm-abc");
        _api.GetDevicesAsync(Arg.Any<string?>()).ThrowsAsync(new HttpRequestException("offline"));
        await _service.UnregisterCurrentDeviceAsync(CancellationToken.None);
        _auth.GetDeferredLogoutAccessTokenAsync().Returns((string?)null);

        await _service.CompleteSignOutCleanupAsync();

        Assert.False(_service.SignOutCleanupPending);
        await _api.DidNotReceiveWithAnyArgs().UnregisterDeviceAsync(default);
    }

    [Fact]
    public async Task AbandonedPost_CleanupOnSettleKeepsPending_NextStartRuns()
    {
        var rows = FakeServer();
        _api.RegisterDeviceTokenAsync(Arg.Any<RegisterDeviceTokenRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
            await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>()));
        _auth.GetDeferredLogoutAccessTokenAsync().Returns("old-access");

        var registration = _service.RegisterTokenAsync("fcm-abandoned", "Android");
        using (var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
            await _service.UnregisterCurrentDeviceAsync(budget.Token);
        await registration.WaitAsync(TimeSpan.FromSeconds(5));
        await _service.CompleteSignOutCleanupAsync();

        Assert.True(_service.SignOutCleanupPending);
        await _auth.DidNotReceive().CompleteDeferredLogoutAsync();

        rows.Add(new DeviceTokenDto { Id = Guid.NewGuid(), Token = "fcm-abandoned" });
        await new DeviceTokenService(_api, _auth).CompleteSignOutCleanupAsync();

        Assert.Empty(rows);
        Assert.False(_service.SignOutCleanupPending);
    }

    [Fact]
    public async Task PostTimedOutBeforeSignOut_SignOutDefersCleanup_NextStartRemovesLateRow()
    {
        var rows = FakeServer();
        _api.RegisterDeviceTokenAsync(Arg.Any<RegisterDeviceTokenRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException(System.Net.HttpStatusCode.ServiceUnavailable, "no connection"));
        _auth.GetDeferredLogoutAccessTokenAsync().Returns("old-access");

        await _service.EnsureDeviceRegisteredAsync();
        await _service.UnregisterCurrentDeviceAsync(CancellationToken.None);
        await _service.CompleteSignOutCleanupAsync();

        Assert.True(_service.SignOutCleanupPending);
        await _auth.DidNotReceive().CompleteDeferredLogoutAsync();

        var deviceId = Preferences.Default.Get(DeviceIdKey, "");
        rows.Add(new DeviceTokenDto { Id = Guid.NewGuid(), Token = deviceId });
        var nextStart = new DeviceTokenService(_api, _auth);
        await nextStart.CompleteSignOutCleanupAsync();

        Assert.Empty(rows);
        await _auth.Received(1).CompleteDeferredLogoutAsync();
        Assert.False(nextStart.SignOutCleanupPending);
    }

    [Fact]
    public async Task PostRejectedByServer_SignOutStaysClean()
    {
        FakeServer();
        _api.RegisterDeviceTokenAsync(Arg.Any<RegisterDeviceTokenRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException(System.Net.HttpStatusCode.BadRequest, "rejected"));

        await _service.EnsureDeviceRegisteredAsync();
        await _service.UnregisterCurrentDeviceAsync(CancellationToken.None);

        Assert.False(_service.SignOutCleanupPending);
    }

    [Fact]
    public async Task SignOut_WaitsForTokenRefreshRegistration()
    {
        var rows = FakeServer();
        var post = new TaskCompletionSource();
        PostsWaitFor(rows, post);

        var onNewToken = _service.RegisterTokenAsync("fcm-rotated", "Android");
        var signOut = _service.UnregisterCurrentDeviceAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.False(signOut.IsCompleted);

        post.SetResult();
        await signOut.WaitAsync(TimeSpan.FromSeconds(5));
        await onNewToken;

        Assert.Empty(rows);
        Assert.False(_service.SignOutCleanupPending);
    }

    [Fact]
    public async Task OverlappingRegistrations_HungSessionAThenLoginB_SignOutWaitsForBoth()
    {
        var rows = FakeServer();
        var postA = new TaskCompletionSource();
        PostsWaitFor(rows, postA);
        _auth.GetDeferredLogoutAccessTokenAsync().Returns("old-access");

        var a = _service.RegisterTokenAsync("fcm-a", "Android");
        using (var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
            await _service.UnregisterCurrentDeviceAsync(budget.Token);
        var b = _service.RegisterTokenAsync("fcm-b", "Android");

        var signOutB = _service.UnregisterCurrentDeviceAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.False(signOutB.IsCompleted);
        postA.SetResult();
        await signOutB.WaitAsync(TimeSpan.FromSeconds(5));
        await a;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b);
        await _service.CompleteSignOutCleanupAsync().WaitAsync(TimeSpan.FromSeconds(5));

        await _api.Received(1).RegisterDeviceTokenAsync(Arg.Any<RegisterDeviceTokenRequest>(), Arg.Any<CancellationToken>());
        Assert.Empty(rows);
        Assert.False(_service.SignOutCleanupPending);
    }

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

        await _api.Received(1).RegisterDeviceTokenAsync(Arg.Is<RegisterDeviceTokenRequest>(r => r.Token == "fcm-abc" && r.Platform == 1 && r.TenantId == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterToken_RefreshedToken_RegistersAndStores()
    {
        Preferences.Default.Set(TokenKey, "fcm-old");

        await _service.RegisterTokenAsync("fcm-new", "Android");

        await _api.Received(1).RegisterDeviceTokenAsync(Arg.Is<RegisterDeviceTokenRequest>(r => r.Token == "fcm-new" && r.Platform == 0), Arg.Any<CancellationToken>());
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
        await _api.Received(1).RegisterDeviceTokenAsync(Arg.Is<RegisterDeviceTokenRequest>(r => r.Token == deviceId && r.Platform == 1 && r.TenantId == null), Arg.Any<CancellationToken>());
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
        await _api.Received(1).RegisterDeviceTokenAsync(Arg.Is<RegisterDeviceTokenRequest>(r => r.Token == "fcm-abc" && r.TenantId == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnableNotifications_NoStoredToken_RegistersAndPersistsDeviceId()
    {
        Assert.True(await _service.EnableNotificationsAsync());

        var deviceId = Preferences.Default.Get(DeviceIdKey, "");
        Assert.Equal(deviceId, Preferences.Default.Get(TokenKey, ""));
        await _api.Received(1).RegisterDeviceTokenAsync(Arg.Is<RegisterDeviceTokenRequest>(r => r.Token == deviceId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnableNotifications_ApiFailure_Throws()
    {
        _api.RegisterDeviceTokenAsync(default!).ThrowsAsyncForAnyArgs(new HttpRequestException());

        await Assert.ThrowsAsync<HttpRequestException>(() => _service.EnableNotificationsAsync());
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

        await _service.UnregisterCurrentDeviceAsync(CancellationToken.None);

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

        await _service.UnregisterCurrentDeviceAsync(CancellationToken.None);
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
    public async Task EnsureDeviceRegistered_TurnedOffDuringPost_RemovesDeviceAfterPost()
    {
        var post = new TaskCompletionSource();
        _api.RegisterDeviceTokenAsync(Arg.Any<RegisterDeviceTokenRequest>(), Arg.Any<CancellationToken>()).Returns(post.Task);

        var registration = _service.EnsureDeviceRegisteredAsync();
        Assert.True(await _service.DisableNotificationsAsync());
        var deviceId = Preferences.Default.Get(DeviceIdKey, "");
        var mine = new DeviceTokenDto { Id = Guid.NewGuid(), Token = deviceId };
        _api.GetDevicesAsync().Returns([mine]);

        post.SetResult();
        await registration;

        await _api.Received(1).UnregisterDeviceAsync(mine.Id);
        Assert.False(_service.PushRemovalPending);
    }

    [Fact]
    public async Task Disable_TimedOut_NoLateDelete_RetriesAtNextLogin()
    {
        Preferences.Default.Set(TokenKey, "fcm-abc");
        var mine = new DeviceTokenDto { Id = Guid.NewGuid(), Token = "fcm-abc" };
        var devices = new TaskCompletionSource<List<DeviceTokenDto>>();
        _api.GetDevicesAsync().Returns(devices.Task);

        var limit = DeviceTokenService.ServerRemovalLimit;
        DeviceTokenService.ServerRemovalLimit = TimeSpan.FromMilliseconds(100);
        try
        {
            Assert.False(await _service.DisableNotificationsAsync());
            Assert.True(_service.PushDisabledByUser);
            Assert.True(_service.PushRemovalPending);

            devices.SetResult([mine]);
            await Task.Delay(300);
            await _api.DidNotReceiveWithAnyArgs().UnregisterDeviceAsync(default);
        }
        finally
        {
            DeviceTokenService.ServerRemovalLimit = limit;
        }

        _api.GetDevicesAsync().Returns([mine]);
        await _service.EnsureDeviceRegisteredAsync();

        await _api.Received(1).UnregisterDeviceAsync(mine.Id);
        Assert.False(_service.PushRemovalPending);
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

        await Assert.ThrowsAsync<HttpRequestException>(() => _service.EnableNotificationsAsync());

        Assert.True(_service.PushDisabledByUser);
    }
}
