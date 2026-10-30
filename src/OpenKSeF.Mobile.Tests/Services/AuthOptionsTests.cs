using OpenKSeF.Mobile.Services;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

public class AuthOptionsTests
{
    [Fact]
    public void Defaults_MatchKeycloakMobileClient()
    {
        var options = new AuthOptions();

        Assert.Equal("", options.Authority);
        Assert.Equal("openksef-mobile", options.ClientId);
        Assert.Equal("openksef://auth/callback", options.RedirectUri);
        Assert.Equal("openksef://auth/logout", options.PostLogoutRedirectUri);
    }
}
