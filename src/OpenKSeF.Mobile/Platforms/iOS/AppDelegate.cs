using Foundation;
using OpenKSeF.Mobile.Services;
using UIKit;
using UserNotifications;

namespace OpenKSeF.Mobile;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate, IUNUserNotificationCenterDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        UNUserNotificationCenter.Current.Delegate = this;
        return base.FinishedLaunching(application, launchOptions);
    }

    [Export("application:didRegisterForRemoteNotificationsWithDeviceToken:")]
    public void RegisteredForRemoteNotifications(UIApplication application, NSData deviceToken)
    {
        var token = deviceToken.ToArray();
        var tokenString = BitConverter.ToString(token).Replace("-", string.Empty).ToLowerInvariant();

        _ = RegisterTokenAsync(tokenString);
    }

    [Export("application:didFailToRegisterForRemoteNotificationsWithError:")]
    public void FailedToRegisterForRemoteNotifications(UIApplication application, NSError error)
    {
        System.Diagnostics.Debug.WriteLine($"APNs registration failed: {error.LocalizedDescription}");
    }

    // Handle notification when app is in foreground
    [Export("userNotificationCenter:willPresentNotification:withCompletionHandler:")]
    public void WillPresentNotification(
        UNUserNotificationCenter center,
        UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler)
    {
        completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound);
    }

    private static async Task RegisterTokenAsync(string token)
    {
        try
        {
            var service = IPlatformApplication.Current?.Services.GetService<IDeviceTokenService>();
            if (service is not null)
                await service.RegisterTokenAsync(token, "iOS");
        }
        catch
        {
            // Best-effort registration
        }
    }
}
