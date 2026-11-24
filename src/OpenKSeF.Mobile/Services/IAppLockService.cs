namespace OpenKSeF.Mobile.Services;

public interface IAppLockService
{
    bool Enabled { get; }
    int TimeoutSeconds { get; set; }
    bool IsDeviceSecure { get; }
    bool IsLocked { get; }

    event Action? LockChanged;

    /// <summary>Raised when the lock was on and the phone no longer has a screen lock; the setting is already off.</summary>
    event Action? TurnedOffNoScreenLock;

    /// <summary>True when the setting changed; enabling and disabling both need the device owner prompt.</summary>
    Task<bool> SetEnabledAsync(bool enabled);

    /// <summary>Cold start: covers at once, then keeps the cover only for a stored session. True when a prompt is due.</summary>
    Task<bool> StartAsync();

    void OnBackground();

    /// <summary>True when the cover stays and a prompt is due.</summary>
    bool OnForeground();

    Task<bool> UnlockAsync();

    /// <summary>Drops the cover without a prompt, after sign-out.</summary>
    void Release();
}
