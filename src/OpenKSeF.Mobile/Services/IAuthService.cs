namespace OpenKSeF.Mobile.Services;

public enum SessionRestoreResult
{
    None,
    Restored,
    Unverified
}

public interface IAuthService
{
    Task<bool> LoginWithCredentialsAsync(string email, string password);
    Task<bool> LoginWithGoogleAsync();
    Task<bool> RegisterAsync(string email, string password, string? firstName, string? lastName);
    Task LogoutAsync();
    Task<string?> GetAccessTokenAsync();
    Task<SessionRestoreResult> RestoreSessionAsync();
    Task<bool> RedeemSetupTokenAsync(string serverUrl, string setupToken);
    bool IsAuthenticated { get; }
}
