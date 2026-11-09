using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

[Collection("Shell")]
public class DeviceTokenServiceTests : IDisposable
{
    private const string TokenKey = "device_push_token";
    private const string RegisteredKey = "device_registered";
    private const string DeviceIdKey = "device_stable_id";
    private const string PushDisabledKey = "push_disabled_by_user";

    private readonly IApiService _api = Substitute.For<IApiService>();
    private readonly IAuthService _auth = Substitute.For<IAuthService>();
    private readonly DeviceTokenService _service;

    private readonly TimeSpan _lateRegistrationWait = DeviceTokenService.LateRegistrationWait;

    public DeviceTokenServiceTests()
    {
        DeviceTokenService.LateRegistrationWait = TimeSpan.FromMilliseconds(300);
        Preferences.Default.Clear();
        Permissions.Reset();
        DeviceInfo.Platform = DevicePlatform.Android;
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).Returns(new List<DeviceTokenDto>());
        _auth.CompleteDeferredLogoutAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
        _service = new DeviceTokenService(_api, _auth);
    }

    public void Dispose() => DeviceTokenService.LateRegistrationWait = _lateRegistrationWait;

    private static string NewSignOutId() => Guid.NewGuid().ToString("N");

    private void ServerHas(params string[] tokens) =>
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).Returns(tokens.Select(t => new DeviceTokenDto { Id = Guid.NewGuid(), Token = t }).ToList());

    [Fact]
    public async Task ShowHubNotification_OnlyWithoutNativeTokenAndWithPushOn()
    {
        Assert.True(_service.ShowHubNotification(true));

        await _service.EnsureDeviceRegisteredAsync();
        Assert.StartsWith("device-", Preferences.Default.Get(TokenKey, ""));
        Assert.True(_service.ShowHubNotification(true));

        await _service.RegisterTokenAsync("fcm-token-123", "Android");
        Assert.False(_service.ShowHubNotification(true));

        await _service.UnregisterCurrentDeviceAsync(NewSignOutId(), CancellationToken.None);
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
        _api.GetDevicesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ => rows.ToList());
        _api.UnregisterDeviceAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
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
            await _service.UnregisterCurrentDeviceAsync(NewSignOutId(), budget.Token);

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
        _auth.GetDeferredLogoutAccessTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("old-access");

        var registration = _service.RegisterTokenAsync("fcm-late", "Android");
        using (var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            await _service.UnregisterCurrentDeviceAsync(NewSignOutId(), budget.Token);
        Assert.True(_service.SignOutCleanupPending);

        var cleanup = _service.CompleteSignOutCleanupAsync();
        post.SetResult();
        await registration.WaitAsync(TimeSpan.FromSeconds(5));
        await cleanup.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(rows);
        await _api.Received().UnregisterDeviceAsync(Arg.Any<Guid>(), "old-access", Arg.Any<CancellationToken>());
        await _auth.Received(1).CompleteDeferredLogoutAsync(Arg.Any<string>());
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
        _api.GetDevicesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));

        await _service.UnregisterCurrentDeviceAsync(NewSignOutId(), CancellationToken.None);

        Assert.True(_service.SignOutCleanupPending);
        Assert.Single(rows);

        _api.GetDevicesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ => rows.ToList());
        _auth.GetDeferredLogoutAccessTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("old-access");
        var nextStart = new DeviceTokenService(_api, _auth);
        await nextStart.CompleteSignOutCleanupAsync();

        Assert.Empty(rows);
        await _auth.Received(1).CompleteDeferredLogoutAsync(Arg.Any<string>());
        Assert.False(nextStart.SignOutCleanupPending);
    }

    [Fact]
    public async Task Cleanup_StillOffline_KeepsPendingAndDeferredLogout()
    {
        Preferences.Default.Set(TokenKey, "fcm-abc");
        _api.GetDevicesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));
        await _service.UnregisterCurrentDeviceAsync(NewSignOutId(), CancellationToken.None);
        _auth.GetDeferredLogoutAccessTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));

        await _service.CompleteSignOutCleanupAsync();

        Assert.True(_service.SignOutCleanupPending);
        await _auth.DidNotReceive().CompleteDeferredLogoutAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Cleanup_SessionGone_ClearsPendingWithoutDelete()
    {
        Preferences.Default.Set(TokenKey, "fcm-abc");
        _api.GetDevicesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));
        await _service.UnregisterCurrentDeviceAsync(NewSignOutId(), CancellationToken.None);
        _auth.GetDeferredLogoutAccessTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);

        await _service.CompleteSignOutCleanupAsync();

        Assert.False(_service.SignOutCleanupPending);
        await _api.DidNotReceiveWithAnyArgs().UnregisterDeviceAsync(default, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AbandonedPost_CleanupWaitsForLateRow_ThenRunsWithoutRestart()
    {
        var rows = FakeServer();
        _api.RegisterDeviceTokenAsync(Arg.Any<RegisterDeviceTokenRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
            await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>()));
        _auth.GetDeferredLogoutAccessTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("old-access");

        var registration = _service.RegisterTokenAsync("fcm-abandoned", "Android");
        using (var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
            Assert.False(await _service.UnregisterCurrentDeviceAsync(NewSignOutId(), budget.Token));
        await registration.WaitAsync(TimeSpan.FromSeconds(5));
        var cleanup = _service.CompleteSignOutCleanupAsync();

        await Task.Delay(100);
        Assert.False(cleanup.IsCompleted);
        await _auth.DidNotReceiveWithAnyArgs().GetDeferredLogoutAccessTokenAsync(default!, default);
        rows.Add(new DeviceTokenDto { Id = Guid.NewGuid(), Token = "fcm-abandoned" });
        await cleanup.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(rows);
        await _auth.Received(1).CompleteDeferredLogoutAsync(Arg.Any<string>());
        Assert.False(_service.SignOutCleanupPending);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("timeout")]
    [InlineData("network")]
    [InlineData("5xx")]
    public async Task PostAbandonedInSessionA_SessionBSignOutStillConfirmed(string outcome)
    {
        var rows = FakeServer();
        _api.RegisterDeviceTokenAsync(Arg.Any<RegisterDeviceTokenRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            switch (outcome)
            {
                case "cancel": await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>()); break;
                case "timeout": throw new ApiException(System.Net.HttpStatusCode.ServiceUnavailable, "timeout");
                case "network": throw new HttpRequestException("reset");
                default: throw new ApiException(System.Net.HttpStatusCode.InternalServerError, "boom");
            }
        });

        var a = _service.RegisterTokenAsync("fcm-a", "Android");
        using (var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
            Assert.False(await _service.UnregisterCurrentDeviceAsync("a", budget.Token));
        await a.WaitAsync(TimeSpan.FromSeconds(5));

        _api.RegisterDeviceTokenAsync(Arg.Any<RegisterDeviceTokenRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Store(rows, call.Arg<RegisterDeviceTokenRequest>());
            return Task.CompletedTask;
        });
        await _service.RegisterTokenAsync("fcm-b", "Android");

        Assert.True(await _service.UnregisterCurrentDeviceAsync("b", CancellationToken.None));
        Assert.Empty(rows);
        Assert.Equal(["a"], PendingIds());
    }

    [Fact]
    public async Task SecondDeferredSignOut_KeepsFirst_CleanupDeletesEachWithOwnBearer()
    {
        var rows = new Dictionary<string, List<DeviceTokenDto>> { ["a-access"] = [], ["b-access"] = [] };
        _api.GetDevicesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<string?>() is { } bearer
            ? rows[bearer].ToList()
            : throw new HttpRequestException("offline"));
        _api.UnregisterDeviceAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            rows[call.Arg<string?>()!].RemoveAll(r => r.Id == call.Arg<Guid>());
            return Task.CompletedTask;
        });
        _auth.GetDeferredLogoutAccessTokenAsync("a", Arg.Any<CancellationToken>()).Returns("a-access");
        _auth.GetDeferredLogoutAccessTokenAsync("b", Arg.Any<CancellationToken>()).Returns("b-access");

        Preferences.Default.Set(TokenKey, "fcm-a");
        Assert.False(await _service.UnregisterCurrentDeviceAsync("a", CancellationToken.None));
        Preferences.Default.Set(TokenKey, "fcm-b");
        Assert.False(await _service.UnregisterCurrentDeviceAsync("b", CancellationToken.None));
        rows["a-access"].Add(new DeviceTokenDto { Id = Guid.NewGuid(), Token = "fcm-a" });
        rows["b-access"].Add(new DeviceTokenDto { Id = Guid.NewGuid(), Token = "fcm-b" });

        await _service.CompleteSignOutCleanupAsync();

        Assert.All(rows.Values, Assert.Empty);
        await _auth.Received(1).CompleteDeferredLogoutAsync("a");
        await _auth.Received(1).CompleteDeferredLogoutAsync("b");
        Assert.False(_service.SignOutCleanupPending);
    }

    [Fact]
    public async Task ConfirmedSignOutB_LeavesPendingTokensOfA()
    {
        var rows = FakeServer();
        _auth.GetDeferredLogoutAccessTokenAsync("a", Arg.Any<CancellationToken>()).Returns("a-access");
        Preferences.Default.Set(TokenKey, "fcm-a");
        Preferences.Default.Set(DeviceIdKey, "device-a");
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));
        Assert.False(await _service.UnregisterCurrentDeviceAsync("a", CancellationToken.None));

        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).Returns(_ => new List<DeviceTokenDto>());
        Preferences.Default.Set(TokenKey, "fcm-b");
        Assert.True(await _service.UnregisterCurrentDeviceAsync("b", CancellationToken.None));
        Assert.Equal(["a"], PendingIds());

        rows.Add(new DeviceTokenDto { Id = Guid.NewGuid(), Token = "device-a" });
        await _service.CompleteSignOutCleanupAsync();

        Assert.Empty(rows);
        await _api.Received(1).UnregisterDeviceAsync(Arg.Any<Guid>(), "a-access", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PendingSignOuts_BoundedToFive_EvictedSessionLoggedOut()
    {
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));

        for (var i = 0; i < 7; i++)
        {
            Preferences.Default.Set(TokenKey, $"fcm-{i}");
            await _service.UnregisterCurrentDeviceAsync($"s{i}", CancellationToken.None);
        }

        Assert.Equal(["s2", "s3", "s4", "s5", "s6"], PendingIds());
        await _auth.Received(1).CompleteDeferredLogoutAsync("s0", true);
        await _auth.Received(1).CompleteDeferredLogoutAsync("s1", true);
    }

    [Fact]
    public async Task Cleanup_RefreshHangs_LimitEndsRun_NextRunCompletes()
    {
        var rows = FakeServer();
        Preferences.Default.Set(TokenKey, "fcm-a");
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));
        await _service.UnregisterCurrentDeviceAsync("a", CancellationToken.None);
        rows.Add(new DeviceTokenDto { Id = Guid.NewGuid(), Token = "fcm-a" });
        _auth.GetDeferredLogoutAccessTokenAsync("a", Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<string?>().Task, Task.FromResult<string?>("a-access"));
        var limit = DeviceTokenService.ServerRemovalLimit;
        DeviceTokenService.ServerRemovalLimit = TimeSpan.FromMilliseconds(100);
        try
        {
            await _service.CompleteSignOutCleanupAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(_service.SignOutCleanupPending);

            await _service.CompleteSignOutCleanupAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            DeviceTokenService.ServerRemovalLimit = limit;
        }

        Assert.Empty(rows);
        Assert.False(_service.SignOutCleanupPending);
    }

    [Fact]
    public async Task Cleanup_SkippedWhileSignOutUnderway()
    {
        Preferences.Default.Set(TokenKey, "fcm-a");
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));
        await _service.UnregisterCurrentDeviceAsync("a", CancellationToken.None);
        _auth.LogoutUnderway.Returns(true);

        await _service.CompleteSignOutCleanupAsync();

        await _auth.DidNotReceiveWithAnyArgs().GetDeferredLogoutAccessTokenAsync(default!, default);
        Assert.Equal(["a"], PendingIds());
    }

    [Fact]
    public async Task Cleanup_KeycloakLogoutFails_KeepsEntry_NextRunLogsOutAndClears()
    {
        var rows = FakeServer();
        Preferences.Default.Set(TokenKey, "fcm-a");
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));
        await _service.UnregisterCurrentDeviceAsync("a", CancellationToken.None);
        rows.Add(new DeviceTokenDto { Id = Guid.NewGuid(), Token = "fcm-a" });
        _auth.GetDeferredLogoutAccessTokenAsync("a", Arg.Any<CancellationToken>()).Returns("a-access");
        _auth.CompleteDeferredLogoutAsync("a", false).Returns(false, true);

        await _service.CompleteSignOutCleanupAsync();

        Assert.Empty(rows);
        Assert.Equal(["a"], PendingIds());

        await new DeviceTokenService(_api, _auth).CompleteSignOutCleanupAsync();

        await _auth.Received(2).CompleteDeferredLogoutAsync("a", false);
        Assert.False(_service.SignOutCleanupPending);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("delete")]
    public async Task Cleanup_LimitFires_CancelsServerCall(string hangs)
    {
        Preferences.Default.Set(TokenKey, "fcm-a");
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));
        await _service.UnregisterCurrentDeviceAsync("a", CancellationToken.None);
        _auth.GetDeferredLogoutAccessTokenAsync("a", Arg.Any<CancellationToken>()).Returns("a-access");
        var cancelled = new TaskCompletionSource();
        async Task Hang(CancellationToken ct)
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { cancelled.TrySetResult(); }
        }
        _api.GetDevicesAsync("a-access", Arg.Any<CancellationToken>()).Returns(async call =>
        {
            if (hangs == "get")
                await Hang(call.Arg<CancellationToken>());
            return [new DeviceTokenDto { Id = Guid.NewGuid(), Token = "fcm-a" }];
        });
        _api.UnregisterDeviceAsync(Arg.Any<Guid>(), "a-access", Arg.Any<CancellationToken>()).Returns(call => Hang(call.Arg<CancellationToken>()));
        var limit = DeviceTokenService.ServerRemovalLimit;
        DeviceTokenService.ServerRemovalLimit = TimeSpan.FromMilliseconds(100);
        try
        {
            await _service.CompleteSignOutCleanupAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            DeviceTokenService.ServerRemovalLimit = limit;
        }

        Assert.Equal(["a"], PendingIds());
        await _auth.DidNotReceiveWithAnyArgs().CompleteDeferredLogoutAsync(default!);
    }

    private static string[] PendingIds() =>
        JsonSerializer.Deserialize<JsonElement[]>(Preferences.Default.Get("pending_signout_sessions", "[]"))!
            .Select(e => e.GetProperty("Id").GetString()!).ToArray();

    [Fact]
    public async Task PostTimedOutBeforeSignOut_SignOutDefersCleanup_SameProcessRemovesLateRow()
    {
        var rows = FakeServer();
        _api.RegisterDeviceTokenAsync(Arg.Any<RegisterDeviceTokenRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException(System.Net.HttpStatusCode.ServiceUnavailable, "no connection"));
        _auth.GetDeferredLogoutAccessTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("old-access");

        await _service.EnsureDeviceRegisteredAsync();
        Assert.False(await _service.UnregisterCurrentDeviceAsync(NewSignOutId(), CancellationToken.None));
        var cleanup = _service.CompleteSignOutCleanupAsync();
        var deviceId = Preferences.Default.Get(DeviceIdKey, "");
        rows.Add(new DeviceTokenDto { Id = Guid.NewGuid(), Token = deviceId });
        await cleanup.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(rows);
        await _auth.Received(1).CompleteDeferredLogoutAsync(Arg.Any<string>());
        Assert.False(_service.SignOutCleanupPending);
    }

    [Fact]
    public async Task PostRejectedByServer_SignOutStaysClean()
    {
        FakeServer();
        _api.RegisterDeviceTokenAsync(Arg.Any<RegisterDeviceTokenRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException(System.Net.HttpStatusCode.BadRequest, "rejected"));

        await _service.EnsureDeviceRegisteredAsync();
        await _service.UnregisterCurrentDeviceAsync(NewSignOutId(), CancellationToken.None);

        Assert.False(_service.SignOutCleanupPending);
    }

    [Fact]
    public async Task SignOut_WaitsForTokenRefreshRegistration()
    {
        var rows = FakeServer();
        var post = new TaskCompletionSource();
        PostsWaitFor(rows, post);

        var onNewToken = _service.RegisterTokenAsync("fcm-rotated", "Android");
        var signOut = _service.UnregisterCurrentDeviceAsync(NewSignOutId(), CancellationToken.None);
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
        _auth.GetDeferredLogoutAccessTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("old-access");

        var a = _service.RegisterTokenAsync("fcm-a", "Android");
        using (var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
            await _service.UnregisterCurrentDeviceAsync(NewSignOutId(), budget.Token);
        var b = _service.RegisterTokenAsync("fcm-b", "Android");

        var signOutB = _service.UnregisterCurrentDeviceAsync(NewSignOutId(), CancellationToken.None);
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
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));

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
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).Returns([mine, mineById, other]);

        await _service.UnregisterCurrentDeviceAsync(NewSignOutId(), CancellationToken.None);

        await _api.Received(1).UnregisterDeviceAsync(mine.Id, null, Arg.Any<CancellationToken>());
        await _api.Received(1).UnregisterDeviceAsync(mineById.Id, null, Arg.Any<CancellationToken>());
        await _api.DidNotReceive().UnregisterDeviceAsync(other.Id, null, Arg.Any<CancellationToken>());
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
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).Returns([mine, mineById, other]);

        Assert.True(await _service.DisableNotificationsAsync());

        await _api.Received(1).UnregisterDeviceAsync(mine.Id, null, Arg.Any<CancellationToken>());
        await _api.Received(1).UnregisterDeviceAsync(mineById.Id, null, Arg.Any<CancellationToken>());
        await _api.DidNotReceive().UnregisterDeviceAsync(other.Id, null, Arg.Any<CancellationToken>());
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
        await _api.DidNotReceive().GetDevicesAsync(null, Arg.Any<CancellationToken>());
        Assert.Equal("", Preferences.Default.Get(TokenKey, ""));
    }

    [Fact]
    public async Task Logout_WhileOff_KeepsChoice_NextLoginDoesNotRegister()
    {
        Preferences.Default.Set(DeviceIdKey, "device-abc");
        await _service.DisableNotificationsAsync();

        await _service.UnregisterCurrentDeviceAsync(NewSignOutId(), CancellationToken.None);
        await _service.EnsureDeviceRegisteredAsync();

        Assert.True(_service.PushDisabledByUser);
        await _api.DidNotReceiveWithAnyArgs().RegisterDeviceTokenAsync(default!);
    }

    [Fact]
    public async Task Disable_Offline_ReturnsFalse_KeepsChoiceAndToken()
    {
        Preferences.Default.Set(TokenKey, "fcm-abc");
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));

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
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).Returns([mine]);

        post.SetResult();
        await registration;

        await _api.Received(1).UnregisterDeviceAsync(mine.Id, null, Arg.Any<CancellationToken>());
        Assert.False(_service.PushRemovalPending);
    }

    [Fact]
    public async Task Disable_TimedOut_NoLateDelete_RetriesAtNextLogin()
    {
        Preferences.Default.Set(TokenKey, "fcm-abc");
        var mine = new DeviceTokenDto { Id = Guid.NewGuid(), Token = "fcm-abc" };
        var devices = new TaskCompletionSource<List<DeviceTokenDto>>();
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).Returns(devices.Task);

        var limit = DeviceTokenService.ServerRemovalLimit;
        DeviceTokenService.ServerRemovalLimit = TimeSpan.FromMilliseconds(100);
        try
        {
            Assert.False(await _service.DisableNotificationsAsync());
            Assert.True(_service.PushDisabledByUser);
            Assert.True(_service.PushRemovalPending);

            devices.SetResult([mine]);
            await Task.Delay(300);
            await _api.DidNotReceiveWithAnyArgs().UnregisterDeviceAsync(default, null, Arg.Any<CancellationToken>());
        }
        finally
        {
            DeviceTokenService.ServerRemovalLimit = limit;
        }

        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).Returns([mine]);
        await _service.EnsureDeviceRegisteredAsync();

        await _api.Received(1).UnregisterDeviceAsync(mine.Id, null, Arg.Any<CancellationToken>());
        Assert.False(_service.PushRemovalPending);
    }

    [Fact]
    public async Task EnsureDeviceRegistered_AfterOfflineDisable_RetriesDeleteAndClearsKeys()
    {
        Preferences.Default.Set(TokenKey, "fcm-abc");
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));
        await _service.DisableNotificationsAsync();
        var mine = new DeviceTokenDto { Id = Guid.NewGuid(), Token = "fcm-abc" };
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).Returns([mine]);

        await _service.EnsureDeviceRegisteredAsync();

        await _api.Received(1).UnregisterDeviceAsync(mine.Id, null, Arg.Any<CancellationToken>());
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
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).Returns([first, second]);
        _api.UnregisterDeviceAsync(second.Id, null, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));

        Assert.False(await _service.DisableNotificationsAsync());
        Assert.Equal("fcm-abc", Preferences.Default.Get(TokenKey, ""));

        _api.UnregisterDeviceAsync(second.Id, null, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).Returns([second]);
        await _service.EnsureDeviceRegisteredAsync();

        await _api.Received(2).UnregisterDeviceAsync(second.Id, null, Arg.Any<CancellationToken>());
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
