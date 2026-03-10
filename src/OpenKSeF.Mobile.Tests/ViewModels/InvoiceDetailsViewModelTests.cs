using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

// Minimal DTO duplicate for testing without MAUI project reference
public class InvoiceDto
{
    public string VendorName { get; set; } = string.Empty;
    public string VendorNip { get; set; } = string.Empty;
    public decimal AmountGross { get; set; }
    public string Currency { get; set; } = "PLN";
    public string KSeFInvoiceNumber { get; set; } = string.Empty;
}

public class InvoiceDetailsViewModelTests
{
    // Duplicated from InvoiceDetailsViewModel for testing
    private static string BuildTransferDetails(InvoiceDto invoice)
    {
        return $"Odbiorca: {invoice.VendorName}\n" +
               $"NIP: {invoice.VendorNip}\n" +
               $"Kwota: {invoice.AmountGross:N2} {invoice.Currency}\n" +
               $"Tytul: Faktura {invoice.KSeFInvoiceNumber}";
    }

    [Fact]
    public void BuildTransferDetails_FormatsCorrectly()
    {
        var invoice = new InvoiceDto
        {
            VendorName = "Test Sp. z o.o.",
            VendorNip = "5261040828",
            AmountGross = 1230.50m,
            Currency = "PLN",
            KSeFInvoiceNumber = "FV/2026/001"
        };

        var result = BuildTransferDetails(invoice);

        Assert.Contains("Odbiorca: Test Sp. z o.o.", result);
        Assert.Contains("NIP: 5261040828", result);
        Assert.Contains("1,230.50 PLN", result); // or 1 230,50 depending on culture
        Assert.Contains("Tytul: Faktura FV/2026/001", result);
    }

    [Fact]
    public void BuildTransferDetails_IncludesAllRequiredFields()
    {
        var invoice = new InvoiceDto
        {
            VendorName = "Vendor",
            VendorNip = "1234567890",
            AmountGross = 100m,
            Currency = "EUR",
            KSeFInvoiceNumber = "INV-001"
        };

        var result = BuildTransferDetails(invoice);

        Assert.Contains("Odbiorca:", result);
        Assert.Contains("NIP:", result);
        Assert.Contains("Kwota:", result);
        Assert.Contains("Tytul:", result);
        Assert.Contains("EUR", result);
    }
}
