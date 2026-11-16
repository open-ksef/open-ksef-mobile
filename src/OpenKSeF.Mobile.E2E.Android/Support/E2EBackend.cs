using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OpenKSeF.Mobile.Models;

namespace OpenKSeF.Mobile.E2E.Android.Support;

/// <summary>
/// Host-side calls to the dev backend: proof that no device token row is left behind, and throwaway Keycloak users with seeded tenants and invoices.
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

    public const string SeedAccount = "95000000009999000011112222";
    public const string SeedVendorNip = "1234567890";
    public const string SeedGrossGrosze = "012300";

    public static async Task<List<DeviceTokenDto>> GetDevicesAsync(User user)
    {
        using var http = await AuthorizedClientAsync(user);
        return (await http.GetFromJsonAsync<List<DeviceTokenDto>>($"{ServerUrl}/api/devices"))!;
    }

    public static async Task<int> CountDevicesAsync(User user) => (await GetDevicesAsync(user)).Count;

    public static async Task<List<string>> GetInvoiceNumbersAsync(User user)
    {
        using var http = await AuthorizedClientAsync(user);
        var numbers = new List<string>();
        foreach (var tenant in (await http.GetFromJsonAsync<List<TenantDto>>($"{ServerUrl}/api/tenants"))!)
        {
            var page = await http.GetFromJsonAsync<PagedResult<InvoiceDto>>($"{ServerUrl}/api/tenants/{tenant.Id}/invoices");
            numbers.AddRange(page!.Items.Select(i => i.DisplayNumber));
        }
        return numbers;
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

    public static async Task<User> CreateSeededUserAsync()
    {
        var user = await CreateUserAsync();
        try
        {
            await SeedTenantsAsync(user);
        }
        catch
        {
            await DeleteUserAsync(user);
            throw;
        }
        return user;
    }

    private static async Task SeedTenantsAsync(User user)
    {
        using var http = await AuthorizedClientAsync(user);
        foreach (var (nip, name) in new[] { ("1111111111", "E2E Seed A"), ("2222222222", "E2E Seed B") })
        {
            var response = await http.PostAsJsonAsync($"{ServerUrl}/api/tenants", new { nip, displayName = name });
            response.EnsureSuccessStatusCode();
            var tenant = (await response.Content.ReadFromJsonAsync<TenantDto>())!;
            await PsqlAsync($"""
                INSERT INTO "InvoiceHeaders" ("Id","TenantId","KSeFInvoiceNumber","KSeFReferenceNumber","InvoiceNumber","VendorName","VendorNip",
                  "AmountNet","AmountVat","AmountGross","Currency","IssueDate","DueDate","FirstSeenAt","LastUpdatedAt","NotifiedAt","Direction","IsPaid",
                  "InvoiceType","Settlement","VendorBankAccount")
                VALUES (gen_random_uuid(),'{tenant.Id}','E2E-{tenant.Id:N}','E2E-REF-{tenant.Id:N}','FV/E2E/{tenant.Id.ToString("N")[..8]}','E2E Vendor','{SeedVendorNip}',
                  100.00,23.00,123.00,'PLN',now(),current_date + 14,now(),now(),now(),'Received',false,
                  'Vat',1,'{SeedAccount}')
                """);
        }
    }

    public static async Task<bool> RegistrationAllowedAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var status = await http.GetFromJsonAsync<JsonElement>($"{ServerUrl}/api/system/setup-status");
        return status.GetProperty("registrationAllowed").GetBoolean();
    }

    public static void Unmet(string reason)
    {
        if (Environment.GetEnvironmentVariable("E2E_ALLOW_SKIP") == "1")
            Assert.Ignore(reason);
        Assert.Fail($"{reason} Set E2E_ALLOW_SKIP=1 to skip instead.");
    }

    private static async Task PsqlAsync(string sql)
    {
        var container = Environment.GetEnvironmentVariable("E2E_POSTGRES_CONTAINER") ?? "openksef-postgres";
        var psi = new ProcessStartInfo("docker") { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "exec", container, "psql", "-U", "openksef", "-d", "openksef", "-v", "ON_ERROR_STOP=1", "-c", sql })
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        var error = process.StandardError.ReadToEndAsync();
        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"psql failed: {await error}");
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
