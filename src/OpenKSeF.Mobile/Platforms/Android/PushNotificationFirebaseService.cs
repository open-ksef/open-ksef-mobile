#if FIREBASE_ENABLED
using Android.App;
using Android.Content;
using Firebase.Installations;
using Firebase.Messaging;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile;

[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public class PushNotificationFirebaseService : FirebaseMessagingService
{
    public static Task<string?> TryGetCurrentTokenAsync(bool enable)
    {
        if (enable)
            FirebaseMessaging.Instance.AutoInitEnabled = true;
        else if (!HasRegisteredInstallation())
            return Task.FromResult<string?>(null);

        return AwaitAsync(() => FirebaseMessaging.Instance.GetToken());
    }

    public static async Task DisableAsync()
    {
        FirebaseMessaging.Instance.AutoInitEnabled = false;
        if (!HasRegisteredInstallation())
            return;

        await AwaitAsync(() => FirebaseMessaging.Instance.DeleteToken());
        await AwaitAsync(() => FirebaseInstallations.Instance.Delete());
    }

    // Only a registered installation (Status 3 in the SDK's PersistedInstallation.*.json) is known to Google; deleteToken on any other state registers one first.
    private static bool HasRegisteredInstallation() =>
        Directory.EnumerateFiles(Android.App.Application.Context.FilesDir!.AbsolutePath, "PersistedInstallation*")
            .Any(f => File.ReadAllText(f).Contains("\"Status\":3", StringComparison.Ordinal));

    private static async Task<string?> AwaitAsync(Func<Android.Gms.Tasks.Task> start)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            start().AddOnCompleteListener(new FcmTaskCompleteListener(tcs));
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

    private sealed class FcmTaskCompleteListener : Java.Lang.Object, Android.Gms.Tasks.IOnCompleteListener
    {
        private readonly TaskCompletionSource<string?> _tcs;

        public FcmTaskCompleteListener(TaskCompletionSource<string?> tcs)
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
