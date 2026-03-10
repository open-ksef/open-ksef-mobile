using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

public class LoginViewModelTests
{
    [Fact]
    public void RefreshServerSettings_UpdatesServerUrlFromService()
    {
        var serverSettings = CreateServerSettings("https://old.com", isConfigured: true);
        var vm = CreateViewModel(serverSettings: serverSettings);

        Assert.Equal("https://old.com", vm.ServerUrl);

        serverSettings.ServerUrl.Returns("https://new.com");
        vm.RefreshServerSettings();

        Assert.Equal("https://new.com", vm.ServerUrl);
    }

    [Fact]
    public void RefreshServerSettings_ShowsServerSection_WhenNotConfigured()
    {
        var serverSettings = CreateServerSettings("", isConfigured: false);
        var vm = CreateViewModel(serverSettings: serverSettings);

        Assert.True(vm.IsServerVisible);
    }

    [Fact]
    public void RefreshServerSettings_HidesServerSection_WhenConfigured()
    {
        var serverSettings = CreateServerSettings("https://configured.com", isConfigured: true);
        var vm = CreateViewModel(serverSettings: serverSettings);

        Assert.False(vm.IsServerVisible);

        serverSettings.IsConfigured.Returns(false);
        vm.RefreshServerSettings();

        Assert.True(vm.IsServerVisible);
    }

    [Fact]
    public void Constructor_InitializesServerUrlFromService()
    {
        var serverSettings = CreateServerSettings("https://initial.com", isConfigured: true);
        var vm = CreateViewModel(serverSettings: serverSettings);

        Assert.Equal("https://initial.com", vm.ServerUrl);
        Assert.False(vm.IsServerVisible);
    }

    [Fact]
    public void Constructor_ShowsServerSection_WhenNotConfigured()
    {
        var serverSettings = CreateServerSettings("", isConfigured: false);
        var vm = CreateViewModel(serverSettings: serverSettings);

        Assert.True(vm.IsServerVisible);
    }

    [Fact]
    public async Task NavigateAfterLogin_CallsEnsureDeviceRegistered_WhenOnboardingComplete()
    {
        var deviceTokenService = Substitute.For<ITestDeviceTokenService>();
        var navVm = new TestNavigationLoginViewModel(
            onboardingComplete: true,
            deviceTokenService: deviceTokenService);

        await navVm.SimulateNavigateAfterLogin();

        await deviceTokenService.Received(1).EnsureDeviceRegisteredAsync();
        Assert.Equal("//main/invoices", navVm.NavigatedRoute);
    }

    [Fact]
    public async Task NavigateAfterLogin_SkipsDeviceRegistration_WhenOnboardingNeeded()
    {
        var deviceTokenService = Substitute.For<ITestDeviceTokenService>();
        var navVm = new TestNavigationLoginViewModel(
            onboardingComplete: false,
            deviceTokenService: deviceTokenService);

        await navVm.SimulateNavigateAfterLogin();

        await deviceTokenService.DidNotReceive().EnsureDeviceRegisteredAsync();
        Assert.Equal("//onboarding", navVm.NavigatedRoute);
    }

    [Fact]
    public async Task NavigateAfterLogin_StillNavigates_WhenDeviceRegistrationFails()
    {
        var deviceTokenService = Substitute.For<ITestDeviceTokenService>();
        deviceTokenService.EnsureDeviceRegisteredAsync()
            .Returns<Task>(_ => throw new Exception("Network error"));

        var navVm = new TestNavigationLoginViewModel(
            onboardingComplete: true,
            deviceTokenService: deviceTokenService);

        await navVm.SimulateNavigateAfterLogin();

        Assert.Equal("//main/invoices", navVm.NavigatedRoute);
    }

    private static ITestServerSettings CreateServerSettings(string url, bool isConfigured)
    {
        var settings = Substitute.For<ITestServerSettings>();
        settings.ServerUrl.Returns(url);
        settings.IsConfigured.Returns(isConfigured);
        return settings;
    }

    private static TestLoginViewModel CreateViewModel(ITestServerSettings? serverSettings = null)
    {
        serverSettings ??= CreateServerSettings("https://default.com", isConfigured: true);
        return new TestLoginViewModel(serverSettings);
    }
}

/// <summary>
/// Mirrors IServerSettingsService for testing without MAUI dependency.
/// </summary>
public interface ITestServerSettings
{
    string ServerUrl { get; }
    bool IsConfigured { get; }
}

/// <summary>
/// Mirrors the relevant parts of LoginViewModel for testing RefreshServerSettings
/// without MAUI Shell dependency.
/// </summary>
public partial class TestLoginViewModel : ObservableObject
{
    private readonly ITestServerSettings _serverSettings;

    [ObservableProperty]
    private string _serverUrl;

    [ObservableProperty]
    private bool _isServerVisible;

    public TestLoginViewModel(ITestServerSettings serverSettings)
    {
        _serverSettings = serverSettings;
        _serverUrl = serverSettings.ServerUrl;
        _isServerVisible = !serverSettings.IsConfigured;
    }

    public void RefreshServerSettings()
    {
        ServerUrl = _serverSettings.ServerUrl;
        IsServerVisible = !_serverSettings.IsConfigured;
    }
}

public interface ITestDeviceTokenService
{
    Task EnsureDeviceRegisteredAsync();
    Task<bool> EnableNotificationsAsync();
    Task<bool> AreNotificationsEnabledAsync();
}

/// <summary>
/// Mirrors the NavigateAfterLogin logic from LoginViewModel without Shell dependency.
/// </summary>
public class TestNavigationLoginViewModel
{
    private readonly bool _onboardingComplete;
    private readonly ITestDeviceTokenService _deviceTokenService;

    public string? NavigatedRoute { get; private set; }

    public TestNavigationLoginViewModel(bool onboardingComplete, ITestDeviceTokenService deviceTokenService)
    {
        _onboardingComplete = onboardingComplete;
        _deviceTokenService = deviceTokenService;
    }

    public async Task SimulateNavigateAfterLogin()
    {
        var needsOnboarding = !_onboardingComplete;

        if (!needsOnboarding)
        {
            try { await _deviceTokenService.EnsureDeviceRegisteredAsync(); } catch { }
        }

        NavigatedRoute = needsOnboarding ? "//onboarding" : "//main/invoices";
    }
}
