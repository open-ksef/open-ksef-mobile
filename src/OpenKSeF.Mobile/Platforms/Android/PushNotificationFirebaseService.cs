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
    public static async Task<string?> TryGetCurrentTokenAsync(bool enable)
    {
        if (!enable && !HasRegisteredInstallation())
            return null;

        var task = await AwaitAsync(() =>
        {
            if (enable)
                FirebaseMessaging.Instance.AutoInitEnabled = true;
            return FirebaseMessaging.Instance.GetToken();
        });
        return task is { IsSuccessful: true } ? task.Result?.ToString() : null;
    }

    public static void DisableAutoInit() => FirebaseMessaging.Instance.AutoInitEnabled = false;

    public static async Task DeleteAsync()
    {
        if (await AwaitAsync(() => FirebaseMessaging.Instance.DeleteToken()) is { IsSuccessful: true })
            await AwaitAsync(() => FirebaseInstallations.Instance.Delete());
    }

    // GetToken registers an installation when none is registered; the SDK keeps its state in PersistedInstallation.*.json.
    private static bool HasRegisteredInstallation()
    {
        try
        {
            var context = Android.App.Application.Context;
            return new[] { context.FilesDir, context.NoBackupFilesDir }
                .Where(dir => dir is not null)
                .SelectMany(dir => Directory.EnumerateFiles(dir!.AbsolutePath, "PersistedInstallation*.json"))
                .Any(f => File.ReadAllText(f).Contains("\"Status\":3", StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }

    private static async Task<Android.Gms.Tasks.Task?> AwaitAsync(Func<Android.Gms.Tasks.Task> start)
    {
        var tcs = new TaskCompletionSource<Android.Gms.Tasks.Task?>(TaskCreationOptions.RunContinuationsAsynchronously);

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
        private readonly TaskCompletionSource<Android.Gms.Tasks.Task?> _tcs;

        public FcmTaskCompleteListener(TaskCompletionSource<Android.Gms.Tasks.Task?> tcs)
        {
            _tcs = tcs;
        }

        public void OnComplete(Android.Gms.Tasks.Task task) => _tcs.TrySetResult(task);
    }
}
#endif
