using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

public class AccountViewModelNotificationTests
{
    private readonly ITestDeviceTokenService _deviceTokenService;
    private readonly TestAccountViewModel _vm;

    public AccountViewModelNotificationTests()
    {
        _deviceTokenService = Substitute.For<ITestDeviceTokenService>();
        _vm = new TestAccountViewModel(_deviceTokenService);
    }

    [Fact]
    public async Task LoadSettings_SetsNotificationsEnabled_WhenPermissionGranted()
    {
        _deviceTokenService.AreNotificationsEnabledAsync().Returns(true);

        await _vm.LoadSettingsAsync();

        Assert.True(_vm.NotificationsEnabled);
        Assert.Contains("włączone", _vm.NotificationStatusText);
    }

    [Fact]
    public async Task LoadSettings_SetsNotificationsDisabled_WhenPermissionNotGranted()
    {
        _deviceTokenService.AreNotificationsEnabledAsync().Returns(false);

        await _vm.LoadSettingsAsync();

        Assert.False(_vm.NotificationsEnabled);
        Assert.Contains("wyłączone", _vm.NotificationStatusText);
    }

    [Fact]
    public async Task ToggleNotifications_EnablesSuccessfully_WhenPermissionGranted()
    {
        _deviceTokenService.AreNotificationsEnabledAsync().Returns(false);
        _deviceTokenService.EnableNotificationsAsync().Returns(true);

        await _vm.LoadSettingsAsync();
        await _vm.ToggleNotificationsAsync();

        Assert.True(_vm.NotificationsEnabled);
        Assert.Contains("włączone", _vm.NotificationStatusText);
        await _deviceTokenService.Received(1).EnableNotificationsAsync();
    }

    [Fact]
    public async Task ToggleNotifications_StaysDisabled_WhenPermissionDenied()
    {
        _deviceTokenService.AreNotificationsEnabledAsync().Returns(false);
        _deviceTokenService.EnableNotificationsAsync().Returns(false);

        await _vm.LoadSettingsAsync();
        await _vm.ToggleNotificationsAsync();

        Assert.False(_vm.NotificationsEnabled);
        Assert.Contains("ustawienia", _vm.NotificationStatusText.ToLower());
    }

    [Fact]
    public async Task ToggleNotifications_AlreadyEnabled_ShowsSystemSettingsMessage()
    {
        _deviceTokenService.AreNotificationsEnabledAsync().Returns(true);

        await _vm.LoadSettingsAsync();
        await _vm.ToggleNotificationsAsync();

        await _deviceTokenService.DidNotReceive().EnableNotificationsAsync();
        Assert.Contains("system", _vm.NotificationStatusText.ToLower());
    }

    [Fact]
    public async Task ToggleNotifications_HandlesException_Gracefully()
    {
        _deviceTokenService.AreNotificationsEnabledAsync().Returns(false);
        _deviceTokenService.EnableNotificationsAsync()
            .Returns<bool>(_ => throw new Exception("Network error"));

        await _vm.LoadSettingsAsync();
        await _vm.ToggleNotificationsAsync();

        Assert.Contains("błąd", _vm.NotificationStatusText.ToLower());
    }
}

public partial class TestAccountViewModel : ObservableObject
{
    private readonly ITestDeviceTokenService _deviceTokenService;

    [ObservableProperty]
    private bool _notificationsEnabled;

    [ObservableProperty]
    private string _notificationStatusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public TestAccountViewModel(ITestDeviceTokenService deviceTokenService)
    {
        _deviceTokenService = deviceTokenService;
    }

    public async Task LoadSettingsAsync()
    {
        try
        {
            NotificationsEnabled = await _deviceTokenService.AreNotificationsEnabledAsync();
            NotificationStatusText = NotificationsEnabled
                ? "Powiadomienia push są włączone."
                : "Powiadomienia push są wyłączone.";
        }
        catch
        {
            NotificationStatusText = "Nie udało się sprawdzić statusu powiadomień.";
        }
    }

    public async Task ToggleNotificationsAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;

            var alreadyEnabled = await _deviceTokenService.AreNotificationsEnabledAsync();

            if (alreadyEnabled)
            {
                NotificationsEnabled = true;
                NotificationStatusText = "Aby wyłączyć powiadomienia, zmień ustawienia w systemie.";
                return;
            }

            var granted = await _deviceTokenService.EnableNotificationsAsync();

            NotificationsEnabled = granted;
            NotificationStatusText = granted
                ? "Powiadomienia push są włączone."
                : "Brak uprawnień. Włącz powiadomienia w ustawieniach systemu.";
        }
        catch
        {
            NotificationsEnabled = false;
            NotificationStatusText = "Wystąpił błąd podczas włączania powiadomień.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
