using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

public class QrCodeTests
{
    private static InvoiceDto Invoice(string vendorName = "Vendor", decimal amount = 100m, string? invoiceNumber = "FV/001", string ksefNumber = "KSEF-001") =>
        new() { VendorName = vendorName, AmountGross = amount, InvoiceNumber = invoiceNumber, KSeFInvoiceNumber = ksefNumber };

    private static string[] Segments(InvoiceDto invoice) => TransferQrGenerator.BuildPayload(invoice).Split('|');

    [Fact]
    public void BuildPayload_ProducesExactZbpPayload()
    {
        Assert.Equal("|PL||123456|Lodz Sp. z o.o.|FV/2026/001|||", TransferQrGenerator.BuildPayload(Invoice("Łódź Sp. z o.o.", 1234.56m, "FV/2026/001")));
    }

    [Fact]
    public void BuildPayload_ProducesZbpFormat_WithEightSegments()
    {
        var segments = Segments(Invoice());

        Assert.Equal(9, segments.Length);
        Assert.Empty(segments[0]);
        Assert.Equal("PL", segments[1]);
    }

    [Theory]
    [InlineData("12.34", "1234")]
    [InlineData("500", "50000")]
    [InlineData("0.005", "1")]
    [InlineData("0", "0")]
    [InlineData("-10", "0")]
    public void BuildPayload_AmountInGrosze(string amount, string expected)
    {
        Assert.Equal(expected, Segments(Invoice(amount: decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)))[3]);
    }

    [Fact]
    public void BuildPayload_PolishDiacritics_Transliterated()
    {
        Assert.Equal("ZazolcGeslaJazn", Segments(Invoice("ZażółćGęśląJaźń"))[4]);
    }

    [Fact]
    public void BuildPayload_NameTruncatedTo20Chars()
    {
        Assert.Equal("ABCDEFGHIJ KLMNOPQRS", Segments(Invoice("ABCDEFGHIJ KLMNOPQRST Extra"))[4]);
    }

    [Fact]
    public void BuildPayload_TitleTruncatedTo32Chars()
    {
        Assert.Equal("FV/2026/000000000000000000000001", Segments(Invoice(invoiceNumber: "FV/2026/000000000000000000000001-EXTRA"))[5]);
    }

    [Fact]
    public void BuildPayload_TitleFallsBackToKsefNumber()
    {
        Assert.Equal("KSEF-001", Segments(Invoice(invoiceNumber: null))[5]);
    }

    [Fact]
    public void BuildPayload_AccountSegmentEmpty()
    {
        Assert.Empty(Segments(Invoice())[2]);
    }

    [Fact]
    public void BuildPayload_TrailingSegmentsEmpty()
    {
        var segments = Segments(Invoice());

        Assert.Empty(segments[6]);
        Assert.Empty(segments[7]);
        Assert.Empty(segments[8]);
    }

    [Fact]
    public void BuildPayload_SpecialCharsStripped()
    {
        Assert.Equal("Firma  Co. PL", Segments(Invoice("Firma & Co. (PL)"))[4]);
    }

    [Fact]
    public void Generate_ProducesPng()
    {
        var bytes = TransferQrGenerator.Generate(Invoice());

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
    }
}
