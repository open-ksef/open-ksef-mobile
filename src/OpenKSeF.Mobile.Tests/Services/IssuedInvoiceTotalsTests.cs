using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

public class IssuedInvoiceTotalsTests
{
    private static IssuedInvoiceLineRequest Line(decimal quantity, decimal price, string rate) =>
        new() { Name = "Usługa", Quantity = quantity, UnitNetPrice = price, VatRate = rate };

    [Fact]
    public void ThreeLinesOf033At23_RoundsVatOnSummedNet()
    {
        var totals = IssuedInvoiceTotals.Compute([Line(1, 0.33m, "23"), Line(1, 0.33m, "23"), Line(1, 0.33m, "23")]);

        Assert.Equal(0.99m, totals.TotalNet);
        Assert.Equal(0.23m, totals.TotalVat);
        Assert.Equal(1.22m, totals.TotalGross);
        var summary = Assert.Single(totals.VatSummary);
        Assert.Equal(("23", 0.99m, 0.23m), (summary.VatRate, summary.Net, summary.Vat));
    }

    [Fact]
    public void TwoRates_SummarisedPerRate()
    {
        var totals = IssuedInvoiceTotals.Compute([Line(1, 100m, "23"), Line(1, 100m, "8")]);

        Assert.Equal(231.00m, totals.TotalGross);
        Assert.Equal([("23", 23m), ("8", 8m)], totals.VatSummary.Select(s => (s.VatRate, s.Vat)));
    }

    [Theory]
    [InlineData("0 KR")]
    [InlineData("zw")]
    public void NonNumericRate_HasZeroVat(string rate)
    {
        var totals = IssuedInvoiceTotals.Compute([Line(1, 50m, rate)]);

        Assert.Equal(0m, totals.TotalVat);
        Assert.Equal(50m, totals.TotalGross);
    }

    [Fact]
    public void LineNet_RoundsHalfAwayFromZero()
    {
        Assert.Equal(0.13m, IssuedInvoiceTotals.LineNet(Line(0.5m, 0.25m, "23")));
        Assert.Equal(0.03m, IssuedInvoiceTotals.Compute([Line(1, 0.11m, "23")]).TotalVat);
    }
}
