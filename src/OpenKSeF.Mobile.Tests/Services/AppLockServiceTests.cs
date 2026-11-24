using NSubstitute;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

[Collection("Shell")]
public class AppLockServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

    private readonly IAuthService _auth = Substitute.For<IAuthService>();
    private readonly AppLockService _lock;
    private DateTimeOffset _now = T0;
    private bool _deviceSecure = true;
    private bool _promptResult = true;
    private int _prompts;

    public AppLockServiceTests()
    {
        Preferences.Default.Remove("app_lock_enabled");
        Preferences.Default.Remove("app_lock_timeout");
        _auth.HasSession.Returns(true);
        _auth.HasStoredSessionAsync().Returns(true);
        _lock = new AppLockService(_auth)
        {
            Now = () => _now,
            DeviceSecure = () => _deviceSecure,
            Prompt = () => { _prompts++; return Task.FromResult(_promptResult); },
        };
    }

    private async Task EnableAsync(int timeoutSeconds = AppLockService.DefaultTimeoutSeconds)
    {
        Assert.True(await _lock.SetEnabledAsync(true));
        _lock.TimeoutSeconds = timeoutSeconds;
        _prompts = 0;
    }

    private AccountViewModel NewAccountViewModel() => new(
        Substitute.For<IServerSettingsService>(),
        Substitute.For<IDeviceTokenService>(),
        Substitute.For<INotificationHubService>(),
        Substitute.For<IPostLoginNavigationService>(),
        Substitute.For<IApiService>(),
        _lock);

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, true)]
    [InlineData(300, 240, false)]
    [InlineData(300, 299, false)]
    [InlineData(300, 300, true)]
    [InlineData(300, 3600, true)]
    [InlineData(60, 59, false)]
    [InlineData(60, 60, true)]
    [InlineData(300, -10, true)]
    public void LockDue_ComparesTimeInBackgroundWithTimeout(int timeoutSeconds, int secondsAway, bool due)
    {
        Assert.Equal(due, AppLockService.LockDue(T0, T0.AddSeconds(secondsAway), timeoutSeconds));
    }

    [Fact]
    public async Task FreshInstall_IsOffWithFiveMinutes_AndColdStartHasNoPrompt()
    {
        Assert.False(_lock.Enabled);
        Assert.Equal(300, _lock.TimeoutSeconds);

        Assert.False(await _lock.StartAsync());
        Assert.False(_lock.IsLocked);
        Assert.Equal(0, _prompts);
    }

    [Fact]
    public async Task Enabling_SavesOnlyAfterSuccessfulPrompt()
    {
        _promptResult = false;
        Assert.False(await _lock.SetEnabledAsync(true));
        Assert.False(_lock.Enabled);

        _promptResult = true;
        Assert.True(await _lock.SetEnabledAsync(true));
        Assert.True(_lock.Enabled);
        Assert.Equal(2, _prompts);
    }

    [Fact]
    public async Task Disabling_NeedsPrompt()
    {
        await EnableAsync();
        _promptResult = false;

        Assert.False(await _lock.SetEnabledAsync(false));
        Assert.True(_lock.Enabled);
        Assert.Equal(1, _prompts);
    }

    [Fact]
    public async Task ColdStart_WithStoredSession_LocksBeforeAnyData()
    {
        await EnableAsync();
        var lockedDuringCheck = false;
        _auth.HasStoredSessionAsync().Returns(_ => { lockedDuringCheck = _lock.IsLocked; return true; });

        Assert.True(await _lock.StartAsync());
        Assert.True(lockedDuringCheck);
        Assert.True(_lock.IsLocked);
    }

    [Fact]
    public async Task ColdStart_SignedOut_NoCoverNoPrompt()
    {
        await EnableAsync();
        _auth.HasStoredSessionAsync().Returns(false);

        Assert.False(await _lock.StartAsync());
        Assert.False(_lock.IsLocked);
    }

    [Fact]
    public async Task ColdStart_SessionCheckFails_StaysLocked()
    {
        await EnableAsync();
        _auth.HasStoredSessionAsync().Returns(Task.FromException<bool>(new InvalidOperationException("keystore")));

        Assert.True(await _lock.StartAsync());
        Assert.True(_lock.IsLocked);
    }

    [Theory]
    [InlineData(300, 240, false)]
    [InlineData(300, 300, true)]
    [InlineData(300, 301, true)]
    [InlineData(0, 1, true)]
    [InlineData(60, 30, false)]
    public async Task Resume_LocksOnlyPastTimeout_AndCoversWhileAway(int timeoutSeconds, int secondsAway, bool locked)
    {
        await EnableAsync(timeoutSeconds);

        _lock.OnBackground();
        Assert.True(_lock.IsLocked);
        _now = T0.AddSeconds(secondsAway);

        Assert.Equal(locked, _lock.OnForeground());
        Assert.Equal(locked, _lock.IsLocked);
    }

    [Fact]
    public async Task Resume_WhenAlreadyLocked_ShortTripDoesNotUnlock()
    {
        await EnableAsync();
        _lock.OnBackground();
        _now = T0.AddMinutes(10);
        Assert.True(_lock.OnForeground());
        _promptResult = false;
        Assert.False(await _lock.UnlockAsync());

        _lock.OnBackground();
        _now = _now.AddSeconds(5);

        Assert.True(_lock.OnForeground());
        Assert.True(_lock.IsLocked);
    }

    [Fact]
    public async Task Background_SignedOut_NoCover()
    {
        await EnableAsync(0);
        _auth.HasSession.Returns(false);

        _lock.OnBackground();
        _now = T0.AddMinutes(10);

        Assert.False(_lock.OnForeground());
        Assert.False(_lock.IsLocked);
    }

    [Fact]
    public async Task CancelledPrompt_KeepsCover_AndRetryUnlocks()
    {
        await EnableAsync(0);
        _lock.OnBackground();
        _now = T0.AddSeconds(1);
        Assert.True(_lock.OnForeground());

        _promptResult = false;
        Assert.False(await _lock.UnlockAsync());
        Assert.True(_lock.IsLocked);

        _promptResult = true;
        Assert.True(await _lock.UnlockAsync());
        Assert.False(_lock.IsLocked);
        Assert.Equal(2, _prompts);
    }

    [Fact]
    public async Task OwnPromptLeavingTheApp_DoesNotStartAnotherLock()
    {
        await EnableAsync(0);
        _lock.OnBackground();
        _now = T0.AddSeconds(1);
        _lock.OnForeground();
        var prompt = new TaskCompletionSource<bool>();
        _lock.Prompt = () => prompt.Task;

        var unlock = _lock.UnlockAsync();
        _lock.OnBackground();
        _now = _now.AddSeconds(30);
        Assert.False(_lock.OnForeground());
        Assert.False(await _lock.UnlockAsync());
        prompt.SetResult(true);

        Assert.True(await unlock);
        Assert.False(_lock.IsLocked);
    }

    [Fact]
    public async Task DeviceLostScreenLock_TurnsOffWithNotice_NoLockOut()
    {
        await EnableAsync(0);
        var notices = 0;
        _lock.TurnedOffNoScreenLock += () => notices++;
        _lock.OnBackground();
        _deviceSecure = false;
        _now = T0.AddMinutes(1);

        Assert.False(_lock.OnForeground());
        Assert.False(_lock.IsLocked);
        Assert.False(_lock.Enabled);
        Assert.Equal(1, notices);
    }

    [Fact]
    public async Task DeviceLostScreenLock_ColdStart_TurnsOffWithNotice()
    {
        await EnableAsync();
        var notices = 0;
        _lock.TurnedOffNoScreenLock += () => notices++;
        _deviceSecure = false;

        Assert.False(await _lock.StartAsync());
        Assert.False(_lock.IsLocked);
        Assert.False(_lock.Enabled);
        Assert.Equal(1, notices);
    }

    [Fact]
    public async Task Release_DropsCoverWithoutPrompt()
    {
        await EnableAsync();
        await _lock.StartAsync();

        _lock.Release();

        Assert.False(_lock.IsLocked);
        Assert.Equal(0, _prompts);
    }

    [Fact]
    public async Task Account_DeviceWithoutScreenLock_StaysOffWithHint()
    {
        _deviceSecure = false;
        var vm = NewAccountViewModel();

        vm.AppLockEnabled = true;
        await vm.ToggleAppLockCommand.ExecuteAsync(true);

        Assert.False(vm.AppLockEnabled);
        Assert.False(_lock.Enabled);
        Assert.Equal(AccountViewModel.NoScreenLockText, vm.AppLockHint);
        Assert.Equal(0, _prompts);
    }

    [Fact]
    public async Task Account_CancelledPrompt_RevertsToggle()
    {
        _promptResult = false;
        var vm = NewAccountViewModel();

        vm.AppLockEnabled = true;
        await vm.ToggleAppLockCommand.ExecuteAsync(true);

        Assert.False(vm.AppLockEnabled);
        Assert.False(_lock.Enabled);
        Assert.Null(vm.AppLockHint);
    }

    [Fact]
    public async Task Account_ScreenLockRemoved_SwitchTurnsOff()
    {
        await EnableAsync(0);
        var vm = NewAccountViewModel();
        Assert.True(vm.AppLockEnabled);
        _lock.OnBackground();
        _deviceSecure = false;

        _lock.OnForeground();

        Assert.False(vm.AppLockEnabled);
    }

    [Fact]
    public async Task Account_TimeoutChoice_IsStored()
    {
        var vm = NewAccountViewModel();
        Assert.Equal("300", vm.AppLockTimeout);
        await vm.ToggleAppLockCommand.ExecuteAsync(true);
        Assert.True(vm.AppLockEnabled);

        vm.AppLockTimeout = "0";

        Assert.Equal(0, _lock.TimeoutSeconds);
        Assert.Equal("0", NewAccountViewModel().AppLockTimeout);
    }
}
