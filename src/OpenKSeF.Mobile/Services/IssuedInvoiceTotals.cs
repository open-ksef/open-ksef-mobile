using System.Globalization;
using OpenKSeF.Mobile.Models;

namespace OpenKSeF.Mobile.Services;

/// <summary>Client-side totals preview; must match the server rule (line net rounded, VAT rounded per rate on summed net).</summary>
public sealed record IssuedInvoiceTotals(List<VatSummaryDto> VatSummary, decimal TotalNet, decimal TotalVat, decimal TotalGross)
{
    public static readonly IReadOnlyList<string> VatRates = ["23", "8", "5", "0 KR", "zw"];

    public static decimal LineNet(IssuedInvoiceLineRequest line) =>
        Math.Round(line.Quantity * line.UnitNetPrice, 2, MidpointRounding.AwayFromZero);

    public static IssuedInvoiceTotals Compute(IEnumerable<IssuedInvoiceLineRequest> lines)
    {
        var summary = lines
            .GroupBy(l => l.VatRate)
            .OrderBy(g => VatRates.Contains(g.Key) ? VatRates.ToList().IndexOf(g.Key) : VatRates.Count)
            .Select(g =>
            {
                var net = g.Sum(LineNet);
                return new VatSummaryDto { VatRate = g.Key, Net = net, Vat = Math.Round(net * RateOf(g.Key), 2, MidpointRounding.AwayFromZero) };
            })
            .ToList();
        var totalNet = summary.Sum(s => s.Net);
        var totalVat = summary.Sum(s => s.Vat);
        return new IssuedInvoiceTotals(summary, totalNet, totalVat, totalNet + totalVat);
    }

    private static decimal RateOf(string vatRate) =>
        decimal.TryParse(vatRate, NumberStyles.None, CultureInfo.InvariantCulture, out var percent) ? percent / 100 : 0;
}
