using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenKSeF.Mobile.Models;

namespace OpenKSeF.Mobile.E2E.Android.Flows;

/// <summary>
/// Integration tests against the dev backend without Appium, deserializing into the app's own linked DTOs
/// with the same System.Text.Json defaults as ApiService. Run with: dotnet test --filter "Category=Integration".
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
        _username = Environment.GetEnvironmentVariable("KEYCLOAK_USERNAME") ?? "testuser";
        _password = Environment.GetEnvironmentVariable("KEYCLOAK_PASSWORD") ?? "Test1234!";
    }

    private string TokenEndpoint => $"{_serverUrl}/auth/realms/openksef/protocol/openid-connect/token";

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");
        return http;
    }

    [Test]
    public async Task RopcLogin_ReturnsValidAccessToken()
    {
        using var http = CreateClient();

        var token = await LoginAsync(http);

        Assert.That(token.AccessToken, Is.Not.Null.And.Not.Empty);
        Assert.That(token.RefreshToken, Is.Not.Null.And.Not.Empty);
        Assert.That(token.ExpiresIn, Is.GreaterThan(0));
    }

    [Test]
    public async Task RefreshTokenGrant_ReturnsNewAccessAndRefreshTokens()
    {
        using var http = CreateClient();
        var login = await LoginAsync(http);

        var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = "openksef-mobile",
            ["refresh_token"] = login.RefreshToken!
        }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var refreshed = await response.Content.ReadFromJsonAsync<KeycloakTokenResponse>();
        Assert.That(refreshed!.AccessToken, Is.Not.Null.And.Not.Empty.And.Not.EqualTo(login.AccessToken));
        Assert.That(refreshed.RefreshToken, Is.Not.Null.And.Not.Empty);
        Assert.That(refreshed.ExpiresIn, Is.GreaterThan(0));

        using var api = CreateApiClient(refreshed.AccessToken!);
        var me = await api.GetAsync($"{_serverUrl}/api/account/me");
        Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetMe_ReturnsUserId()
    {
        using var http = await CreateAuthenticatedClientAsync();

        var me = await GetAsync<UserInfo>(http, "/api/account/me");

        Assert.That(Guid.TryParse(me.UserId, out _), Is.True, $"userId={me.UserId}");
    }

    [Test]
    public async Task OnboardingStatus_DefaultUserIsOnboarded()
    {
        using var http = await CreateAuthenticatedClientAsync();

        var status = await GetAsync<OnboardingStatusDto>(http, "/api/account/onboarding-status");

        Assert.That(status.IsComplete, Is.True);
        Assert.That(status.HasTenant, Is.True);
        Assert.That(Guid.TryParse(status.FirstTenantId, out _), Is.True, $"firstTenantId={status.FirstTenantId}");
    }

    [Test]
    public async Task GetTenants_ReturnsNonEmptyListWithIds()
    {
        using var http = await CreateAuthenticatedClientAsync();

        var tenants = await GetAsync<List<TenantDto>>(http, "/api/tenants");

        Assert.That(tenants, Is.Not.Empty);
        Assert.That(tenants.Select(t => t.Id), Has.None.EqualTo(Guid.Empty));
        Assert.That(tenants.Select(t => t.Nip), Has.All.Match(@"^\d{10}$"));
    }

    [Test]
    public async Task TenantPut_RoundTripsNotificationEmail()
    {
        using var http = await CreateAuthenticatedClientAsync();
        var tenant = (await GetAsync<List<TenantDto>>(http, "/api/tenants"))[0];
        var email = $"qa-int-{Guid.NewGuid():N}@openksef.test";
        var passed = false;

        try
        {
            var updated = await PutTenantAsync(http, tenant.Id, new UpdateTenantRequest { DisplayName = tenant.DisplayName, NotificationEmail = email });
            Assert.That(updated.NotificationEmail, Is.EqualTo(email));

            var reloaded = await GetAsync<TenantDto>(http, $"/api/tenants/{tenant.Id}");
            Assert.That(reloaded.NotificationEmail, Is.EqualTo(email));
            Assert.That(reloaded.DisplayName, Is.EqualTo(tenant.DisplayName));
            passed = true;
        }
        finally
        {
            string? restoreError;
            try
            {
                using var restore = await http.PutAsync($"{_serverUrl}/api/tenants/{tenant.Id}",
                    JsonContent.Create(new UpdateTenantRequest { DisplayName = tenant.DisplayName, NotificationEmail = tenant.NotificationEmail }));
                restoreError = restore.IsSuccessStatusCode ? null : $"Tenant restore failed: {(int)restore.StatusCode} {await restore.Content.ReadAsStringAsync()}";
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                restoreError = $"Tenant restore failed: {e.Message}";
            }
            // An assert here while the body is already failing would replace the original failure.
            if (passed)
                Assert.That(restoreError, Is.Null);
            else if (restoreError is not null)
                TestContext.Out.WriteLine(restoreError);
        }
    }

    [Test]
    public async Task DeviceRegistration_RegisterListDeleteRoundTrip()
    {
        Assume.That(new Uri(_serverUrl).IsLoopback, Is.True, "Device registration runs only against a local dev stack.");
        Assume.That(await GetPushRelayUrlAsync(), Is.Null.Or.Empty,
            "Register sends a confirmation push through the configured relay (DevicesController.Register). Run against a stack without a relay.");
        using var http = await CreateAuthenticatedClientAsync();
        var token = $"qa-int-{Guid.NewGuid():N}";
        Guid? deviceId = null;

        try
        {
            var register = await http.PostAsync($"{_serverUrl}/api/devices/register",
                JsonContent.Create(new RegisterDeviceTokenRequest { Token = token, Platform = 1 }));
            Assert.That(register.StatusCode, Is.EqualTo(HttpStatusCode.OK), await register.Content.ReadAsStringAsync());

            var device = (await GetAsync<List<DeviceTokenDto>>(http, "/api/devices")).SingleOrDefault(d => d.Token == token);
            Assert.That(device, Is.Not.Null, "Registered device missing from GET /api/devices");
            deviceId = device!.Id;
            Assert.That(device.Platform, Is.EqualTo(1));
            Assert.That(device.TenantId, Is.Null);

            var delete = await http.DeleteAsync($"{_serverUrl}/api/devices/{deviceId}");
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            deviceId = null;

            var after = await GetAsync<List<DeviceTokenDto>>(http, "/api/devices");
            Assert.That(after.Select(d => d.Token), Has.None.EqualTo(token));
        }
        finally
        {
            if (deviceId is not null)
                await http.DeleteAsync($"{_serverUrl}/api/devices/{deviceId}");
        }
    }

    [Test]
    public async Task InvoiceListAndDetail_DeserializeIntoAppDtos()
    {
        using var http = await CreateAuthenticatedClientAsync();
        var tenants = await GetAsync<List<TenantDto>>(http, "/api/tenants");

        InvoiceDto? first = null;
        Guid tenantId = default;
        foreach (var tenant in tenants)
        {
            var page = await GetAsync<PagedResult<InvoiceDto>>(http, $"/api/tenants/{tenant.Id}/invoices?page=1&pageSize=5");
            Assert.That(page.Items.Count, Is.LessThanOrEqualTo(5));
            if (page.Items.Count == 0)
                continue;
            Assert.That(page.TotalCount, Is.GreaterThanOrEqualTo(page.Items.Count));
            first = page.Items[0];
            tenantId = tenant.Id;
            break;
        }

        Assert.That(first, Is.Not.Null, "No invoices for any tenant of the default user");
        Assert.That(first!.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(first.KSeFInvoiceNumber, Is.Not.Empty);
        if (first.Currency == "PLN")
            Assert.That(first.AmountGross, Is.EqualTo(first.AmountNet + first.AmountVat));
        Assert.That(first.IssueDate, Is.Not.EqualTo(default(DateTime)));

        var detail = await GetAsync<InvoiceDto>(http, $"/api/tenants/{tenantId}/invoices/{first.Id}");
        Assert.That(detail.Id, Is.EqualTo(first.Id));
        Assert.That(detail.KSeFInvoiceNumber, Is.EqualTo(first.KSeFInvoiceNumber));
        Assert.That(detail.AmountGross, Is.EqualTo(first.AmountGross));
        Assert.That(detail.Lines, Is.Not.Null);
    }

    private async Task<string?> GetPushRelayUrlAsync()
    {
        using var login = CreateClient();
        var admin = await LoginAsync(login,
            Environment.GetEnvironmentVariable("OPENKSEF_ADMIN_USERNAME") ?? "admin@openksef.local",
            Environment.GetEnvironmentVariable("OPENKSEF_ADMIN_PASSWORD") ?? "Admin1234!");
        using var http = CreateApiClient(admin.AccessToken!);
        var response = await http.PostAsync($"{_serverUrl}/api/system/settings", JsonContent.Create(new
        {
            kcAdminUsername = Environment.GetEnvironmentVariable("KEYCLOAK_ADMIN") ?? "admin",
            kcAdminPassword = Environment.GetEnvironmentVariable("KEYCLOAK_ADMIN_PASSWORD") ?? "admin"
        }));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"Relay check needs system settings: {await response.Content.ReadAsStringAsync()}");
        var settings = await response.Content.ReadFromJsonAsync<JsonElement>();
        return settings.GetProperty("pushRelayUrl").GetString();
    }

    private Task<KeycloakTokenResponse> LoginAsync(HttpClient http) => LoginAsync(http, _username, _password);

    private async Task<KeycloakTokenResponse> LoginAsync(HttpClient http, string username, string password)
    {
        var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "openksef-mobile",
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid profile email"
        }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            $"ROPC login failed. Body: {await response.Content.ReadAsStringAsync()}");
        var token = await response.Content.ReadFromJsonAsync<KeycloakTokenResponse>();
        Assert.That(token?.AccessToken, Is.Not.Null.And.Not.Empty, "access_token missing (check snake_case mapping)");
        return token!;
    }

    private static HttpClient CreateApiClient(string accessToken)
    {
        var http = CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return http;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        using var login = CreateClient();
        return CreateApiClient((await LoginAsync(login)).AccessToken!);
    }

    private async Task<T> GetAsync<T>(HttpClient http, string path)
    {
        var response = await http.GetAsync($"{_serverUrl}{path}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"GET {path}: {await response.Content.ReadAsStringAsync()}");
        var body = await response.Content.ReadFromJsonAsync<T>();
        Assert.That(body, Is.Not.Null, $"GET {path} deserialized to null");
        return body!;
    }

    private async Task<TenantDto> PutTenantAsync(HttpClient http, Guid tenantId, UpdateTenantRequest request)
    {
        var response = await http.PutAsync($"{_serverUrl}/api/tenants/{tenantId}", JsonContent.Create(request));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TenantDto>())!;
    }

    private sealed class KeycloakTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
