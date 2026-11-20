namespace OpenKSeF.Mobile.E2E.Android.Support;

/// <summary>
/// One-off removal of leftover E2E tenants (fake NIP prefix 000) from the local dev database. Run only when no E2E run is active.
/// </summary>
[Explicit("Deletes dev test data; run by hand: --filter Name=DeleteLeftoverFakeNipTenants")]
public sealed class DevDataCleanup
{
    [Test]
    public Task DeleteLeftoverFakeNipTenants() => E2EBackend.DeleteFakeNipTenantsAsync();
}
