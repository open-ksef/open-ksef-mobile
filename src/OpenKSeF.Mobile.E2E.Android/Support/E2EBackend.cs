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

    private static readonly Dictionary<string, (string Token, DateTime Until)> UserTokens = [];

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

    public static async Task<int> WaitForDevicesAsync(User user, int expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        int count;
        while ((count = await CountDevicesAsync(user)) < expected && DateTime.UtcNow < deadline)
            await Task.Delay(1000);
        return count;
    }

    public static async Task<bool> StaysWithoutDevicesAsync(User user, TimeSpan period)
    {
        var deadline = DateTime.UtcNow + period;
        do
        {
            if (await CountDevicesAsync(user) > 0)
                return false;
            await Task.Delay(1000);
        } while (DateTime.UtcNow < deadline);
        return true;
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
        foreach (var name in new[] { "E2E Seed A", "E2E Seed B" })
        {
            var nip = FakeNip.Next();
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

    public static async Task CompleteSellerProfilesAsync(User user)
    {
        using var http = await AuthorizedClientAsync(user);
        foreach (var tenant in (await http.GetFromJsonAsync<List<TenantDto>>($"{ServerUrl}/api/tenants"))!)
        {
            var profile = (await http.GetFromJsonAsync<SellerProfileDto>($"{ServerUrl}/api/tenants/{tenant.Id}/seller-profile"))!;
            profile.LegalName = tenant.DisplayName ?? "E2E Seed";
            profile.AddressLine1 = "ul. Testowa 1";
            profile.AddressLine2 = "00-001 Warszawa";
            (await http.PutAsJsonAsync<SellerProfileRequest>($"{ServerUrl}/api/tenants/{tenant.Id}/seller-profile", profile)).EnsureSuccessStatusCode();
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
        psi.Environment.Remove("DOCKER_HOST");
        psi.Environment["DOCKER_CONTEXT"] = "default";
        foreach (var arg in new[] { "exec", container, "psql", "-U", "openksef", "-d", "openksef", "-v", "ON_ERROR_STOP=1", "-c", sql })
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        var error = process.StandardError.ReadToEndAsync();
        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"psql failed: {await error}");
    }

    // App data goes through psql, not the API: a user whose 2FA setup failed cannot get a token.
    public static async Task DeleteUserAsync(User user)
    {
        if (!user.Username.StartsWith("e2e-mobile-", StringComparison.Ordinal))
            throw new InvalidOperationException($"Refusing to delete non-E2E user {user.Username}.");
        using var admin = await AdminClientAsync();
        var ids = (await KeycloakUserIdsAsync(admin, user.Username)).ToList();
        try
        {
            foreach (var id in ids)
                await PsqlAsync($"""
                    DELETE FROM "DeviceTokens" WHERE "UserId" = '{id}';
                    DELETE FROM "IssuedInvoices" WHERE "TenantId" IN (SELECT "Id" FROM "Tenants" WHERE "UserId" = '{id}');
                    DELETE FROM "Tenants" WHERE "UserId" = '{id}';
                    """);
        }
        finally
        {
            foreach (var id in ids)
                (await admin.DeleteAsync($"{KeycloakAdminUrl}/auth/admin/realms/openksef/users/{id}")).EnsureSuccessStatusCode();
            UserTokens.Remove(user.Username);
            File.Delete(AndroidOidcLoginFlow.SecretPath(user.Username));
        }
    }

    public static async Task EndKeycloakSessionsAsync(User user)
    {
        using var admin = await AdminClientAsync();
        foreach (var id in await KeycloakUserIdsAsync(admin, user.Username))
            (await admin.PostAsync($"{KeycloakAdminUrl}/auth/admin/realms/openksef/users/{id}/logout", null)).EnsureSuccessStatusCode();
    }

    public static Task DeleteFakeNipTenantsAsync() => PsqlAsync($"""
        DELETE FROM "IssuedInvoices" WHERE "TenantId" IN (SELECT "Id" FROM "Tenants" WHERE "Nip" LIKE '{FakeNip.Prefix}%');
        DELETE FROM "Tenants" WHERE "Nip" LIKE '{FakeNip.Prefix}%';
        """);

    private static async Task<IEnumerable<string>> KeycloakUserIdsAsync(HttpClient admin, string username)
    {
        var found = await admin.GetFromJsonAsync<List<JsonElement>>(
            $"{KeycloakAdminUrl}/auth/admin/realms/openksef/users?exact=true&username={Uri.EscapeDataString(username)}");
        return found!.Select(e => e.GetProperty("id").GetString()!);
    }

    // Cached because a user with 2FA needs a fresh TOTP step per token, and the device polls run every second.
    private static async Task<HttpClient> AuthorizedClientAsync(User user)
    {
        if (!UserTokens.TryGetValue(user.Username, out var cached) || cached.Until <= DateTime.UtcNow)
        {
            var secret = AndroidOidcLoginFlow.StoredSecret(user.Username)
                ?? (user.Username == EnvironmentUser()?.Username ? Environment.GetEnvironmentVariable("E2E_TEST_TOTP_SECRET") : null);
            UserTokens[user.Username] = cached = await TokenAsync(ServerUrl, "openksef", "openksef-mobile", user.Username, user.Password,
                string.IsNullOrWhiteSpace(secret) ? null : Totp.NextCode(user.Username, secret));
        }
        return BearerClient(cached.Token);
    }

    private static async Task<HttpClient> AdminClientAsync()
    {
        var token = await TokenAsync(
            KeycloakAdminUrl,
            "master",
            "admin-cli",
            Environment.GetEnvironmentVariable("KEYCLOAK_ADMIN") ?? "admin",
            Environment.GetEnvironmentVariable("KEYCLOAK_ADMIN_PASSWORD") ?? "admin");
        return BearerClient(token.Token);
    }

    private static async Task<(string Token, DateTime Until)> TokenAsync(string baseUrl, string realm, string clientId, string username, string password, string? totp = null)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["username"] = username,
            ["password"] = password
        };
        if (totp is not null)
            form["totp"] = totp;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var response = await http.PostAsync($"{baseUrl}/auth/realms/{realm}/protocol/openid-connect/token", new FormUrlEncodedContent(form));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("access_token").GetString()!, DateTime.UtcNow.AddSeconds(body.GetProperty("expires_in").GetInt32() - 30));
    }

    private static HttpClient BearerClient(string token)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}
