using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class LoginViewModelTests
{
    private readonly IAuthService _auth = Substitute.For<IAuthService>();
    private readonly IPostLoginNavigationService _postLoginNav = Substitute.For<IPostLoginNavigationService>();

    public LoginViewModelTests()
    {
        Shell.Current.Reset();
        Preferences.Default.Clear();
    }

    private static IServerSettingsService CreateServerSettings(string url, bool isConfigured)
    {
        var settings = Substitute.For<IServerSettingsService>();
        settings.ServerUrl.Returns(url);
        settings.IsConfigured.Returns(isConfigured);
        settings.TryUpdateServerUrl(Arg.Any<string>(), out Arg.Any<string>(), out Arg.Any<string?>())
            .Returns(x =>
            {
                x[1] = url;
                x[2] = (string?)null;
                return true;
            });
        return settings;
    }

    private LoginViewModel CreateViewModel(IServerSettingsService? serverSettings = null) =>
        new(_auth, serverSettings ?? CreateServerSettings("https://default.com", isConfigured: true), _postLoginNav);

    [Fact]
    public void Constructor_InitializesFromServerSettings()
    {
        var vm = CreateViewModel(CreateServerSettings("https://initial.com", isConfigured: true));

        Assert.Equal("https://initial.com", vm.ServerUrl);
        Assert.Equal("initial.com", vm.ActiveServerHost);
        Assert.False(vm.IsServerVisible);
    }

    [Fact]
    public void Constructor_ShowsServerSection_WhenNotConfigured()
    {
        Assert.True(CreateViewModel(CreateServerSettings("", isConfigured: false)).IsServerVisible);
    }

    [Fact]
    public void RefreshServerSettings_PicksUpChangedServer()
    {
        var serverSettings = CreateServerSettings("https://old.com", isConfigured: true);
        var vm = CreateViewModel(serverSettings);

        serverSettings.ServerUrl.Returns("https://new.com");
        serverSettings.IsConfigured.Returns(false);
        vm.RefreshServerSettings();

        Assert.Equal("https://new.com", vm.ServerUrl);
        Assert.True(vm.IsServerVisible);
    }

    [Fact]
    public async Task LoginWithCredentials_Success_NavigatesPostLogin()
    {
        _auth.LoginWithCredentialsAsync("user@example.com", "secret").Returns(true);
        var vm = CreateViewModel();
        vm.Email = " user@example.com ";
        vm.Password = "secret";

        await vm.LoginWithCredentialsCommand.ExecuteAsync(null);

        await _postLoginNav.Received(1).NavigateAsync();
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task LoginWithCredentials_WrongPassword_ShowsErrorWithoutNavigation()
    {
        var vm = CreateViewModel();
        vm.Email = "user@example.com";
        vm.Password = "wrong";

        await vm.LoginWithCredentialsCommand.ExecuteAsync(null);

        Assert.Equal("Nieprawidłowy email lub hasło.", vm.ErrorMessage);
        await _postLoginNav.DidNotReceive().NavigateAsync();
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task LoginWithCredentials_EmptyFields_DoesNotCallAuth()
    {
        var vm = CreateViewModel();

        await vm.LoginWithCredentialsCommand.ExecuteAsync(null);

        Assert.Equal("Podaj email i hasło.", vm.ErrorMessage);
        await _auth.DidNotReceiveWithAnyArgs().LoginWithCredentialsAsync(default!, default!);
    }

    [Theory]
    [InlineData("short", "short", "Hasło musi mieć co najmniej 8 znaków.")]
    [InlineData("password1", "password2", "Hasła nie są identyczne.")]
    public async Task Register_InvalidPassword_DoesNotCallAuth(string password, string confirm, string expectedError)
    {
        var vm = CreateViewModel();
        vm.RegEmail = "new@example.com";
        vm.RegPassword = password;
        vm.RegConfirmPassword = confirm;

        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(expectedError, vm.ErrorMessage);
        await _auth.DidNotReceiveWithAnyArgs().RegisterAsync(default!, default!, default, default);
    }
}

[Collection("Shell")]
public class PostLoginNavigationServiceTests
{
    private readonly IApiService _api = Substitute.For<IApiService>();
    private readonly IAuthService _auth = Substitute.For<IAuthService>();
    private readonly IDeviceTokenService _deviceTokenService = Substitute.For<IDeviceTokenService>();
    private readonly INotificationHubService _notificationHub = Substitute.For<INotificationHubService>();
    private readonly LocalDbService _localDb = new();
    private readonly PostLoginNavigationService _nav;

    public PostLoginNavigationServiceTests()
    {
        Shell.Current.Reset();
        Preferences.Default.Clear();
        var serverSettings = Substitute.For<IServerSettingsService>();
        serverSettings.ServerUrl.Returns("https://ksef.example.com");
        _api.GetMeAsync().Returns(new UserInfo { UserId = "u1" });
        _nav = new PostLoginNavigationService(_api, _auth, _deviceTokenService, _notificationHub, serverSettings, _localDb);
    }

    private void OnboardingComplete(bool complete) =>
        _api.GetOnboardingStatusAsync().Returns(new OnboardingStatusDto { IsComplete = complete });

    [Fact]
    public async Task Navigate_OnboardingComplete_RegistersDeviceStartsHubAndOpensInvoices()
    {
        OnboardingComplete(true);

        await _nav.NavigateAsync();

        await _deviceTokenService.Received(1).EnsureDeviceRegisteredAsync();
        await _notificationHub.Received(1).StartAsync();
        Assert.Equal("//main/invoices", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Navigate_OnboardingNeeded_SkipsDeviceRegistration()
    {
        OnboardingComplete(false);

        await _nav.NavigateAsync();

        await _deviceTokenService.DidNotReceive().EnsureDeviceRegisteredAsync();
        await _notificationHub.DidNotReceive().StartAsync();
        Assert.Equal("//onboarding", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Navigate_DeviceRegistrationFails_StillNavigates()
    {
        OnboardingComplete(true);
        _deviceTokenService.EnsureDeviceRegisteredAsync().ThrowsAsync(new Exception("Network error"));

        await _nav.NavigateAsync();

        await _notificationHub.Received(1).StartAsync();
        Assert.Equal("//main/invoices", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Navigate_DifferentUser_ClearsCachedInvoicesOfPreviousUser()
    {
        OnboardingComplete(true);
        var tenantId = Guid.NewGuid();
        await _localDb.EnsureCurrentUserAsync("https://ksef.example.com|previous-user");
        await _localDb.SaveInvoicesAsync(tenantId, [new InvoiceDto { Id = Guid.NewGuid(), IssueDate = DateTime.UtcNow }]);
        Assert.Single(await _localDb.GetInvoicesAsync(tenantId));

        await _nav.NavigateAsync();

        Assert.Empty(await _localDb.GetInvoicesAsync(tenantId));
    }

    [Fact]
    public async Task Navigate_SameUser_KeepsOfflineCache()
    {
        OnboardingComplete(true);
        var tenantId = Guid.NewGuid();
        await _localDb.EnsureCurrentUserAsync("https://ksef.example.com|u1");
        await _localDb.SaveInvoicesAsync(tenantId, [new InvoiceDto { Id = Guid.NewGuid(), IssueDate = DateTime.UtcNow }]);

        await _nav.NavigateAsync();

        Assert.Single(await _localDb.GetInvoicesAsync(tenantId));
    }

    [Fact]
    public async Task SignOut_UnregistersDeviceStopsHubAndLogsOut()
    {
        await _nav.SignOutAsync();

        Received.InOrder(() =>
        {
            _notificationHub.StopAsync();
            _deviceTokenService.UnregisterCurrentDeviceAsync();
            _auth.LogoutAsync();
        });
    }
}
