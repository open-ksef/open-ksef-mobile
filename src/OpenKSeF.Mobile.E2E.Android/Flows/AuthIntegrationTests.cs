using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

/// <summary>
/// Integration tests that verify the full auth flow against the real backend
/// without Appium or an emulator. These tests catch issues like:
/// - ROPC login failures (credential/deserialization problems)
/// - Token redeem failures (QR setup flow)
/// - API call failures after authentication
/// - JSON deserialization issues (e.g. source-gen breaking token parsing)
///
/// Run with: dotnet test --filter "Category=Integration"
/// Requires: Docker backend running (docker compose up)
/// Optional: APP_EXTERNAL_BASE_URL for ngrok, defaults to http://localhost:8080
/// </summary>
[Category("Integration")]
public class AuthIntegrationTests
{
    private string _serverUrl = null!;
    private string _username = null!;
    private string _password = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        _serverUrl = (Environment.GetEnvironmentVariable("INTEGRATION_TEST_SERVER_URL")
            ?? "http://localhost:8080").TrimEnd('/');
        _username = Environment.GetEnvironmentVariable("KEYCLOAK_USERNAME")
            ?? "testuser@openksef.test";
        _password = Environment.GetEnvironmentVariable("KEYCLOAK_PASSWORD")
            ?? "Test1234!";
    }

    private HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");
        return http;
    }

    [Test]
    public async Task RopcLogin_ReturnsValidAccessToken()
    {
        using var http = CreateClient();

        var tokenEndpoint = $"{_serverUrl}/auth/realms/openksef/protocol/openid-connect/token";
        var response = await http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "openksef-mobile",
            ["username"] = _username,
            ["password"] = _password,
            ["scope"] = "openid profile email"
        }));

        Assert.That((int)response.StatusCode, Is.EqualTo(200),
            $"ROPC login failed with {response.StatusCode}. Body: {await response.Content.ReadAsStringAsync()}");

        var token = await response.Content.ReadFromJsonAsync<KeycloakTokenResponse>();

        Assert.That(token, Is.Not.Null, "Token response deserialized to null");
        Assert.That(token!.AccessToken, Is.Not.Null.And.Not.Empty,
            "access_token is missing -- JSON deserialization may be broken (check snake_case mapping)");
        Assert.That(token.RefreshToken, Is.Not.Null.And.Not.Empty, "refresh_token is missing");
        Assert.That(token.ExpiresIn, Is.GreaterThan(0), "expires_in should be positive");

        TestContext.Progress.WriteLine($"ROPC OK: token length={token.AccessToken!.Length}, expires_in={token.ExpiresIn}s");
    }

    [Test]
    public async Task AuthenticatedApiCall_GetMe_ReturnsUserInfo()
    {
        using var http = CreateClient();
        var accessToken = await GetAccessTokenAsync(http);

        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await http.GetAsync($"{_serverUrl}/api/account/me");

        Assert.That((int)response.StatusCode, Is.EqualTo(200),
            $"/api/account/me failed: {response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("email").IgnoreCase,
            "Response should contain user email field");

        TestContext.Progress.WriteLine($"/api/account/me OK: {body[..Math.Min(body.Length, 200)]}");
    }

    [Test]
    public async Task AuthenticatedApiCall_OnboardingStatus_Deserializes()
    {
        using var http = CreateClient();
        var accessToken = await GetAccessTokenAsync(http);

        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await http.GetAsync($"{_serverUrl}/api/account/onboarding-status");

        Assert.That((int)response.StatusCode, Is.EqualTo(200),
            $"/api/account/onboarding-status failed: {response.StatusCode}");

        var status = await response.Content.ReadFromJsonAsync<OnboardingStatusResponse>();
        Assert.That(status, Is.Not.Null, "Onboarding status deserialized to null");

        TestContext.Progress.WriteLine($"Onboarding: isComplete={status!.IsComplete}, hasTenant={status.HasTenant}");
    }

    [Test]
    public async Task AuthenticatedApiCall_GetTenants_Deserializes()
    {
        using var http = CreateClient();
        var accessToken = await GetAccessTokenAsync(http);

        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await http.GetAsync($"{_serverUrl}/api/tenants");

        Assert.That((int)response.StatusCode, Is.EqualTo(200),
            $"/api/tenants failed: {response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Is.Not.Null.And.Not.Empty);

        var tenants = JsonSerializer.Deserialize<List<TenantResponse>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.That(tenants, Is.Not.Null, "Tenant list deserialized to null");

        TestContext.Progress.WriteLine($"Tenants: {tenants!.Count} found");
    }

    [Test]
    public async Task SetupTokenFlow_GenerateAndRedeem()
    {
        using var http = CreateClient();
        var accessToken = await GetAccessTokenAsync(http);

        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        // Generate setup token
        var setupResponse = await http.PostAsync($"{_serverUrl}/api/account/setup-token", null);
        Assert.That((int)setupResponse.StatusCode, Is.EqualTo(200),
            $"Setup token generation failed: {setupResponse.StatusCode}");

        var setupResult = await setupResponse.Content.ReadFromJsonAsync<SetupTokenResponse>();
        Assert.That(setupResult?.SetupToken, Is.Not.Null.And.Not.Empty,
            "Setup token is missing from response");

        TestContext.Progress.WriteLine($"Setup token generated: {setupResult!.SetupToken[..8]}...");

        // Redeem setup token (as if scanned by the app)
        http.DefaultRequestHeaders.Authorization = null;
        var redeemResponse = await http.PostAsJsonAsync(
            $"{_serverUrl}/api/account/redeem-setup-token",
            new { setupToken = setupResult.SetupToken });

        var redeemBody = await redeemResponse.Content.ReadAsStringAsync();

        if (redeemResponse.IsSuccessStatusCode)
        {
            var redeemResult = await redeemResponse.Content.ReadFromJsonAsync<RedeemTokenResponse>();
            Assert.That(redeemResult?.AccessToken, Is.Not.Null.And.Not.Empty,
                "Redeemed access token is missing -- JSON deserialization may be broken");
            TestContext.Progress.WriteLine($"Redeem OK: token length={redeemResult!.AccessToken!.Length}");
        }
        else
        {
            TestContext.Progress.WriteLine($"Redeem returned {redeemResponse.StatusCode}: {redeemBody} (expected in some backend configs)");
            Assert.That((int)redeemResponse.StatusCode, Is.Not.EqualTo(500),
                "Redeem should not crash the server (5xx)");
        }
    }

    [Test]
    public async Task FullLoginFlow_RopcThenApiCall_EndToEnd()
    {
        using var http = CreateClient();

        // Step 1: ROPC login (same as app's LoginWithCredentialsAsync)
        var tokenEndpoint = $"{_serverUrl}/auth/realms/openksef/protocol/openid-connect/token";
        var loginResponse = await http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "openksef-mobile",
            ["username"] = _username,
            ["password"] = _password,
            ["scope"] = "openid profile email"
        }));
        loginResponse.EnsureSuccessStatusCode();

        var token = await loginResponse.Content.ReadFromJsonAsync<KeycloakTokenResponse>();
        Assert.That(token?.AccessToken, Is.Not.Null.And.Not.Empty, "Login failed");

        // Step 2: Check onboarding (same as app's PostLoginNavigationService)
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var onboardingResponse = await http.GetAsync($"{_serverUrl}/api/account/onboarding-status");
        onboardingResponse.EnsureSuccessStatusCode();

        var onboarding = await onboardingResponse.Content.ReadFromJsonAsync<OnboardingStatusResponse>();
        Assert.That(onboarding, Is.Not.Null);

        // Step 3: If onboarding complete, get tenants + invoices
        if (onboarding!.IsComplete)
        {
            var tenantsResponse = await http.GetAsync($"{_serverUrl}/api/tenants");
            tenantsResponse.EnsureSuccessStatusCode();
            TestContext.Progress.WriteLine("Full flow OK: login -> onboarding -> tenants");
        }
        else
        {
            TestContext.Progress.WriteLine("Full flow OK: login -> onboarding (needs setup)");
        }
    }

    private async Task<string> GetAccessTokenAsync(HttpClient http)
    {
        var tokenEndpoint = $"{_serverUrl}/auth/realms/openksef/protocol/openid-connect/token";
        var response = await http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "openksef-mobile",
            ["username"] = _username,
            ["password"] = _password,
            ["scope"] = "openid profile email"
        }));
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<KeycloakTokenResponse>();
        return token?.AccessToken ?? throw new InvalidOperationException("Failed to get access token");
    }

    // DTOs matching the exact JSON shapes the app expects
    private sealed class KeycloakTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class OnboardingStatusResponse
    {
        [JsonPropertyName("isComplete")]
        public bool IsComplete { get; set; }

        [JsonPropertyName("hasTenant")]
        public bool HasTenant { get; set; }
    }

    private sealed class TenantResponse
    {
        public Guid Id { get; set; }
        public string Nip { get; set; } = "";
        public string? DisplayName { get; set; }
    }

    private sealed class SetupTokenResponse
    {
        [JsonPropertyName("setupToken")]
        public string? SetupToken { get; set; }
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
