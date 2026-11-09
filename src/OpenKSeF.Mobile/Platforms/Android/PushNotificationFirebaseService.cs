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
    public static async Task<string?> TryGetCurrentTokenAsync()
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var tokenTask = FirebaseMessaging.Instance.GetToken();
            tokenTask.AddOnCompleteListener(new FcmTokenCompleteListener(tcs));
        }
        catch
        {
            tcs.TrySetResult(null);
        }

        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            // Token fetch can stall on some devices/networks; return null so caller can use fallback.
            return null;
        }
    }

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

        NotificationHubService.ShowLocalNotification(notification.Title ?? "OpenKSeF", notification.Body ?? string.Empty, message.Data);
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
        }
    }

    private sealed class FcmTokenCompleteListener : Java.Lang.Object, Android.Gms.Tasks.IOnCompleteListener
    {
        private readonly TaskCompletionSource<string?> _tcs;

        public FcmTokenCompleteListener(TaskCompletionSource<string?> tcs)
        {
            _tcs = tcs;
        }

        public void OnComplete(Android.Gms.Tasks.Task task)
        {
            if (task.IsSuccessful)
            {
                _tcs.TrySetResult(task.Result?.ToString());
                return;
            }

            _tcs.TrySetResult(null);
        }
    }
}
#endif
