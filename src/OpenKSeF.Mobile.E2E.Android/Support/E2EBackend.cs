using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OpenKSeF.Mobile.Models;

namespace OpenKSeF.Mobile.E2E.Android.Support;

/// <summary>
/// Host-side calls to the dev backend: proof that no device token row is left behind, and throwaway Keycloak users for flows that write.
/// </summary>
public static class E2EBackend
{
    public sealed record User(string Username, string Password);

    private static string ServerUrl =>
        (Environment.GetEnvironmentVariable("E2E_BACKEND_URL") ?? "http://localhost:8080").TrimEnd('/');

    private static string KeycloakAdminUrl =>
        (Environment.GetEnvironmentVariable("E2E_KEYCLOAK_URL") ?? "http://localhost:8082").TrimEnd('/');

    public static User? EnvironmentUser()
    {
        var username = Environment.GetEnvironmentVariable("KEYCLOAK_USERNAME");
        var password = Environment.GetEnvironmentVariable("KEYCLOAK_PASSWORD");
        return string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) ? null : new User(username, password);
    }

    public static async Task<int> CountDevicesAsync(User user)
    {
        using var http = await AuthorizedClientAsync(user);
        var devices = await http.GetFromJsonAsync<List<DeviceTokenDto>>($"{ServerUrl}/api/devices");
        return devices!.Count;
    }

    public static async Task<int> WaitForNoDevicesAsync(User user, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        int count;
        while ((count = await CountDevicesAsync(user)) > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(1000);
        return count;
    }

    public static async Task<User> CreateUserAsync()
    {
        var name = $"e2e-mobile-{Guid.NewGuid():N}"[..24];
        var user = new User(name, $"E2e-{Guid.NewGuid():N}");
        using var admin = await AdminClientAsync();
        var response = await admin.PostAsJsonAsync($"{KeycloakAdminUrl}/auth/admin/realms/openksef/users", new
        {
            username = user.Username,
            email = $"{user.Username}@test.open-ksef.pl",
            firstName = "E2E",
            lastName = "Mobile",
            enabled = true,
            emailVerified = true,
            requiredActions = Array.Empty<string>(),
            credentials = new[] { new { type = "password", value = user.Password, temporary = false } }
        });
        response.EnsureSuccessStatusCode();
        return user;
    }

    public static async Task DeleteUserAsync(User user)
    {
        using (var http = await AuthorizedClientAsync(user))
        {
            foreach (var device in (await http.GetFromJsonAsync<List<DeviceTokenDto>>($"{ServerUrl}/api/devices"))!)
                (await http.DeleteAsync($"{ServerUrl}/api/devices/{device.Id}")).EnsureSuccessStatusCode();
            foreach (var tenant in (await http.GetFromJsonAsync<List<TenantDto>>($"{ServerUrl}/api/tenants"))!)
                (await http.DeleteAsync($"{ServerUrl}/api/tenants/{tenant.Id}")).EnsureSuccessStatusCode();
        }

        using var admin = await AdminClientAsync();
        var found = await admin.GetFromJsonAsync<List<JsonElement>>(
            $"{KeycloakAdminUrl}/auth/admin/realms/openksef/users?exact=true&username={Uri.EscapeDataString(user.Username)}");
        foreach (var entry in found!)
            (await admin.DeleteAsync($"{KeycloakAdminUrl}/auth/admin/realms/openksef/users/{entry.GetProperty("id").GetString()}")).EnsureSuccessStatusCode();
    }

    private static async Task<HttpClient> AuthorizedClientAsync(User user)
    {
        var token = await TokenAsync(ServerUrl, "openksef", "openksef-mobile", user.Username, user.Password);
        return BearerClient(token);
    }

    private static async Task<HttpClient> AdminClientAsync()
    {
        var token = await TokenAsync(
            KeycloakAdminUrl,
            "master",
            "admin-cli",
            Environment.GetEnvironmentVariable("KEYCLOAK_ADMIN") ?? "admin",
            Environment.GetEnvironmentVariable("KEYCLOAK_ADMIN_PASSWORD") ?? "admin");
        return BearerClient(token);
    }

    private static async Task<string> TokenAsync(string baseUrl, string realm, string clientId, string username, string password)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var response = await http.PostAsync($"{baseUrl}/auth/realms/{realm}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = clientId,
                ["username"] = username,
                ["password"] = password
            }));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("access_token").GetString()!;
    }

    private static HttpClient BearerClient(string token)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}
