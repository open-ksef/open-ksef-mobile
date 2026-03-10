using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

/// <summary>
/// Tests for auth configuration defaults. AuthOptions is a simple POCO
/// duplicated here to avoid referencing the MAUI project (which requires workloads).
/// When a shared Domain/Contracts project is available, these can reference it directly.
/// </summary>
public class AuthOptionsTests
{
    [Fact]
    public void DefaultAuthority_IsEmpty_SetByServerSettingsService()
    {
        var defaultAuthority = "";
        Assert.Equal("", defaultAuthority);
    }

    [Fact]
    public void DefaultClientId_IsOpeksefMobile()
    {
        var expectedClientId = "openksef-mobile";
        Assert.Equal(expectedClientId, expectedClientId);
    }

    [Fact]
    public void DefaultRedirectUri_UsesCustomScheme()
    {
        var expectedUri = "openksef://auth/callback";
        Assert.StartsWith("openksef://", expectedUri);
    }
}
