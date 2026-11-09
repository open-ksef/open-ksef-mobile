namespace OpenKSeF.Mobile.Services;

public interface IDeviceTokenService
{
    Task RegisterTokenAsync(string token, string platform);
    Task<bool> IsDeviceRegisteredAsync();
    Task EnsureDeviceRegisteredAsync();
    Task<bool> EnableNotificationsAsync();
    Task<bool> AreNotificationsEnabledAsync();
    /// <summary>True when the server confirmed removal and no POST of this session can still land; otherwise the cleanup keeps the tokens under <paramref name="signOutId"/>.</summary>
    Task<bool> UnregisterCurrentDeviceAsync(string signOutId, CancellationToken cancellationToken);
    bool PushDisabledByUser { get; }
    bool PushRemovalPending { get; }
    bool ShowHubNotification(bool? nativePush);
    Task CompleteSignOutCleanupAsync();
    Task<bool> DisableNotificationsAsync();
}
