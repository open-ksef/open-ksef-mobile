using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

/// <summary>
/// Tests for Polish NIP (tax identification number) validation.
/// NIP is a 10-digit number with a checksum in the last digit.
/// Weights: 6, 5, 7, 2, 3, 4, 5, 6, 7
/// </summary>
public class NipValidationTests
{
    // Duplicated from TenantFormViewModel for testing without MAUI reference
    private static bool IsValidNipChecksum(string nip)
    {
        if (nip.Length != 10 || !nip.All(char.IsDigit))
            return false;

        int[] weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
        var sum = 0;
        for (var i = 0; i < 9; i++)
            sum += (nip[i] - '0') * weights[i];

        return sum % 11 == nip[9] - '0';
    }

    [Theory]
    [InlineData("5261040828")] // Polish Ministry of Finance
    [InlineData("1234563218")] // Valid test NIP
    public void ValidNip_ReturnsTrue(string nip)
    {
        Assert.True(IsValidNipChecksum(nip));
    }

    [Theory]
    [InlineData("1234567890")] // Invalid checksum
    [InlineData("5261040821")] // Wrong check digit
    [InlineData("5261040829")] // Off by one from valid NIP
    public void InvalidChecksum_ReturnsFalse(string nip)
    {
        Assert.False(IsValidNipChecksum(nip));
    }

    [Theory]
    [InlineData("123456789")]   // Too short
    [InlineData("12345678901")] // Too long
    [InlineData("")]            // Empty
    [InlineData("abcdefghij")]  // Non-numeric
    [InlineData("123-456-78-90")] // With dashes
    public void InvalidFormat_ReturnsFalse(string nip)
    {
        Assert.False(IsValidNipChecksum(nip));
    }
}
