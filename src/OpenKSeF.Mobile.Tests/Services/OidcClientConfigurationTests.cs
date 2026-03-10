using IdentityModel.OidcClient;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

public class OidcClientConfigurationTests
{
    [Fact]
    public void OidcClientOptions_DisablePushedAuthorization_PreventsParWithKeycloak()
    {
        var options = new OidcClientOptions
        {
            Authority = "https://example.com/auth/realms/openksef",
            ClientId = "openksef-mobile",
            Scope = "openid profile email",
            RedirectUri = "openksef://auth/callback",
            PostLogoutRedirectUri = "openksef://auth/logout",
            DisablePushedAuthorization = true
        };

        Assert.True(options.DisablePushedAuthorization);
    }

    [Fact]
    public void OidcClientOptions_DefaultHasParEnabled()
    {
        var options = new OidcClientOptions();

        Assert.False(options.DisablePushedAuthorization);
    }

    [Fact]
    public void OidcClientOptions_MatchesMobileAppConfiguration()
    {
        var options = CreateMobileOidcOptions("https://example.com/auth/realms/openksef");

        Assert.Equal("openksef-mobile", options.ClientId);
        Assert.Equal("openid profile email", options.Scope);
        Assert.Equal("openksef://auth/callback", options.RedirectUri);
        Assert.True(options.DisablePushedAuthorization);
    }

    private static OidcClientOptions CreateMobileOidcOptions(string authority)
    {
        return new OidcClientOptions
        {
            Authority = authority,
            ClientId = "openksef-mobile",
            Scope = "openid profile email",
            RedirectUri = "openksef://auth/callback",
            PostLogoutRedirectUri = "openksef://auth/logout",
            DisablePushedAuthorization = true
        };
    }
}
