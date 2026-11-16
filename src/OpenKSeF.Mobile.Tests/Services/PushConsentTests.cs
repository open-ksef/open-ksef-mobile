using System.Net;
using NSubstitute;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

[Collection("Shell")]
public class PushConsentTests
{
    private readonly IApiService _api = Substitute.For<IApiService>();
    private readonly IAuthService _auth = Substitute.For<IAuthService>();
    private readonly DeviceTokenService _device;
    private readonly List<(Guid Id, string User, string Token)> _rows = [];
    private readonly List<string> _posts = [];
    private string _user = "a";
    private Func<CancellationToken, Task>? _post;

    public PushConsentTests()
    {
        Shell.Current.Reset();
        Preferences.Default.Clear();
        Permissions.Reset();
        DeviceInfo.Platform = DevicePlatform.Android;
        _api.GetMeAsync().Returns(_ => new UserInfo { UserId = _user });
        _api.GetOnboardingStatusAsync().Returns(new OnboardingStatusDto { IsComplete = true });
        _api.RegisterDeviceTokenAsync(Arg.Any<RegisterDeviceTokenRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var user = _user;
            if (_post is not null)
                await _post(call.Arg<CancellationToken>());
            _posts.Add(user);
            _rows.Add((Guid.NewGuid(), user, call.Arg<RegisterDeviceTokenRequest>().Token));
        });
        _api.GetDevicesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ =>
            _rows.Where(r => r.User == _user).Select(r => new DeviceTokenDto { Id = r.Id, Token = r.Token }).ToList());
        _api.UnregisterDeviceAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _rows.RemoveAll(r => r.Id == call.Arg<Guid>() && r.User == _user);
            return Task.CompletedTask;
        });
        _auth.CompleteDeferredLogoutAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
        _device = new DeviceTokenService(_api, _auth);
    }

    private PostLoginNavigationService Navigation()
    {
        var settings = Substitute.For<IServerSettingsService>();
        settings.ServerUrl.Returns("https://ksef.example.com");
        return new(_api, _auth, _device, Substitute.For<INotificationHubService>(), settings, new LocalDbService());
    }

    private int Prompts => Shell.Current.Alerts.Count(a => a.Message == PostLoginNavigationService.PushConsentMessage);

    [Fact]
    public async Task FreshInstall_Declines_NoRegistration_NoPermissionPrompt_SwitchOff_AskedOnce()
    {
        Permissions.Status = PermissionStatus.Denied;
        Shell.Current.AlertAnswer = false;

        await Navigation().NavigateAsync();
        await Navigation().NavigateAsync();

        Assert.Equal(1, Prompts);
        Assert.Empty(_posts);
        Assert.Equal(0, Permissions.RequestCount);
        Assert.False(await _device.AreNotificationsEnabledAsync());
    }

    [Fact]
    public async Task FreshInstall_Accepts_PermissionAfterTak_OneRegistration_SwitchOn()
    {
        Permissions.Status = PermissionStatus.Denied;
        var requestsWhilePrompt = -1;
        Shell.Current.WhileConfirming = () =>
        {
            requestsWhilePrompt = Permissions.RequestCount;
            return Task.CompletedTask;
        };

        await Navigation().NavigateAsync();
        Permissions.Status = PermissionStatus.Granted;
        await Navigation().NavigateAsync();

        Assert.Equal(0, requestsWhilePrompt);
        Assert.Equal(1, Permissions.RequestCount);
        Assert.Equal(["a"], _posts);
        Assert.Equal(1, Prompts);
        Assert.True(await _device.AreNotificationsEnabledAsync());
    }

    [Fact]
    public async Task Tak_PermissionDenied_NoRegistration_SwitchOff_NotAskedAgain()
    {
        Permissions.Status = PermissionStatus.Denied;
        Permissions.RequestResult = PermissionStatus.Denied;

        await Navigation().NavigateAsync();
        await Navigation().NavigateAsync();

        Assert.Empty(_posts);
        Assert.Equal(1, Prompts);
        Assert.True(_device.PushDisabledByUser);
        Assert.False(_device.PushChoicePending);
    }

    [Fact]
    public async Task AccountAOff_BTurnsOn_AStaysOffAndUnregisteredAtNextLogin()
    {
        Shell.Current.AlertAnswer = false;
        await Navigation().NavigateAsync();
        await Navigation().SignOutAsync();

        _user = "b";
        Shell.Current.AlertAnswer = true;
        await Navigation().NavigateAsync();
        Assert.False(_device.PushDisabledByUser);
        await Navigation().SignOutAsync();

        _user = "a";
        await Navigation().NavigateAsync();

        Assert.Equal(["b"], _posts);
        Assert.Equal(2, Prompts);
        Assert.True(_device.PushDisabledByUser);
        Assert.DoesNotContain(_rows, r => r.User == "a");
    }

    [Fact]
    public async Task AccountBOn_SignOut_ALogsIn_IsAskedNotRegistered()
    {
        _user = "b";
        await Navigation().NavigateAsync();
        await Navigation().SignOutAsync();

        _user = "a";
        Shell.Current.AlertAnswer = false;
        await Navigation().NavigateAsync();

        Assert.Equal(["b"], _posts);
        Assert.Equal(2, Prompts);
    }

    [Fact]
    public async Task Upgrade_DisabledOnDevice_MapsToSignedInAccountOnly()
    {
        Preferences.Default.Set("push_disabled_by_user", true);

        await Navigation().RestoreAfterUpgrade(_auth);

        Assert.Equal(0, Prompts);
        Assert.Empty(_posts);
        Assert.True(_device.PushDisabledByUser);
        Assert.False(Preferences.Default.ContainsKey("push_disabled_by_user"));

        await Navigation().SignOutAsync();
        _user = "b";
        Shell.Current.AlertAnswer = false;
        await Navigation().NavigateAsync();

        Assert.Equal(1, Prompts);
    }

    [Fact]
    public async Task Upgrade_RegisteredOnDevice_StaysOnWithoutPromptOrNewPost()
    {
        Preferences.Default.Set("device_push_token", "device-old");
        Preferences.Default.Set("device_registered", true);
        Preferences.Default.Set("device_stable_id", "device-old");
        _rows.Add((Guid.NewGuid(), "a", "device-old"));

        await Navigation().RestoreAfterUpgrade(_auth);

        Assert.Equal(0, Prompts);
        Assert.Empty(_posts);
        Assert.False(_device.PushDisabledByUser);
    }

    [Theory]
    [InlineData("5xx")]
    [InlineData("timeout")]
    public async Task Upgrade_MeFails_KeepsOldRowAndTokenKeys_MigratesAtNextSuccess(string failure)
    {
        Preferences.Default.Set("device_push_token", "device-old");
        Preferences.Default.Set("device_registered", true);
        _rows.Add((Guid.NewGuid(), "a", "device-old"));
        Exception error = failure == "5xx"
            ? new ApiException(HttpStatusCode.ServiceUnavailable, "down")
            : new TaskCanceledException("HttpClient.Timeout");
        _api.GetMeAsync().Returns(Task.FromException<UserInfo>(error));

        await Navigation().RestoreAfterUpgrade(_auth);

        Assert.Single(_rows);
        Assert.Equal("device-old", Preferences.Default.Get("device_push_token", ""));
        Assert.True(Preferences.Default.Get("device_registered", false));
        await _api.DidNotReceiveWithAnyArgs().UnregisterDeviceAsync(default);

        _api.GetMeAsync().Returns(_ => new UserInfo { UserId = _user });
        await Navigation().RestoreAfterUpgrade(_auth);

        Assert.Equal(0, Prompts);
        Assert.Empty(_posts);
        Assert.Single(_rows);
        Assert.False(_device.PushDisabledByUser);
    }

    [Fact]
    public async Task SessionEnded_ClearsAccount_NextUserTokenRefreshNotRegistered()
    {
        await Navigation().NavigateAsync();
        Assert.Equal(["a"], _posts);

        await Navigation().HandleSessionEndedAsync();
        await _device.RegisterTokenAsync("fcm-rotated", "Android");

        Assert.Null(_device.PushAccount);
        Assert.Equal(["a"], _posts);
    }

    [Fact]
    public async Task Upgrade_MigratesOnce_SecondAccountWithStaleTokenKeysIsAsked()
    {
        Preferences.Default.Set("device_push_token", "device-old");
        Preferences.Default.Set("device_registered", true);
        await Navigation().RestoreAfterUpgrade(_auth);

        _user = "b";
        Shell.Current.AlertAnswer = false;
        await Navigation().NavigateAsync();

        Assert.Equal(1, Prompts);
        Assert.Equal(["a"], _posts);
    }

    [Fact]
    public async Task SignedOutWhilePromptOpen_TakDoesNotRegisterOrAskPermission()
    {
        Permissions.Status = PermissionStatus.Denied;
        Shell.Current.WhileConfirming = () => Navigation().SignOutAsync();
        _device.SetAccount("a");

        await Navigation().ActivatePushAsync();

        Assert.Empty(_posts);
        Assert.Equal(0, Permissions.RequestCount);
        Assert.False(Preferences.Default.ContainsKey("push_choice_a"));

        Shell.Current.WhileConfirming = null;
        Shell.Current.AlertAnswer = false;
        await Navigation().NavigateAsync();
        Assert.Equal(2, Prompts);
    }

    [Fact]
    public async Task OtherAccountSignedInWhilePromptOpen_AnswerIsDropped()
    {
        Shell.Current.WhileConfirming = async () =>
        {
            await Navigation().SignOutAsync();
            _device.SetAccount("b");
        };
        _device.SetAccount("a");

        await Navigation().ActivatePushAsync();

        Assert.Empty(_posts);
        Assert.False(Preferences.Default.ContainsKey("push_choice_a"));
        Assert.True(_device.PushChoicePending);
    }

    [Theory]
    [InlineData("ok", true)]
    [InlineData("4xx", false)]
    [InlineData("5xx", false)]
    [InlineData("timeout", false)]
    [InlineData("network", false)]
    [InlineData("signout", false)]
    public async Task Tak_PostOutcome_OnlySuccessRecordsConsent(string outcome, bool on)
    {
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        _post = async ct =>
        {
            started.TrySetResult();
            await release.Task;
            switch (outcome)
            {
                case "4xx": throw new ApiException(HttpStatusCode.BadRequest, "bad");
                case "5xx": throw new ApiException(HttpStatusCode.ServiceUnavailable, "down");
                case "timeout": throw new TaskCanceledException("HttpClient.Timeout");
                case "network": throw new HttpRequestException("reset");
                case "signout": ct.ThrowIfCancellationRequested(); break;
            }
        };

        _device.SetAccount("a");
        var navigation = Navigation().ActivatePushAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task? signOut = outcome == "signout" ? Navigation().SignOutAsync() : null;
        release.SetResult();
        await navigation;
        if (signOut is not null)
            await signOut.WaitAsync(TimeSpan.FromSeconds(30));

        _post = null;
        Assert.Equal(on, Preferences.Default.Get("push_choice_a", false));
        Assert.Equal(!on && outcome != "signout", _device.PushChoicePending);
    }
}

internal static class PushConsentTestExtensions
{
    public static async Task RestoreAfterUpgrade(this PostLoginNavigationService navigation, IAuthService auth)
    {
        auth.RestoreSessionAsync().Returns(SessionRestoreResult.Restored);
        await navigation.RestoreSessionAsync();
    }
}
