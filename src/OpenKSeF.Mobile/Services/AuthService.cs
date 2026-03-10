using IdentityModel.OidcClient;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OidcBrowser = IdentityModel.OidcClient.Browser.IBrowser;

namespace OpenKSeF.Mobile.Services;

public class AuthService : IAuthService
{
    private readonly AuthOptions _options;
    private readonly OidcBrowser _browser;
    private readonly IServerSettingsService _serverSettings;
    private readonly HttpClient _httpClient;
    private OidcClient _oidcClient;
    private string _authority;
    private string _storagePrefix;
    private string? _accessToken;
    private string? _refreshToken;
    private DateTimeOffset _tokenExpiry;

    public bool IsAuthenticated => _accessToken is not null && _tokenExpiry > DateTimeOffset.UtcNow;

    public AuthService(AuthOptions options, OidcBrowser browser, IServerSettingsService serverSettings, HttpClient httpClient)
    {
        _options = options;
        _browser = browser;
        _serverSettings = serverSettings;
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");
        _authority = _serverSettings.Authority;
        _storagePrefix = BuildStoragePrefix(_authority);
        _oidcClient = CreateOidcClient(_authority);
    }

    public async Task<bool> LoginAsync()
    {
        EnsureConfiguration();

        try
        {
            var result = await _oidcClient.LoginAsync(new LoginRequest());

            if (result.IsError)
                return false;

            _accessToken = result.AccessToken;
            _refreshToken = result.RefreshToken;
            _tokenExpiry = result.AccessTokenExpiration;

            await StoreTokensAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> LoginWithCredentialsAsync(string email, string password)
    {
        EnsureConfiguration();

        try
        {
            var tokenEndpoint = $"{_authority}/protocol/openid-connect/token";
            var formData = new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = _options.ClientId,
                ["username"] = email,
                ["password"] = password,
                ["scope"] = "openid profile email"
            };

            var response = await _httpClient.PostAsync(tokenEndpoint, new FormUrlEncodedContent(formData));
            if (!response.IsSuccessStatusCode)
                return false;

            var tokenResponse = await response.Content.ReadFromJsonAsync<KeycloakTokenResponse>();
            if (tokenResponse?.AccessToken is null)
                return false;

            _accessToken = tokenResponse.AccessToken;
            _refreshToken = tokenResponse.RefreshToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            await StoreTokensAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> LoginWithGoogleAsync()
    {
        EnsureConfiguration();

        try
        {
            var loginRequest = new LoginRequest();
            loginRequest.FrontChannelExtraParameters.Add("kc_idp_hint", "google");
            var result = await _oidcClient.LoginAsync(loginRequest);

            if (result.IsError)
                return false;

            _accessToken = result.AccessToken;
            _refreshToken = result.RefreshToken;
            _tokenExpiry = result.AccessTokenExpiration;

            await StoreTokensAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RegisterAsync(string email, string password, string? firstName, string? lastName)
    {
        EnsureConfiguration();

        try
        {
            var baseUrl = _serverSettings.ServerUrl.TrimEnd('/');
            var registerPayload = new RegisterAccountRequest
            {
                Email = email,
                Password = password,
                FirstName = firstName,
                LastName = lastName
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{baseUrl}/api/account/register", registerPayload);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                var error = JsonSerializer.Deserialize<ErrorResponse>(body);
                throw new InvalidOperationException(error?.Error ?? "Rejestracja nie powiodła się.");
            }

            var loginSuccess = await LoginWithCredentialsAsync(email, password);
            if (!loginSuccess)
                throw new InvalidOperationException("Konto zostało utworzone. Zaloguj się za pomocą formularza logowania.");

            return true;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        EnsureConfiguration();
        _accessToken = null;
        _refreshToken = null;
        _tokenExpiry = DateTimeOffset.MinValue;

        SecureStorage.Default.Remove(GetAccessTokenKey());
        SecureStorage.Default.Remove(GetRefreshTokenKey());
        SecureStorage.Default.Remove(GetTokenExpiryKey());

        await Task.CompletedTask;
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        EnsureConfiguration();

        if (_accessToken is null)
            await TryLoadTokensAsync();

        if (_accessToken is not null && _tokenExpiry > DateTimeOffset.UtcNow.AddMinutes(1))
            return _accessToken;

        if (_refreshToken is not null)
        {
            var refreshed = await TryRefreshTokenAsync();
            if (refreshed)
                return _accessToken;
        }

        return null;
    }

    public async Task<bool> RedeemSetupTokenAsync(string serverUrl, string setupToken)
    {
        try
        {
            var baseUrl = serverUrl.TrimEnd('/');
            var request = new RedeemSetupTokenRequest { SetupToken = setupToken };
            var response = await _httpClient.PostAsJsonAsync(
                $"{baseUrl}/api/account/redeem-setup-token", request);

            if (!response.IsSuccessStatusCode)
                return false;

            var result = await response.Content.ReadFromJsonAsync<RedeemTokenResponse>();
            if (result?.AccessToken is null)
                return false;

            EnsureConfiguration();

            _accessToken = result.AccessToken;
            _refreshToken = result.RefreshToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn);

            await StoreTokensAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryRefreshTokenAsync()
    {
        try
        {
            var result = await _oidcClient.RefreshTokenAsync(_refreshToken!);

            if (result.IsError)
                return false;

            _accessToken = result.AccessToken;
            _refreshToken = result.RefreshToken;
            _tokenExpiry = result.AccessTokenExpiration;

            await StoreTokensAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task StoreTokensAsync()
    {
        if (_accessToken is not null)
            await SecureStorage.Default.SetAsync(GetAccessTokenKey(), _accessToken);
        if (_refreshToken is not null)
            await SecureStorage.Default.SetAsync(GetRefreshTokenKey(), _refreshToken);

        await SecureStorage.Default.SetAsync(GetTokenExpiryKey(), _tokenExpiry.ToString("O"));
    }

    private async Task TryLoadTokensAsync()
    {
        _accessToken = await SecureStorage.Default.GetAsync(GetAccessTokenKey());
        _refreshToken = await SecureStorage.Default.GetAsync(GetRefreshTokenKey());

        var expiryStr = await SecureStorage.Default.GetAsync(GetTokenExpiryKey());
        if (expiryStr is not null && DateTimeOffset.TryParse(expiryStr, out var expiry))
            _tokenExpiry = expiry;
    }

    private void EnsureConfiguration()
    {
        var authority = _serverSettings.Authority;
        if (string.Equals(_authority, authority, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _authority = authority;
        _storagePrefix = BuildStoragePrefix(authority);
        _oidcClient = CreateOidcClient(authority);
        _accessToken = null;
        _refreshToken = null;
        _tokenExpiry = DateTimeOffset.MinValue;
    }

    private OidcClient CreateOidcClient(string authority)
    {
        var oidcOptions = new OidcClientOptions
        {
            Authority = authority,
            ClientId = _options.ClientId,
            Scope = "openid profile email",
            RedirectUri = _options.RedirectUri,
            PostLogoutRedirectUri = _options.PostLogoutRedirectUri,
            Browser = _browser,
            DisablePushedAuthorization = true
        };

        return new OidcClient(oidcOptions);
    }

    private static string BuildStoragePrefix(string authority)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(authority));
        var hash = Convert.ToHexString(hashBytes);
        return $"oidc:{hash}";
    }

    private string GetAccessTokenKey() => $"{_storagePrefix}:access_token";
    private string GetRefreshTokenKey() => $"{_storagePrefix}:refresh_token";
    private string GetTokenExpiryKey() => $"{_storagePrefix}:token_expiry";

    private sealed class KeycloakTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class RegisterAccountRequest
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = "";

        [JsonPropertyName("password")]
        public string Password { get; set; } = "";

        [JsonPropertyName("firstName")]
        public string? FirstName { get; set; }

        [JsonPropertyName("lastName")]
        public string? LastName { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private sealed class RedeemSetupTokenRequest
    {
        [JsonPropertyName("setupToken")]
        public string SetupToken { get; set; } = "";
    }

    private sealed class RedeemTokenResponse
    {
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refreshToken")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expiresIn")]
        public int ExpiresIn { get; set; }
    }
}
