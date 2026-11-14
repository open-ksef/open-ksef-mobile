using Duende.IdentityModel;
using Duende.IdentityModel.Client;
using Duende.IdentityModel.OidcClient;
using Duende.IdentityModel.OidcClient.Browser;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OidcBrowser = Duende.IdentityModel.OidcClient.Browser.IBrowser;

namespace OpenKSeF.Mobile.Services;

public class AuthService : IAuthService
{
    private const string Scope = "openid profile email offline_access";

    internal static TimeSpan RestoreTimeout { get; set; } = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan RefreshRetryDelay = TimeSpan.FromSeconds(15);

    internal TimeProvider Clock { get; set; } = TimeProvider.System;
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

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
    private string? _refreshFailedToken;
    private DateTimeOffset _refreshRetryAt;
    private int _sessionGeneration;

    public event Action? SessionEnded;

    internal string BrowserLoginScope => _oidcClient.Options.Scope;

    public bool LogoutUnderway { get; set; }

    public bool IsAuthenticated => _accessToken is not null && _tokenExpiry > DateTimeOffset.UtcNow;

    public bool HasSession => _refreshToken is not null;

    public int SessionGeneration
    {
        get
        {
            EnsureConfiguration();
            return Volatile.Read(ref _sessionGeneration);
        }
    }

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

    public Task<AuthResult> LoginInBrowserAsync(bool register = false)
    {
        EnsureConfiguration();
        return BrowserLoginAsync(register, null);
    }

    public Task<AuthResult> LoginWithGoogleAsync()
    {
        EnsureConfiguration();

        if (new Uri(_authority).Scheme == Uri.UriSchemeHttp)
            return Task.FromResult(AuthResult.GoogleRequiresHttps);

        return BrowserLoginAsync(false, "google");
    }

    private async Task<AuthResult> BrowserLoginAsync(bool register, string? idpHint)
    {
        // prompt=login: Keycloak logout is deferred until device cleanup, so the Custom Tab SSO cookie would sign the previous user back in.
        var frontChannelParameters = new Parameters { { "prompt", "login" } };
        if (idpHint is not null)
            frontChannelParameters.Add("kc_idp_hint", idpHint);

        LoginResult result;
        try
        {
            var state = await _oidcClient.PrepareLoginAsync(frontChannelParameters);
            var startUrl = register
                ? state.StartUrl.Replace("/protocol/openid-connect/auth?", "/protocol/openid-connect/registrations?")
                : state.StartUrl;

            var browserResult = await _browser.InvokeAsync(new BrowserOptions(startUrl, _options.RedirectUri));
            if (browserResult.ResultType == BrowserResultType.UserCancel)
                return AuthResult.Cancelled;
            if (browserResult.ResultType != BrowserResultType.Success)
                return AuthResult.Rejected;

            result = await _oidcClient.ProcessResponseAsync(browserResult.Response, state);
        }
        // OidcClient throws when discovery fails; a refused sign-in comes back as IsError.
        catch
        {
            return AuthResult.ServerUnreachable;
        }

        if (result.IsError)
            return result.Error is "unauthorized_client" or "invalid_client" ? AuthResult.ClientRejected : AuthResult.Rejected;

        await CompleteLoginAsync(result.AccessToken, result.RefreshToken, result.AccessTokenExpiration);
        return AuthResult.Success;
    }

    public async Task<(AuthResult Result, DevicePairing? Pairing)> StartPairingAsync(CancellationToken cancellationToken)
    {
        EnsureConfiguration();

        var verifier = CryptoRandom.CreateUniqueId(32);
        var challenge = System.Buffers.Text.Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        DeviceAuthorizationResponse response;
        try
        {
            response = await _httpClient.RequestDeviceAuthorizationAsync(new DeviceAuthorizationRequest
            {
                Address = $"{_authority}/protocol/openid-connect/auth/device",
                ClientId = _options.ClientId,
                ClientCredentialStyle = ClientCredentialStyle.PostBody,
                Scope = Scope,
                Parameters =
                {
                    { "code_challenge", challenge },
                    { "code_challenge_method", "S256" }
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (AuthResult.ServerUnreachable, null);
        }
        cancellationToken.ThrowIfCancellationRequested();

        if (!response.IsError && response.DeviceCode is not null && response.UserCode is not null)
            return (AuthResult.Success, new DevicePairing(
                response.UserCode,
                Clock.GetUtcNow().AddSeconds(response.ExpiresIn ?? 300),
                response.DeviceCode,
                verifier,
                TimeSpan.FromSeconds(response.Interval > 0 ? response.Interval : 5)));

        return (MapPairingError(response), null);
    }

    public async Task<AuthResult> CompletePairingAsync(DevicePairing pairing, CancellationToken cancellationToken)
    {
        EnsureConfiguration();

        var interval = pairing.Interval;
        while (true)
        {
            await Delay(interval, cancellationToken);
            if (Clock.GetUtcNow() >= pairing.ExpiresAt)
                return AuthResult.PairingExpired;

            TokenResponse response;
            try
            {
                response = await _httpClient.RequestDeviceTokenAsync(new DeviceTokenRequest
                {
                    Address = $"{_authority}/protocol/openid-connect/token",
                    ClientId = _options.ClientId,
                    ClientCredentialStyle = ClientCredentialStyle.PostBody,
                    DeviceCode = pairing.DeviceCode,
                    Parameters = { { "code_verifier", pairing.CodeVerifier } }
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }
            cancellationToken.ThrowIfCancellationRequested();

            if (!response.IsError && response.AccessToken is not null)
            {
                await CompleteLoginAsync(response.AccessToken, response.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn));
                return AuthResult.Success;
            }

            var error = OAuthError(response);
            if (error == "slow_down")
                interval += TimeSpan.FromSeconds(5);
            else if (error is not (null or "authorization_pending") && (int)response.HttpStatusCode < 500)
                return MapPairingError(response);
        }
    }

    private static AuthResult MapPairingError(ProtocolResponse response) => OAuthError(response) switch
    {
        "unauthorized_client" => AuthResult.PairingNotSupported,
        "invalid_client" => AuthResult.ClientRejected,
        "access_denied" => AuthResult.PairingDenied,
        "expired_token" => AuthResult.PairingExpired,
        null => AuthResult.ServerUnreachable,
        _ when (int)response.HttpStatusCode >= 500 => AuthResult.ServerUnreachable,
        _ => AuthResult.Rejected
    };

    private static string? OAuthError(ProtocolResponse response) =>
        response.Json is { ValueKind: JsonValueKind.Object } json && json.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
            ? error.GetString()
            : null;

    private async Task CompleteLoginAsync(string accessToken, string? refreshToken, DateTimeOffset expiry)
    {
        Interlocked.Increment(ref _sessionGeneration);
        _accessToken = accessToken;
        _refreshToken = refreshToken;
        _tokenExpiry = expiry;

        await StoreLoginTokensAsync();
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
            await PostLogoutAsync(refreshToken);
    }

    public async Task DeferLogoutAsync(string signOutId)
    {
        EnsureConfiguration();

        if (_refreshToken is null)
            await TryLoadTokensAsync();

        var refreshToken = _refreshToken;
        ClearTokens();

        if (refreshToken is not null)
            await SecureStorage.Default.SetAsync(GetDeferredLogoutKey(signOutId), refreshToken);
    }

    public async Task<string?> GetDeferredLogoutAccessTokenAsync(string signOutId, CancellationToken cancellationToken)
    {
        EnsureConfiguration();

        var refreshToken = await SecureStorage.Default.GetAsync(GetDeferredLogoutKey(signOutId));
        if (refreshToken is null)
            return null;

        using var response = await _httpClient.PostAsync(
            $"{_authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _options.ClientId,
                ["refresh_token"] = refreshToken
            }),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.BadRequest
            && (await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken))?.Error == "invalid_grant")
        {
            SecureStorage.Default.Remove(GetDeferredLogoutKey(signOutId));
            return null;
        }

        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<KeycloakTokenResponse>(cancellationToken)
            ?? throw new HttpRequestException("empty token response");
        if (tokens.RefreshToken is not null)
            await SecureStorage.Default.SetAsync(GetDeferredLogoutKey(signOutId), tokens.RefreshToken);

        return tokens.AccessToken;
    }

    public async Task<bool> CompleteDeferredLogoutAsync(string signOutId, bool dropOnFailure = false)
    {
        EnsureConfiguration();

        var refreshToken = await SecureStorage.Default.GetAsync(GetDeferredLogoutKey(signOutId));
        var ended = refreshToken is null || await PostLogoutAsync(refreshToken);
        if (ended || dropOnFailure)
            SecureStorage.Default.Remove(GetDeferredLogoutKey(signOutId));
        return ended;
    }

    private async Task<bool> PostLogoutAsync(string refreshToken)
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
            return response.IsSuccessStatusCode
                || (response.StatusCode == HttpStatusCode.BadRequest
                    && (await response.Content.ReadFromJsonAsync<ErrorResponse>(cts.Token))?.Error == "invalid_grant");
        }
        catch
        {
            return false;
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
        Interlocked.Increment(ref _sessionGeneration);
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

        return _accessToken is not null && _tokenExpiry > DateTimeOffset.UtcNow.AddSeconds(10) ? _accessToken : null;
    }

    public async Task<string?> RefreshAccessTokenAsync()
    {
        EnsureConfiguration();

        return await RefreshOnceAsync() == SessionRestoreResult.Restored ? _accessToken : null;
    }

    /// <summary>A realm with refresh token rotation ends the session when one token is used twice, so all callers share one POST and its result.</summary>
    private Task<SessionRestoreResult> RefreshOnceAsync()
    {
        lock (_refreshGate)
        {
            if (_refreshInFlight is null && _refreshToken is not null && _refreshToken == _refreshFailedToken && Clock.GetUtcNow() < _refreshRetryAt)
                return Task.FromResult(SessionRestoreResult.Unverified);

            return _refreshInFlight ??= RunRefreshAsync();
        }
    }

    private async Task<SessionRestoreResult> RunRefreshAsync()
    {
        // Without the yield a synchronous completion clears the field before ??= assigns it, and the finished task is shared forever.
        await Task.Yield();
        try
        {
            var sent = _refreshToken;
            var result = await RefreshAsync(sent);
            if (result == SessionRestoreResult.Unverified && sent is not null)
            {
                _refreshFailedToken = sent;
                _refreshRetryAt = Clock.GetUtcNow() + RefreshRetryDelay;
            }
            else if (result == SessionRestoreResult.None)
            {
                ClearTokens();
                if (!_restorePending && !LogoutUnderway)
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

    private async Task<SessionRestoreResult> RefreshAsync(string? sent)
    {
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

    private async Task StoreLoginTokensAsync()
    {
        try
        {
            await StoreTokensAsync();
        }
        catch
        {
            ClearTokens();
            throw;
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

        Interlocked.Increment(ref _sessionGeneration);
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
            HttpClientFactory = _ => _httpClient,
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
    private string GetDeferredLogoutKey(string signOutId) => $"{_storagePrefix}:deferred_logout:{signOutId}";

    private sealed class KeycloakTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
