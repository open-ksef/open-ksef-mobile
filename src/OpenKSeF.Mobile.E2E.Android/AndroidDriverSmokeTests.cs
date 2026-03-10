using OpenKSeF.Mobile.E2E.Android.Infrastructure;

namespace OpenKSeF.Mobile.E2E.Android;

[Category("Smoke")]
public sealed class AndroidDriverSmokeTests : AndroidTestBase
{
    [Test]
    public void DriverSession_CanBeInitialized()
    {
        Assert.That(Driver, Is.Not.Null);
        Assert.That(AndroidDriver, Is.Not.Null);
    }
}
