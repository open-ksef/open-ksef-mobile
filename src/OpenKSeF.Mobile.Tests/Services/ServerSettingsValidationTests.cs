using OpenKSeF.Mobile.Services;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

public class ServerSettingsValidationTests
{
    [Theory]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("https://example.com/", "https://example.com")]
    [InlineData("https://example.com/some/path", "https://example.com")]
    [InlineData(" https://example.com:8443/ ", "https://example.com:8443")]
    [InlineData("http://localhost:8080", "http://localhost:8080")]
    [InlineData("http://localhost:8080/", "http://localhost:8080")]
    public void TryNormalize_ValidUrl_NormalizesCorrectly(string input, string expected)
    {
        var success = ServerUrlRules.TryNormalize(input, allowDevCleartext: true, out var normalized, out var error);

        Assert.True(success);
        Assert.Equal(expected, normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("://missing-scheme")]
    public void TryNormalize_InvalidUrl_ReturnsError(string input)
    {
        var success = ServerUrlRules.TryNormalize(input, allowDevCleartext: true, out _, out var error);

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("http://10.0.2.2:8080", "http://10.0.2.2:8080")]
    [InlineData("http://localhost:8080", "http://localhost:8080")]
    public void TryNormalize_HttpDevHost_AcceptedInDebug(string input, string expected)
    {
        var success = ServerUrlRules.TryNormalize(input, allowDevCleartext: true, out var normalized, out _);

        Assert.True(success);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("http://10.0.2.2:8080")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://example.com")]
    public void TryNormalize_Http_RejectedInRelease(string input)
    {
        var success = ServerUrlRules.TryNormalize(input, allowDevCleartext: false, out _, out var error);

        Assert.False(success);
        Assert.Contains("HTTPS", error);
    }

    [Theory]
    [InlineData("http://192.168.1.100:8080")]
    [InlineData("http://my-server.local")]
    [InlineData("http://example.com")]
    public void TryNormalize_HttpNonDevHost_RejectedInDebug(string input)
    {
        var success = ServerUrlRules.TryNormalize(input, allowDevCleartext: true, out _, out var error);

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Fact]
    public void AllowDevCleartext_FollowsBuildConfiguration()
    {
#if DEBUG
        Assert.True(ServerUrlRules.AllowDevCleartext);
#else
        Assert.False(ServerUrlRules.AllowDevCleartext);
        Assert.False(ServerUrlRules.TryNormalize("http://10.0.2.2:8080", ServerUrlRules.AllowDevCleartext, out _, out var error));
        Assert.Equal("Wymagane jest polaczenie HTTPS.", error);
#endif
    }

    [Fact]
    public void TryNormalize_Https_AcceptedInRelease()
    {
        Assert.True(ServerUrlRules.TryNormalize("https://demo.open-ksef.pl", allowDevCleartext: false, out _, out _));
    }

    [Theory]
    [InlineData("https://a.example.com", "https://b.example.com", true)]
    [InlineData("https://example.com", "https://example.com:8443", true)]
    [InlineData("http://10.0.2.2:8080", "https://10.0.2.2:8080", true)]
    [InlineData("https://example.com", "https://EXAMPLE.com", false)]
    [InlineData("https://example.com/", "https://example.com", false)]
    public void IsDifferentServer_ComparesSchemeHostPort(string current, string scanned, bool expected)
    {
        Assert.Equal(expected, ServerUrlRules.IsDifferentServer(current, scanned));
    }

    [Theory]
    [InlineData("https://demo.open-ksef.pl", "demo.open-ksef.pl")]
    [InlineData("http://10.0.2.2:8080", "10.0.2.2:8080")]
    [InlineData("https://bаnk.pl", "xn--bnk-6cd.pl")]
    public void DisplayHost_ShowsPunycodeHostAndNonDefaultPort(string url, string expected)
    {
        Assert.Equal(expected, ServerUrlRules.DisplayHost(url));
    }
}
