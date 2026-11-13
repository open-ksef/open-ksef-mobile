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
    AccountNotSetUp,
    AccountDisabled,
    ClientRejected,
    GoogleRequiresHttps
}

public interface IAuthService
{
    Task<AuthResult> LoginWithCredentialsAsync(string email, string password);
    Task<AuthResult> LoginWithGoogleAsync();
    Task<AuthResult> RegisterAsync(string email, string password, string? firstName, string? lastName);
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
