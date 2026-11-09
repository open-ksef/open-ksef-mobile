using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
                           ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (savedInstanceState is null)
            OnNotificationTapped(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        OnNotificationTapped(intent);
    }

    private static void OnNotificationTapped(Intent? intent) =>
        NotificationHubService.OnNotificationTapped(intent?.GetStringExtra("type"), intent?.GetStringExtra("tenantId"), intent?.GetStringExtra("invoiceId"));
}
