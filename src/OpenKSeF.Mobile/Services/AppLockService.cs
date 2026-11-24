namespace OpenKSeF.Mobile.Services;

/// <summary>App lock (REQ-0033): per-device setting, background timeout and the platform device owner prompt (biometrics or device credential).</summary>
public class AppLockService : IAppLockService
{
    public const int DefaultTimeoutSeconds = 300;
    internal const string PromptTitle = "Odblokuj OpenKSeF";

    private const string EnabledKey = "app_lock_enabled";
    private const string TimeoutKey = "app_lock_timeout";

    private readonly IAuthService _auth;
    private DateTimeOffset? _backgroundedAt;
    private bool _prompting;

    internal Func<bool> DeviceSecure { get; set; } = IsDeviceSecurePlatform;
    internal Func<Task<bool>> Prompt { get; set; } = AuthenticatePlatformAsync;
    internal Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    public AppLockService(IAuthService auth)
    {
        _auth = auth;
    }

    public event Action? LockChanged;
    public event Action? TurnedOffNoScreenLock;

    public bool Enabled
    {
        get => Preferences.Default.Get(EnabledKey, false);
        private set
        {
            Preferences.Default.Set(EnabledKey, value);
            HideRecents(value);
        }
    }

    public int TimeoutSeconds
    {
        get => Preferences.Default.Get(TimeoutKey, DefaultTimeoutSeconds);
        set => Preferences.Default.Set(TimeoutKey, value);
    }

    public bool IsDeviceSecure => DeviceSecure();

    public bool IsLocked { get; private set; }

    /// <summary>A clock set back counts as due, so changing the time never skips the lock.</summary>
    public static bool LockDue(DateTimeOffset backgroundedAt, DateTimeOffset now, int timeoutSeconds) =>
        now < backgroundedAt || now - backgroundedAt >= TimeSpan.FromSeconds(timeoutSeconds);

    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        if (enabled == Enabled || _prompting)
            return false;
        _prompting = true;
        try
        {
            if (!await Prompt())
                return false;
        }
        finally
        {
            _prompting = false;
        }
        Enabled = enabled;
        return true;
    }

    public async Task<bool> StartAsync()
    {
        if (!Enabled)
            return false;
        HideRecents(true);
        SetLocked(true);
        bool signedIn;
        try { signedIn = await _auth.HasStoredSessionAsync(); }
        catch { signedIn = true; }
        if (!signedIn)
        {
            SetLocked(false);
            return false;
        }
        return !TurnOffWithoutScreenLock();
    }

    public void OnBackground()
    {
        if (_prompting || IsLocked || !Enabled || !_auth.HasSession)
            return;
        _backgroundedAt = Now();
        SetLocked(true);
    }

    public bool OnForeground()
    {
        if (_prompting)
            return false;
        var backgroundedAt = _backgroundedAt;
        _backgroundedAt = null;
        if (!IsLocked || TurnOffWithoutScreenLock())
            return false;
        if (backgroundedAt is { } at && !LockDue(at, Now(), TimeoutSeconds))
            SetLocked(false);
        return IsLocked;
    }

    public async Task<bool> UnlockAsync()
    {
        if (_prompting || !IsLocked)
            return !IsLocked;
        _prompting = true;
        try
        {
            if (await Prompt())
                SetLocked(false);
        }
        finally
        {
            _prompting = false;
        }
        return !IsLocked;
    }

    public void Release()
    {
        _backgroundedAt = null;
        SetLocked(false);
    }

    private bool TurnOffWithoutScreenLock()
    {
        if (!Enabled || DeviceSecure())
            return false;
        Enabled = false;
        SetLocked(false);
        TurnedOffNoScreenLock?.Invoke();
        return true;
    }

    private void SetLocked(bool locked)
    {
        if (IsLocked == locked)
            return;
        IsLocked = locked;
        LockChanged?.Invoke();
    }

    private static void HideRecents(bool hide)
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            Platform.CurrentActivity?.SetRecentsScreenshotEnabled(!hide);
#endif
    }

    private static bool IsDeviceSecurePlatform()
    {
#if ANDROID
        var keyguard = (Android.App.KeyguardManager?)Android.App.Application.Context.GetSystemService(Android.Content.Context.KeyguardService);
        return keyguard?.IsDeviceSecure == true;
#elif IOS
        using var context = new LocalAuthentication.LAContext();
        return context.CanEvaluatePolicy(LocalAuthentication.LAPolicy.DeviceOwnerAuthentication, out _);
#else
        return false;
#endif
    }

    private static async Task<bool> AuthenticatePlatformAsync()
    {
#if ANDROID
        if (Platform.CurrentActivity is not AndroidX.Fragment.App.FragmentActivity activity)
            return false;
        var result = new TaskCompletionSource<bool>();
        var prompt = new AndroidX.Biometric.BiometricPrompt(activity, AndroidX.Core.Content.ContextCompat.GetMainExecutor(activity), new PromptCallback(result));
        // BIOMETRIC_STRONG with DEVICE_CREDENTIAL is not supported below API 30; WEAK is, through the AndroidX credential fallback.
        var info = new AndroidX.Biometric.BiometricPrompt.PromptInfo.Builder()
            .SetTitle(PromptTitle)
            .SetAllowedAuthenticators(AndroidX.Biometric.BiometricManager.Authenticators.BiometricWeak | AndroidX.Biometric.BiometricManager.Authenticators.DeviceCredential)
            .Build();
        try
        {
            prompt.Authenticate(info);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[OpenKSeF] App lock prompt failed: {ex}");
            return false;
        }
        return await result.Task;
#elif IOS
        using var context = new LocalAuthentication.LAContext();
        try
        {
            var (ok, _) = await context.EvaluatePolicyAsync(LocalAuthentication.LAPolicy.DeviceOwnerAuthentication, PromptTitle);
            return ok;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[OpenKSeF] App lock prompt failed: {ex}");
            return false;
        }
#else
        await Task.CompletedTask;
        return false;
#endif
    }

#if ANDROID
    private sealed class PromptCallback(TaskCompletionSource<bool> result) : AndroidX.Biometric.BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(AndroidX.Biometric.BiometricPrompt.AuthenticationResult authResult) => result.TrySetResult(true);

        public override void OnAuthenticationError(int errorCode, Java.Lang.ICharSequence errString) => result.TrySetResult(false);
    }
#endif
}
