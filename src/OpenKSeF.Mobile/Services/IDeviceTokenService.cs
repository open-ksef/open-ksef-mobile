namespace OpenKSeF.Mobile.Services;

public interface IDeviceTokenService
{
    Task RegisterTokenAsync(string token, string platform);
    Task RegisterTokenForTenantsAsync(List<Guid>? tenantIds);
    Task RequestNotificationPermissionAsync();
    Task<bool> IsDeviceRegisteredAsync();
    Task EnsureDeviceRegisteredAsync();
    Task<bool> EnableNotificationsAsync();
    Task<bool> AreNotificationsEnabledAsync();
}
