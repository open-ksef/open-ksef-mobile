using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Mobile.Models;
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
        _deviceTokenService.EnableNotificationsAsync().Returns(true);

        await _vm.ToggleNotificationsAsync(true);

        Assert.True(_vm.NotificationsEnabled);
        Assert.Equal("Powiadomienia push są włączone.", _vm.NotificationStatusText);
    }

    [Fact]
    public async Task ToggleNotifications_StaysDisabled_WhenPermissionDenied()
    {
        _deviceTokenService.EnableNotificationsAsync().Returns(false);

        await _vm.ToggleNotificationsAsync(true);

        Assert.False(_vm.NotificationsEnabled);
        Assert.Equal("Brak uprawnień. Włącz powiadomienia w ustawieniach systemu.", _vm.NotificationStatusText);
    }

    private const string PendingStatus =
        "Powiadomienia wyłączone, ale serwer jeszcze o tym nie wie. Powiadomienie może dotrzeć, dopóki nie uruchomisz aplikacji ponownie z dostępem do sieci.";

    [Theory]
    [InlineData(true, "Powiadomienia push są wyłączone.")]
    [InlineData(false, PendingStatus)]
    public async Task ToggleNotifications_TappedOff_TurnsOffAndStopsHub(bool unregistered, string expectedStatus)
    {
        _deviceTokenService.DisableNotificationsAsync().Returns(unregistered);

        await _vm.ToggleNotificationsAsync(false);

        Received.InOrder(() =>
        {
            _deviceTokenService.DisableNotificationsAsync();
            _notificationHub.StopAsync();
        });
        await _deviceTokenService.DidNotReceive().EnableNotificationsAsync();
        Assert.False(_vm.NotificationsEnabled);
        Assert.Equal(expectedStatus, _vm.NotificationStatusText);
        Assert.False(_vm.IsBusy);
    }

    [Fact]
    public async Task ToggleNotifications_TurningOff_HubStopFailure_StillTurnsOff()
    {
        _deviceTokenService.DisableNotificationsAsync().Returns(true);
        _notificationHub.StopAsync().ThrowsAsync(new Exception("hub"));

        await _vm.ToggleNotificationsAsync(false);

        Assert.False(_vm.NotificationsEnabled);
        Assert.Equal("Powiadomienia push są wyłączone.", _vm.NotificationStatusText);
    }

    [Fact]
    public async Task LoadSettings_ShowsPendingText_WhileServerStillHasDevice()
    {
        _deviceTokenService.AreNotificationsEnabledAsync().Returns(false);
        _deviceTokenService.PushRemovalPending.Returns(true);

        await _vm.LoadSettingsAsync();

        Assert.False(_vm.NotificationsEnabled);
        Assert.Equal(PendingStatus, _vm.NotificationStatusText);
    }

    [Fact]
    public async Task ToggleNotifications_Enabled_StartsHub()
    {
        _deviceTokenService.EnableNotificationsAsync().Returns(true);

        await _vm.ToggleNotificationsAsync(true);

        await _notificationHub.Received(1).StartAsync();
    }

    [Fact]
    public async Task ToggleNotifications_Denied_DoesNotStartHub()
    {
        _deviceTokenService.EnableNotificationsAsync().Returns(false);

        await _vm.ToggleNotificationsAsync(true);

        await _notificationHub.DidNotReceive().StartAsync();
    }

    [Fact]
    public async Task ToggleNotifications_HandlesException_Gracefully()
    {
        _deviceTokenService.EnableNotificationsAsync().ThrowsAsync(new Exception("boom"));

        await _vm.ToggleNotificationsAsync(true);

        Assert.False(_vm.NotificationsEnabled);
        Assert.Equal("Wystąpił błąd podczas włączania powiadomień.", _vm.NotificationStatusText);
        Assert.False(_vm.IsBusy);
    }

    [Fact]
    public async Task ToggleNotifications_TappedOn_WhileServiceReportsEnabled_TurnsOn()
    {
        _deviceTokenService.AreNotificationsEnabledAsync().Returns(true);
        _deviceTokenService.EnableNotificationsAsync().Returns(true);

        await _vm.ToggleNotificationsAsync(true);

        await _deviceTokenService.DidNotReceive().DisableNotificationsAsync();
        await _deviceTokenService.Received(1).EnableNotificationsAsync();
        Assert.True(_vm.NotificationsEnabled);
        Assert.Equal("Powiadomienia push są włączone.", _vm.NotificationStatusText);
    }

    [Fact]
    public async Task ToggleNotifications_TappedOff_WhileServiceReportsDisabled_TurnsOff()
    {
        _deviceTokenService.AreNotificationsEnabledAsync().Returns(false);
        _deviceTokenService.DisableNotificationsAsync().Returns(true);

        await _vm.ToggleNotificationsAsync(false);

        await _deviceTokenService.Received(1).DisableNotificationsAsync();
        await _deviceTokenService.DidNotReceive().EnableNotificationsAsync();
        Assert.False(_vm.NotificationsEnabled);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ToggleNotifications_SwitchEchoOfResult_DoesNotReenter(bool turnOn, bool outcome)
    {
        _deviceTokenService.EnableNotificationsAsync().Returns(outcome);
        _deviceTokenService.DisableNotificationsAsync().Returns(outcome);
        _vm.NotificationsEnabled = !turnOn || !outcome;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountViewModel.NotificationsEnabled))
                _vm.ToggleNotificationsCommand.Execute(_vm.NotificationsEnabled);
        };

        await _vm.ToggleNotificationsAsync(turnOn);

        await _deviceTokenService.Received(turnOn ? 1 : 0).EnableNotificationsAsync();
        await _deviceTokenService.Received(turnOn ? 0 : 1).DisableNotificationsAsync();
    }

    private const string NoConnectionStatus =
        "Nie udało się włączyć powiadomień: brak połączenia z serwerem. Spróbuj ponownie po powrocie sieci.";

    [Theory]
    [InlineData(false, NoConnectionStatus)]
    [InlineData(true, NoConnectionStatus + " " + PendingStatus)]
    public async Task ToggleNotifications_TappedOn_NoNetwork_SaysNetworkFailed(bool pending, string expectedStatus)
    {
        _deviceTokenService.EnableNotificationsAsync().ThrowsAsync(
            new ApiException(System.Net.HttpStatusCode.ServiceUnavailable, "Brak polaczenia z serwerem"));
        _deviceTokenService.PushRemovalPending.Returns(pending);

        await _vm.ToggleNotificationsAsync(true);

        Assert.False(_vm.NotificationsEnabled);
        Assert.Equal(expectedStatus, _vm.NotificationStatusText);
        Assert.False(_vm.IsBusy);
        await _notificationHub.DidNotReceive().StartAsync();
    }

    [Fact]
    public async Task ToggleNotifications_Denied_KeepsPendingWarning()
    {
        _deviceTokenService.EnableNotificationsAsync().Returns(false);
        _deviceTokenService.PushRemovalPending.Returns(true);

        await _vm.ToggleNotificationsAsync(true);

        Assert.Equal("Brak uprawnień. Włącz powiadomienia w ustawieniach systemu. " + PendingStatus, _vm.NotificationStatusText);
    }

    [Fact]
    public async Task ToggleNotifications_ServerRejects_DoesNotBlamePermissionOrNetwork()
    {
        _deviceTokenService.EnableNotificationsAsync().ThrowsAsync(
            new ApiException(System.Net.HttpStatusCode.Unauthorized, "Sesja wygasła."));
        _deviceTokenService.PushRemovalPending.Returns(true);

        await _vm.ToggleNotificationsAsync(true);

        Assert.Equal("Wystąpił błąd podczas włączania powiadomień. " + PendingStatus, _vm.NotificationStatusText);
    }

    [Fact]
    public async Task ThirdPartyNotices_OpensTheShippedFile()
    {
        await _vm.OpenThirdPartyNoticesCommand.ExecuteAsync(null);

        Assert.EndsWith("/Resources/Raw/pdf/THIRD-PARTY-NOTICES.txt", Browser.Default.LastUri);
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
