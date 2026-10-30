using System.Globalization;
using NSubstitute;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class InvoiceDetailsViewModelTests
{
    private static InvoiceDto Invoice(decimal amount = 1230.50m, string currency = "PLN", string? invoiceNumber = null, string vendorNip = "5261040828") =>
        new()
        {
            VendorName = "Test Sp. z o.o.",
            VendorNip = vendorNip,
            AmountGross = amount,
            Currency = currency,
            InvoiceNumber = invoiceNumber,
            KSeFInvoiceNumber = "KSEF-001"
        };

    [Theory]
    [InlineData("pl-PL")]
    [InlineData("en-US")]
    [InlineData("")]
    public void BuildTransferDetails_FormatsAmountInPolish_RegardlessOfDeviceCulture(string deviceCulture)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(deviceCulture);
        try
        {
            var result = InvoiceDetailsViewModel.BuildTransferDetails(Invoice(1234567.89m, invoiceNumber: "FV/2026/001"));

            Assert.Equal(
                "Odbiorca: Test Sp. z o.o.\nNIP: 5261040828\nKwota: 1 234 567,89 PLN\nTytul: FV/2026/001",
                result);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("500", "500,00")]
    [InlineData("99.99", "99,99")]
    [InlineData("1230.5", "1 230,50")]
    public void BuildTransferDetails_AmountHasTwoDecimals(string amount, string expected)
    {
        var result = InvoiceDetailsViewModel.BuildTransferDetails(Invoice(decimal.Parse(amount, CultureInfo.InvariantCulture), "EUR"));

        Assert.Contains($"Kwota: {expected} EUR", result);
    }

    [Fact]
    public void BuildTransferDetails_PrefersSellerInvoiceNumber()
    {
        Assert.EndsWith("Tytul: 15445/04/2026", InvoiceDetailsViewModel.BuildTransferDetails(Invoice(invoiceNumber: "15445/04/2026")));
    }

    [Fact]
    public void BuildTransferDetails_UsesKsefNumberWhenSellerInvoiceNumberMissing()
    {
        Assert.EndsWith("Tytul: KSEF-001", InvoiceDetailsViewModel.BuildTransferDetails(Invoice()));
    }

    [Fact]
    public async Task CopyTransferDetails_PutsTransferTextOnClipboard()
    {
        Shell.Current.Reset();
        var vm = new InvoiceDetailsViewModel(Substitute.For<IApiService>()) { Invoice = Invoice() };

        await vm.CopyTransferDetailsCommand.ExecuteAsync(null);

        Assert.Equal(InvoiceDetailsViewModel.BuildTransferDetails(vm.Invoice!), Clipboard.Default.Text);
        Assert.Equal("Dane przelewu skopiowane do schowka.", Shell.Current.LastAlertMessage);
        Assert.Null(vm.ErrorMessage);
    }
}
