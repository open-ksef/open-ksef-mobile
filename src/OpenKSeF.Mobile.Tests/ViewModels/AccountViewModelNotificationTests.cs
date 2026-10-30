using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class AccountViewModelNotificationTests
{
    private readonly IDeviceTokenService _deviceTokenService = Substitute.For<IDeviceTokenService>();
    private readonly INotificationHubService _notificationHub = Substitute.For<INotificationHubService>();
    private readonly IPostLoginNavigationService _session = Substitute.For<IPostLoginNavigationService>();
    private readonly AccountViewModel _vm;

    public AccountViewModelNotificationTests()
    {
        Shell.Current.Reset();
        Application.Current!.Windows[0].Page = null;
        var serverSettings = Substitute.For<IServerSettingsService>();
        serverSettings.ServerUrl.Returns("https://ksef.example.com");
        _vm = new AccountViewModel(serverSettings, _deviceTokenService, _notificationHub, _session);
    }

    [Fact]
    public void Constructor_ShowsServerHost()
    {
        Assert.Equal("ksef.example.com", _vm.ServerHost);
    }

    [Fact]
    public async Task LoadSettings_SetsNotificationsEnabled_WhenPermissionGranted()
    {
        _deviceTokenService.AreNotificationsEnabledAsync().Returns(true);

        await _vm.LoadSettingsAsync();

        Assert.True(_vm.NotificationsEnabled);
        Assert.Equal("Powiadomienia push są włączone.", _vm.NotificationStatusText);
    }

    [Fact]
    public async Task LoadSettings_SetsNotificationsDisabled_WhenPermissionNotGranted()
    {
        _deviceTokenService.AreNotificationsEnabledAsync().Returns(false);

        await _vm.LoadSettingsAsync();

        Assert.False(_vm.NotificationsEnabled);
        Assert.Equal("Powiadomienia push są wyłączone.", _vm.NotificationStatusText);
    }

    [Fact]
    public async Task LoadSettings_IncludesSignalRStatus_WhenConnected()
    {
        _deviceTokenService.AreNotificationsEnabledAsync().Returns(true);
        _notificationHub.IsConnected.Returns(true);

        await _vm.LoadSettingsAsync();

        Assert.Equal("Powiadomienia push są włączone. (SignalR połączony)", _vm.NotificationStatusText);
    }

    [Fact]
    public async Task ToggleNotifications_EnablesSuccessfully_WhenPermissionGranted()
    {
        _deviceTokenService.AreNotificationsEnabledAsync().Returns(false);
        _deviceTokenService.EnableNotificationsAsync().Returns(true);

        await _vm.ToggleNotificationsAsync();

        Assert.True(_vm.NotificationsEnabled);
        Assert.Equal("Powiadomienia push są włączone.", _vm.NotificationStatusText);
    }

    [Fact]
    public async Task ToggleNotifications_StaysDisabled_WhenPermissionDenied()
    {
        _deviceTokenService.AreNotificationsEnabledAsync().Returns(false);
        _deviceTokenService.EnableNotificationsAsync().Returns(false);

        await _vm.ToggleNotificationsAsync();

        Assert.False(_vm.NotificationsEnabled);
        Assert.Equal("Brak uprawnień. Włącz powiadomienia w ustawieniach systemu.", _vm.NotificationStatusText);
    }

    [Theory]
    [InlineData(true, "Powiadomienia push są włączone.")]
    [InlineData(false, "Nie udało się odnowić rejestracji powiadomień.")]
    public async Task ToggleNotifications_AlreadyEnabled_RefreshesRegistration(bool refreshed, string expectedStatus)
    {
        _deviceTokenService.AreNotificationsEnabledAsync().Returns(true);
        _deviceTokenService.EnableNotificationsAsync().Returns(refreshed);

        await _vm.ToggleNotificationsAsync();

        await _deviceTokenService.Received(1).EnableNotificationsAsync();
        Assert.Equal(refreshed, _vm.NotificationsEnabled);
        Assert.Equal(expectedStatus, _vm.NotificationStatusText);
    }

    [Fact]
    public async Task ToggleNotifications_HandlesException_Gracefully()
    {
        _deviceTokenService.AreNotificationsEnabledAsync().Returns(false);
        _deviceTokenService.EnableNotificationsAsync().ThrowsAsync(new Exception("Network error"));

        await _vm.ToggleNotificationsAsync();

        Assert.False(_vm.NotificationsEnabled);
        Assert.Equal("Wystąpił błąd podczas włączania powiadomień.", _vm.NotificationStatusText);
        Assert.False(_vm.IsBusy);
    }

    [Fact]
    public async Task Logout_Confirmed_SignsOutAndResetsShell()
    {
        await _vm.LogoutCommand.ExecuteAsync(null);

        await _session.Received(1).SignOutAsync();
        Assert.IsType<OpenKSeF.Mobile.AppShell>(Application.Current!.Windows[0].Page);
    }

    [Fact]
    public async Task Logout_Cancelled_KeepsSession()
    {
        Shell.Current.AlertAnswer = false;

        await _vm.LogoutCommand.ExecuteAsync(null);

        await _session.DidNotReceive().SignOutAsync();
    }
}
