using System.Text.Json;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

/// <summary>Runs the backend's shared totals vectors (open-ksef src/OpenKSeF.Domain.Tests/Invoicing/TotalsVectors.json, copied verbatim).</summary>
public class IssuedInvoiceTotalsTests
{
    private sealed record Expected(List<decimal> LineNet, List<VatSummaryDto> VatSummary, decimal TotalNet, decimal TotalVat, decimal TotalGross);

    private sealed record Vector(string Name, List<IssuedInvoiceLineRequest> Lines, Expected Expected);

    private static readonly List<Vector> Vectors = JsonSerializer.Deserialize<List<Vector>>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Services", "Vectors", "TotalsVectors.json")),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    public static TheoryData<string> VectorNames => [.. Vectors.Select(v => v.Name)];

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void Compute_MatchesServerVector(string name)
    {
        var vector = Vectors.Single(v => v.Name == name);

        var totals = IssuedInvoiceTotals.Compute(vector.Lines);

        Assert.Equal(vector.Expected.LineNet, vector.Lines.Select(IssuedInvoiceTotals.LineNet));
        Assert.Equal(
            vector.Expected.VatSummary.Select(s => (s.VatRate, s.Net, s.Vat)),
            totals.VatSummary.Select(s => (s.VatRate, s.Net, s.Vat)));
        Assert.Equal(
            (vector.Expected.TotalNet, vector.Expected.TotalVat, vector.Expected.TotalGross),
            (totals.TotalNet, totals.TotalVat, totals.TotalGross));
    }

    [Fact]
    public void Vectors_AreLoaded() => Assert.Equal(9, Vectors.Count);
}
