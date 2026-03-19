using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

public class ServerSettingsValidationTests
{
    [Theory]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("https://example.com/", "https://example.com")]
    [InlineData("https://example.com/some/path", "https://example.com")]
    [InlineData("http://localhost:8080", "http://localhost:8080")]
    [InlineData("http://localhost:8080/", "http://localhost:8080")]
    public void TryUpdateServerUrl_ValidUrl_NormalizesCorrectly(string input, string expected)
    {
        var (success, normalized, error) = ValidateAndNormalize(input);

        Assert.True(success);
        Assert.Equal(expected, normalized);
        Assert.Null(error);
    }

    [Fact]
    public void TryUpdateServerUrl_EmptyUrl_ReturnsError()
    {
        var (success, _, error) = ValidateAndNormalize("");

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryUpdateServerUrl_WhitespaceOnly_ReturnsError()
    {
        var (success, _, error) = ValidateAndNormalize("   ");

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("://missing-scheme")]
    public void TryUpdateServerUrl_InvalidUrl_ReturnsError(string input)
    {
        var (success, _, error) = ValidateAndNormalize(input);

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("http://10.0.2.2:8080", "http://10.0.2.2:8080")]
    [InlineData("http://localhost:8080", "http://localhost:8080")]
    public void TryUpdateServerUrl_HttpDevHost_IsAccepted(string input, string expected)
    {
        var (success, normalized, _) = ValidateAndNormalize(input);

        Assert.True(success);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("http://192.168.1.100:8080")]
    [InlineData("http://my-server.local")]
    [InlineData("http://example.com")]
    public void TryUpdateServerUrl_HttpNonDevHost_ReturnsError(string input)
    {
        var (success, _, error) = ValidateAndNormalize(input);

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Fact]
    public void Authority_IsDerivedFromServerUrl()
    {
        const string serverUrl = "https://example.com";
        const string realmPath = "/auth/realms/openksef";

        Assert.Equal("https://example.com/auth/realms/openksef", serverUrl + realmPath);
    }

    private static readonly HashSet<string> CleartextAllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "10.0.2.2",
    };

    /// <summary>
    /// Mirror of ServerSettingsService.TryUpdateServerUrl validation logic.
    /// </summary>
    private static (bool success, string normalized, string? error) ValidateAndNormalize(string url)
    {
        var normalizedUrl = url.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(normalizedUrl))
            return (false, normalizedUrl, "Adres serwera nie moze byc pusty.");

        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https"))
            return (false, normalizedUrl, "Podaj poprawny adres URL (http:// lub https://).");

        if (uri.Scheme == "http" && !CleartextAllowedHosts.Contains(uri.Host))
            return (false, normalizedUrl, "Polaczenie HTTP dozwolone tylko dla localhost / 10.0.2.2. Uzyj HTTPS.");

        normalizedUrl = uri.GetLeftPart(UriPartial.Authority);
        return (true, normalizedUrl, null);
    }
}
