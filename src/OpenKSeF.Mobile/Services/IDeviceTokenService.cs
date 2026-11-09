namespace OpenKSeF.Mobile.Services;

public interface IDeviceTokenService
{
    Task RegisterTokenAsync(string token, string platform);
    Task<bool> IsDeviceRegisteredAsync();
    Task EnsureDeviceRegisteredAsync();
    Task<bool> EnableNotificationsAsync();
    Task<bool> AreNotificationsEnabledAsync();
    Task UnregisterCurrentDeviceAsync(CancellationToken cancellationToken);
    bool PushDisabledByUser { get; }
    bool PushRemovalPending { get; }
    bool SignOutCleanupPending { get; }
    bool ShowHubNotification(bool? nativePush);
    Task CompleteSignOutCleanupAsync();
    Task<bool> DisableNotificationsAsync();
}
