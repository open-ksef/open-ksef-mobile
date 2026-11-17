namespace OpenKSeF.Mobile.Services;

public enum SessionRestoreResult
{
    None,
    Restored,
    Unverified
}

public enum AuthResult
{
    Rejected,
    Success,
    ServerUnreachable,
    ClientRejected,
    GoogleRequiresHttps,
    Cancelled,
    PairingNotSupported,
    PairingDenied,
    PairingExpired
}

public sealed record DevicePairing(string UserCode, DateTimeOffset ExpiresAt, string DeviceCode, string CodeVerifier, TimeSpan Interval);

public interface IAuthService
{
    Task<AuthResult> LoginInBrowserAsync(bool register = false);
    Task<AuthResult> LoginWithGoogleAsync();
    Task<(AuthResult Result, DevicePairing? Pairing)> StartPairingAsync(CancellationToken cancellationToken);

    /// <summary>Polls the token endpoint until the portal approves, denies or the code expires; network and 5xx failures keep polling.</summary>
    Task<AuthResult> CompletePairingAsync(DevicePairing pairing, CancellationToken cancellationToken);
    Task LogoutAsync();

    /// <summary>Signs out locally and keeps the refresh token only for the device cleanup; the Keycloak logout waits for <see cref="CompleteDeferredLogoutAsync"/>.</summary>
    Task DeferLogoutAsync(string signOutId);

    /// <summary>Null when no deferred logout is left or Keycloak ended that session; throws when the server is unreachable.</summary>
    Task<string?> GetDeferredLogoutAccessTokenAsync(string signOutId, CancellationToken cancellationToken);

    /// <summary>True only when Keycloak logout returns 2xx or invalid_grant; otherwise the refresh token is kept unless <paramref name="dropOnFailure"/>.</summary>
    Task<bool> CompleteDeferredLogoutAsync(string signOutId, bool dropOnFailure = false);
    Task<string?> GetAccessTokenAsync();
    Task<string?> RefreshAccessTokenAsync();
    Task<SessionRestoreResult> RestoreSessionAsync();
    bool IsAuthenticated { get; }
    bool HasSession { get; }
    int SessionGeneration { get; }
    bool LogoutUnderway { get; set; }

    /// <summary>The server refused the refresh token during use. Never raised for a network or server failure, nor while RestoreSessionAsync is waiting or LogoutUnderway is set.</summary>
    event Action? SessionEnded;
}
