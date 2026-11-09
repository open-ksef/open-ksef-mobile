using System.Net;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class InvoiceDetailsViewModelTests
{
    private const string TransferText = "Odbiorca: Inny niz lokalnie\nNIP: 1111111111\nNr rachunku: 00 0000 0000 0000 0000 0000 0000\nKwota: 1 476,00 EUR\nTytul: FV 1/2026";
    private static readonly Guid TenantId = Guid.Parse("304f3ff7-06b6-43e8-bdf4-a0ad77f194e3");
    private static readonly Guid InvoiceId = Guid.Parse("0b7c9a62-3f6e-4d1a-9c55-1f2e3d4c5b6a");

    private readonly IApiService _api = Substitute.For<IApiService>();

    private InvoiceDetailsViewModel Loaded() => new(_api)
    {
        Invoice = new InvoiceDto { Id = InvoiceId, VendorName = "Test Sp. z o.o.", VendorNip = "1111111111", AmountGross = 1230.50m, KSeFInvoiceNumber = "KSEF-001" },
        TenantId = TenantId.ToString()
    };

    [Fact]
    public async Task CopyTransferDetails_PutsServerTextOnClipboardByteForByte()
    {
        Shell.Current.Reset();
        _api.GetTransferDetailsAsync(TenantId, InvoiceId).Returns(new TransferDetailsDto { TransferText = TransferText });
        var vm = Loaded();

        await vm.CopyTransferDetailsCommand.ExecuteAsync(null);

        Assert.Equal(TransferText, Clipboard.Default.Text);
        Assert.Equal("Dane przelewu skopiowane do schowka.", Shell.Current.LastAlertMessage);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task CopyTransferDetails_Offline_LeavesClipboardAndShowsMessage()
    {
        Shell.Current.Reset();
        await Clipboard.Default.SetTextAsync("before");
        _api.GetTransferDetailsAsync(TenantId, InvoiceId)
            .ThrowsAsync(new ApiException(HttpStatusCode.ServiceUnavailable, "Brak połączenia z serwerem."));
        var vm = Loaded();

        await vm.CopyTransferDetailsCommand.ExecuteAsync(null);

        Assert.Equal("before", Clipboard.Default.Text);
        Assert.Equal("Brak połączenia z serwerem.", Shell.Current.LastAlertMessage);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task CopyTransferDetails_NonJsonBody_ShowsFixedTextNotParserError()
    {
        Shell.Current.Reset();
        await Clipboard.Default.SetTextAsync("before");
        _api.GetTransferDetailsAsync(TenantId, InvoiceId)
            .ThrowsAsync(new System.Text.Json.JsonException("'<' is an invalid start of a value."));
        var vm = Loaded();

        await vm.CopyTransferDetailsCommand.ExecuteAsync(null);

        Assert.Equal("before", Clipboard.Default.Text);
        Assert.Equal("Nie udało się skopiować danych przelewu.", Shell.Current.LastAlertMessage);
    }

    [Fact]
    public async Task CopyTransferDetails_RetryAfterOffline_CopiesServerText()
    {
        Shell.Current.Reset();
        _api.GetTransferDetailsAsync(TenantId, InvoiceId)
            .ThrowsAsync(new ApiException(HttpStatusCode.ServiceUnavailable, "offline"));
        var vm = Loaded();
        await vm.CopyTransferDetailsCommand.ExecuteAsync(null);

        _api.GetTransferDetailsAsync(TenantId, InvoiceId).Returns(new TransferDetailsDto { TransferText = TransferText });
        await vm.CopyTransferDetailsCommand.ExecuteAsync(null);

        Assert.Equal("Dane przelewu skopiowane do schowka.", Shell.Current.LastAlertMessage);
        Assert.Equal(TransferText, Clipboard.Default.Text);
    }
}
