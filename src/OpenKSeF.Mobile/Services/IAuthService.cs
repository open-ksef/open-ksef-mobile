namespace OpenKSeF.Mobile.Services;

public interface IAuthService
{
    Task<bool> LoginAsync();
    Task<bool> LoginWithCredentialsAsync(string email, string password);
    Task<bool> LoginWithGoogleAsync();
    Task<bool> RegisterAsync(string email, string password, string? firstName, string? lastName);
    Task LogoutAsync();
    Task<string?> GetAccessTokenAsync();
    Task<bool> RedeemSetupTokenAsync(string serverUrl, string setupToken);
    bool IsAuthenticated { get; }
}
