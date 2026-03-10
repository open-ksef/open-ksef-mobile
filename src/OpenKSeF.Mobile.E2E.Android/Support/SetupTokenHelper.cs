using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace OpenKSeF.Mobile.E2E.Android.Support;

/// <summary>
/// Generates a QR setup token by authenticating to the API as the test user.
/// Used by QR auto-login E2E tests.
/// </summary>
public static class SetupTokenHelper
{
    public static async Task<QrSetupResult> GenerateSetupTokenAsync(string serverBaseUrl)
    {
        var username = Environment.GetEnvironmentVariable("KEYCLOAK_USERNAME")
            ?? throw new InvalidOperationException("KEYCLOAK_USERNAME is required");
        var password = Environment.GetEnvironmentVariable("KEYCLOAK_PASSWORD")
            ?? throw new InvalidOperationException("KEYCLOAK_PASSWORD is required");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");

        var tokenEndpoint = $"{serverBaseUrl.TrimEnd('/')}/auth/realms/openksef/protocol/openid-connect/token";
        var ropcResponse = await http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "openksef-mobile",
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid profile email"
        }));
        ropcResponse.EnsureSuccessStatusCode();

        var tokenResult = await ropcResponse.Content.ReadFromJsonAsync<TokenResponse>();
        if (tokenResult?.AccessToken is null)
            throw new InvalidOperationException("ROPC login failed: no access token");

        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);

        var setupResponse = await http.PostAsync($"{serverBaseUrl.TrimEnd('/')}/api/account/setup-token", null);
        setupResponse.EnsureSuccessStatusCode();

        var setupResult = await setupResponse.Content.ReadFromJsonAsync<SetupTokenResponseDto>();
        if (string.IsNullOrWhiteSpace(setupResult?.SetupToken))
            throw new InvalidOperationException("Failed to generate setup token");

        var qrPayload = $$"""{"type":"openksef-setup","version":1,"serverUrl":"{{serverBaseUrl.TrimEnd('/')}}","setupToken":"{{setupResult.SetupToken}}"}""";

        return new QrSetupResult(setupResult.SetupToken, qrPayload, serverBaseUrl);
    }

    public sealed record QrSetupResult(string SetupToken, string QrPayloadJson, string ServerUrl);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record SetupTokenResponseDto(
        [property: JsonPropertyName("setupToken")] string? SetupToken,
        [property: JsonPropertyName("expiresInSeconds")] int ExpiresInSeconds);
}
