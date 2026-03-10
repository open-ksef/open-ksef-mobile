#if FIREBASE_ENABLED
using Android.App;
using Android.Content;
using Firebase.Messaging;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile;

[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public class PushNotificationFirebaseService : FirebaseMessagingService
{
    public override void OnNewToken(string token)
    {
        base.OnNewToken(token);
        _ = RegisterTokenAsync(token);
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);

        var notification = message.GetNotification();
        if (notification is null)
            return;

        ShowNotification(notification.Title ?? "OpenKSeF", notification.Body ?? string.Empty);
    }

    private static async Task RegisterTokenAsync(string token)
    {
        try
        {
            var service = IPlatformApplication.Current?.Services.GetService<IDeviceTokenService>();
            if (service is not null)
                await service.RegisterTokenAsync(token, "Android");
        }
        catch
        {
            // Best-effort registration
        }
    }

    private void ShowNotification(string title, string body)
    {
        const string channelId = "openksef_invoices";

        var notificationManager = (NotificationManager?)GetSystemService(NotificationService);
        if (notificationManager is null)
            return;

        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(channelId, "Faktury", NotificationImportance.Default)
            {
                Description = "Powiadomienia o nowych fakturach z KSeF"
            };
            notificationManager.CreateNotificationChannel(channel);
        }

        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.ClearTop);
        var pendingIntent = PendingIntent.GetActivity(this, 0, intent,
            PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var builder = new Notification.Builder(this, channelId)
            .SetContentTitle(title)
            .SetContentText(body)
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            .SetAutoCancel(true)
            .SetContentIntent(pendingIntent);

        notificationManager.Notify(DateTime.UtcNow.Millisecond, builder.Build());
    }
}
#endif
