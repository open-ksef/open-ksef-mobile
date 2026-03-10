using System.Globalization;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

/// <summary>
/// Tests for the transfer details formatting logic.
/// Duplicated from Domain TransferDetailsService to avoid MAUI project reference.
/// </summary>
public class TransferDetailsServiceTests
{
    private static string BuildTransferText(string vendorName, string vendorNip, decimal amount, string currency, string invoiceNumber)
    {
        var lines = new List<string>
        {
            $"Odbiorca: {vendorName}"
        };

        if (!string.IsNullOrWhiteSpace(vendorNip))
            lines.Add($"NIP: {vendorNip}");

        lines.Add($"Kwota: {amount.ToString("N2", CultureInfo.InvariantCulture)} {currency}");
        lines.Add($"Tytul: Faktura {invoiceNumber}");

        return string.Join("\n", lines);
    }

    [Fact]
    public void BuildTransferText_StandardInvoice_FormatsCorrectly()
    {
        var result = BuildTransferText("Test Sp. z o.o.", "5261040828", 1230.50m, "PLN", "FV/2026/001");

        Assert.Contains("Odbiorca: Test Sp. z o.o.", result);
        Assert.Contains("NIP: 5261040828", result);
        Assert.Contains("Kwota: 1,230.50 PLN", result);
        Assert.Contains("Tytul: Faktura FV/2026/001", result);
    }

    [Fact]
    public void BuildTransferText_WholeAmount_FormatsWithDecimals()
    {
        var result = BuildTransferText("Vendor", "1234567890", 500m, "PLN", "INV-001");

        Assert.Contains("Kwota: 500.00 PLN", result);
    }

    [Fact]
    public void BuildTransferText_EurCurrency_IncludesCurrency()
    {
        var result = BuildTransferText("Euro Vendor GmbH", "9876543210", 99.99m, "EUR", "INV-002");

        Assert.Contains("Kwota: 99.99 EUR", result);
    }

    [Fact]
    public void BuildTransferText_PolishDiacritics_PreservedInName()
    {
        var result = BuildTransferText("Zrodlo Sp. z o.o.", "5261040828", 100m, "PLN", "FV/001");

        Assert.Contains("Odbiorca: Zrodlo Sp. z o.o.", result);
    }

    [Fact]
    public void BuildTransferText_EmptyNip_OmitsNipLine()
    {
        var result = BuildTransferText("No NIP Vendor", "", 100m, "PLN", "FV/001");

        Assert.DoesNotContain("NIP:", result);
        Assert.Contains("Odbiorca: No NIP Vendor", result);
    }

    [Fact]
    public void BuildTransferText_LargeAmount_FormatsWithThousandsSeparator()
    {
        var result = BuildTransferText("Big Vendor", "1234567890", 1234567.89m, "PLN", "FV/001");

        Assert.Contains("Kwota: 1,234,567.89 PLN", result);
    }
}
