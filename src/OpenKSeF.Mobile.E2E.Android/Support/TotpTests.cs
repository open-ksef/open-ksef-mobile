namespace OpenKSeF.Mobile.E2E.Android.Support;

[TestFixture]
[Category("DeviceFree")]
public class TotpTests
{
    [TestCase(59L, "287082")]
    [TestCase(1111111109L, "081804")]
    [TestCase(20000000000L, "353130")]
    public void Code_MatchesRfc6238Sha1Vectors(long unixTime, string expected) =>
        Assert.That(Totp.Code("GEZDGNBV GY3TQOJQ GEZDGNBV GY3TQOJQ", unixTime / 30), Is.EqualTo(expected));
}
