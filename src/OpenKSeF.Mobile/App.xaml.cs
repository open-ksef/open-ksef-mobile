using System.Diagnostics;
#if ANDROID || IOS
using Microsoft.Maui.Platform;
#endif
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using OpenKSeF.Mobile.Views;

namespace OpenKSeF.Mobile;

public partial class App : Application
{
    internal const string NoScreenLockTurnedOffText = "Blokada aplikacji wyłączona: telefon nie ma blokady ekranu.";

    private readonly IServiceProvider _services;
    private readonly IAuthService _authService;
    private readonly IAppLockService _appLock;
    private LockView? _cover;
#if IOS
    private bool _shield;
#endif
#if ANDROID
    private LockedBackCallback? _back;
#endif

    public App(IAuthService authService, IAppLockService appLock, IServiceProvider services)
    {
        _services = services;
        _authService = authService;
        _appLock = appLock;
        InitializeComponent();
        ThemeSettings.Apply(ThemeSettings.Load());

        authService.SessionEnded += () => MainThread.BeginInvokeOnMainThread(async () =>
            await services.GetRequiredService<IPostLoginNavigationService>().HandleSessionEndedAsync());

        appLock.LockChanged += () => MainThread.BeginInvokeOnMainThread(ApplyCover);
        appLock.TurnedOffNoScreenLock += () => MainThread.BeginInvokeOnMainThread(async () =>
        {
            try { await Windows[0].Page!.DisplayAlertAsync("Blokada aplikacji", NoScreenLockTurnedOffText, "OK"); } catch { }
        });

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

#if ANDROID
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += OnAndroidUnhandledException;
#endif
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());
        window.Created += async (_, _) =>
        {
            ApplyCover();
            if (await _appLock.StartAsync())
                await _appLock.UnlockAsync();
        };
        window.Stopped += (_, _) => _appLock.OnBackground();
        window.Resumed += (_, _) =>
        {
            if (_appLock.OnForeground())
                window.Dispatcher.Dispatch(() => _ = _appLock.UnlockAsync());
        };
#if IOS
        window.Deactivated += (_, _) =>
        {
            _shield = _appLock.Enabled && _authService.HasSession;
            ApplyCover();
        };
        window.Activated += (_, _) =>
        {
            _shield = false;
            ApplyCover();
        };
#endif
        return window;
    }

    protected override void OnResume() =>
        _ = _services.GetRequiredService<IDeviceTokenService>().CompleteSignOutCleanupAsync();

    /// <summary>The cover sits above the whole window, outside Shell navigation, so no GoToAsync can dismiss it and data pages keep loading under it.</summary>
    private void ApplyCover()
    {
        if (Windows.FirstOrDefault()?.Handler is not { MauiContext: { } context } handler)
            return;
#if IOS
        var show = _appLock.IsLocked || _shield;
#else
        var show = _appLock.IsLocked;
#endif
        if (show)
            _cover ??= new LockView(_services.GetRequiredService<LockViewModel>());
        if (_cover is null)
            return;
#if ANDROID
        var activity = (AndroidX.Activity.ComponentActivity)handler.PlatformView!;
        var decor = (Android.Views.ViewGroup)activity.Window!.DecorView;
        var cover = _cover.ToPlatform(context);
        var content = activity.FindViewById(Android.Resource.Id.Content);
        if (show && cover.Parent != decor)
        {
            (cover.Parent as Android.Views.ViewGroup)?.RemoveView(cover);
            cover.Clickable = true;
            decor.AddView(cover, new Android.Views.ViewGroup.LayoutParams(Android.Views.ViewGroup.LayoutParams.MatchParent, Android.Views.ViewGroup.LayoutParams.MatchParent));
            if (_back?.Activity != activity)
            {
                _back?.Remove();
                _back = new LockedBackCallback(activity);
                activity.OnBackPressedDispatcher.AddCallback(activity, _back);
            }
        }
        else if (!show)
        {
            (cover.Parent as Android.Views.ViewGroup)?.RemoveView(cover);
        }
        if (_back is not null)
            _back.Enabled = show;
        if (content is not null)
            content.ImportantForAccessibility = show ? Android.Views.ImportantForAccessibility.NoHideDescendants : Android.Views.ImportantForAccessibility.Auto;
        // Modal pages sit in a DialogFragment window above the DecorView: hide them and let touches and Back reach the cover.
        var dialogWindows = ((AndroidX.Fragment.App.FragmentActivity)activity).SupportFragmentManager.Fragments
            .OfType<AndroidX.Fragment.App.DialogFragment>().Select(f => f.Dialog?.Window).OfType<Android.Views.Window>().ToList();
        foreach (var modal in Windows[0].Navigation.ModalStack)
        {
            if (modal.Handler?.PlatformView is not Android.Views.View modalView)
                continue;
            modalView.Visibility = show ? Android.Views.ViewStates.Invisible : Android.Views.ViewStates.Visible;
            if (dialogWindows.FirstOrDefault(w => w.DecorView == modalView.RootView) is not { } dialogWindow)
                continue;
            const Android.Views.WindowManagerFlags passThrough = Android.Views.WindowManagerFlags.NotTouchable | Android.Views.WindowManagerFlags.NotFocusable;
            if (show)
                dialogWindow.AddFlags(passThrough);
            else
                dialogWindow.ClearFlags(passThrough);
        }
#elif IOS
        var uiWindow = (UIKit.UIWindow)handler.PlatformView!;
        var cover = _cover.ToPlatform(context);
        if (show)
        {
            cover.Frame = uiWindow.Bounds;
            cover.AutoresizingMask = UIKit.UIViewAutoresizing.FlexibleDimensions;
            if (cover.Superview != uiWindow)
                uiWindow.AddSubview(cover);
            else
                uiWindow.BringSubviewToFront(cover);
        }
        else
        {
            cover.RemoveFromSuperview();
        }
#endif
    }

#if ANDROID
    private sealed class LockedBackCallback(Android.App.Activity activity) : AndroidX.Activity.OnBackPressedCallback(true)
    {
        public Android.App.Activity Activity { get; } = activity;

        public override void HandleOnBackPressed() => Activity.MoveTaskToBack(true);
    }
#endif

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
