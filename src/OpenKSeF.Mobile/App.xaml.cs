using System.Diagnostics;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile;

public partial class App : Application
{
    public App(IAuthService authService, IServiceProvider services)
    {
        InitializeComponent();
        UserAppTheme = ThemeSettings.ToAppTheme(ThemeSettings.Load());

        authService.SessionEnded += () => MainThread.BeginInvokeOnMainThread(async () =>
            await services.GetRequiredService<IPostLoginNavigationService>().HandleSessionEndedAsync());

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

#if ANDROID
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += OnAndroidUnhandledException;
#endif
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Trace.WriteLine($"[OpenKSeF] Unhandled exception: {ex}");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Trace.WriteLine($"[OpenKSeF] Unobserved task exception: {e.Exception}");
        e.SetObserved();
    }

#if ANDROID
    private static void OnAndroidUnhandledException(object? sender, Android.Runtime.RaiseThrowableEventArgs e)
    {
        Trace.WriteLine($"[OpenKSeF] Android unhandled exception: {e.Exception}");
    }
#endif
}
