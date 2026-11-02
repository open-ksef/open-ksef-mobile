using IdentityModel.OidcClient;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OidcBrowser = IdentityModel.OidcClient.Browser.IBrowser;

namespace OpenKSeF.Mobile.Services;

public class AuthService : IAuthService
{
    private const string Scope = "openid profile email offline_access";

    internal static TimeSpan RestoreTimeout { get; set; } = TimeSpan.FromSeconds(10);

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
    private readonly object _refreshGate = new();
    private Task<SessionRestoreResult>? _refreshInFlight;
    private bool _restorePending;

    public event Action? SessionEnded;

    internal string GoogleLoginScope => _oidcClient.Options.Scope;

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
                ["scope"] = Scope
            };

            using var response = await _httpClient.PostAsync(tokenEndpoint, new FormUrlEncodedContent(formData));
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

            using var response = await _httpClient.PostAsJsonAsync(
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

        if (_refreshToken is null)
            await TryLoadTokensAsync();

        // Cleared before the POST: a refresh answered meanwhile must not read as a dead session.
        var refreshToken = _refreshToken;
        ClearTokens();

        if (refreshToken is not null)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var response = await _httpClient.PostAsync(
                    $"{_authority}/protocol/openid-connect/logout",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = _options.ClientId,
                        ["refresh_token"] = refreshToken
                    }),
                    cts.Token);
            }
            catch
            {
            }
        }
    }

    /// <summary>Only an explicit invalid_grant ends a stored session; a network or server failure must not log the user out.</summary>
    public async Task<SessionRestoreResult> RestoreSessionAsync()
    {
        EnsureConfiguration();

        try
        {
            await TryLoadTokensAsync();
        }
        catch
        {
            return SessionRestoreResult.None;
        }

        if (_refreshToken is null)
            return SessionRestoreResult.None;

        _restorePending = true;
        try
        {
            // The timeout only stops waiting: an abandoned POST may still rotate the token on the server, so later callers must join it.
            return await RefreshOnceAsync().WaitAsync(RestoreTimeout);
        }
        catch (TimeoutException)
        {
            return SessionRestoreResult.Unverified;
        }
        finally
        {
            _restorePending = false;
        }
    }

    private void ClearTokens()
    {
        _accessToken = null;
        _refreshToken = null;
        _tokenExpiry = DateTimeOffset.MinValue;

        SecureStorage.Default.Remove(GetAccessTokenKey());
        SecureStorage.Default.Remove(GetRefreshTokenKey());
        SecureStorage.Default.Remove(GetTokenExpiryKey());
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        EnsureConfiguration();

        if (_accessToken is null)
            await TryLoadTokensAsync();

        if (_accessToken is not null && _tokenExpiry > DateTimeOffset.UtcNow.AddMinutes(1))
            return _accessToken;

        if (_refreshToken is not null && await RefreshOnceAsync() == SessionRestoreResult.Restored)
            return _accessToken;

        return null;
    }

    public async Task<bool> RedeemSetupTokenAsync(string serverUrl, string setupToken)
    {
        try
        {
            var baseUrl = serverUrl.TrimEnd('/');
            var request = new RedeemSetupTokenRequest { SetupToken = setupToken };
            using var response = await _httpClient.PostAsJsonAsync(
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

    /// <summary>A realm with refresh token rotation ends the session when one token is used twice, so all callers share one POST and its result.</summary>
    private Task<SessionRestoreResult> RefreshOnceAsync()
    {
        lock (_refreshGate)
            return _refreshInFlight ??= RunRefreshAsync();
    }

    private async Task<SessionRestoreResult> RunRefreshAsync()
    {
        // Without the yield a synchronous completion clears the field before ??= assigns it, and the finished task is shared forever.
        await Task.Yield();
        try
        {
            var result = await RefreshAsync();
            if (result == SessionRestoreResult.None)
            {
                ClearTokens();
                if (!_restorePending)
                    SessionEnded?.Invoke();
            }

            return result;
        }
        finally
        {
            lock (_refreshGate)
                _refreshInFlight = null;
        }
    }

    private async Task<SessionRestoreResult> RefreshAsync()
    {
        var sent = _refreshToken;
        if (sent is null)
            return SessionRestoreResult.Unverified;

        try
        {
            using var response = await _httpClient.PostAsync(
                $"{_authority}/protocol/openid-connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = _options.ClientId,
                    ["refresh_token"] = sent
                }));

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                return error?.Error == "invalid_grant" && _refreshToken == sent ? SessionRestoreResult.None : SessionRestoreResult.Unverified;
            }

            if (!response.IsSuccessStatusCode)
                return SessionRestoreResult.Unverified;

            var tokens = await response.Content.ReadFromJsonAsync<KeycloakTokenResponse>();
            if (tokens?.AccessToken is null || _refreshToken != sent)
                return SessionRestoreResult.Unverified;

            _accessToken = tokens.AccessToken;
            _refreshToken = tokens.RefreshToken ?? _refreshToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn);

            await StoreTokensAsync();
            return SessionRestoreResult.Restored;
        }
        catch
        {
            return SessionRestoreResult.Unverified;
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
            Scope = Scope,
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
