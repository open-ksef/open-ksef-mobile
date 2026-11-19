using System.Text.RegularExpressions;
using OpenKSeF.Mobile.E2E.Android.Support;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.E2E;

public class FakeNipTests
{
    [Fact]
    public void Next_ReturnsDistinctChecksumValidNipsUnderFakePrefix()
    {
        var nips = Enumerable.Range(0, 1000).Select(_ => FakeNip.Next()).ToList();

        Assert.All(nips, nip =>
        {
            Assert.Matches(new Regex(@"^000\d{7}$"), nip);
            Assert.True(TenantFormViewModel.IsValidNipChecksum(nip), nip);
        });
        Assert.True(nips.Distinct().Count() > 990);
    }
}
