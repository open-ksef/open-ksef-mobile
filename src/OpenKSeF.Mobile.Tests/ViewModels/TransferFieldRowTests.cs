using System.Globalization;
using System.Net;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class TransferFieldRowTests
{
    private const string Nrb = "PL93 0000 0000 2026 1117 0000 0815";
    private static readonly Guid TenantId = Guid.Parse("304f3ff7-06b6-43e8-bdf4-a0ad77f194e3");
    private static readonly Guid InvoiceId = Guid.Parse("0b7c9a62-3f6e-4d1a-9c55-1f2e3d4c5b6a");

    private readonly IApiService _api = Substitute.For<IApiService>();

    public TransferFieldRowTests()
    {
        Shell.Current.Reset();
        CopyFeedback.Shown.Clear();
        Clipboard.Default.Failure = null;
    }

    private static TransferDetailsDto Transfer(string? account = Nrb, decimal amount = 12345.60m, string currency = "PLN") => new()
    {
        RecipientName = "  Faktor Testowy S.A. ",
        RecipientAccount = account,
        RecipientNip = "111-111-11-11",
        Amount = amount,
        Currency = currency,
        Title = "FAKTURA/SPRZEDAZ/2026/03/ODDZIAL-WARSZAWA/000123",
        TransferText = "Odbiorca: Faktor Testowy S.A.\nKwota: 12 345,60 PLN"
    };

    private static TransferFieldRow Row(TransferDetailsDto transfer, string caption) =>
        TransferFieldRow.From(transfer, "QrCodePage").Single(r => r.Caption == caption);

    [Theory]
    [InlineData(Nrb, "93000000002026111700000815")]
    [InlineData("93 0000 0000 2026 1117 0000 0815", "93000000002026111700000815")]
    [InlineData("pl93-0000-0000-2026-1117-0000-0815", "93000000002026111700000815")]
    [InlineData("DE57 0000 0000 0532 0130 00", "DE57000000000532013000")]
    [InlineData("PL94 0000 0000 2026 1117 0000 0815", "PL94000000002026111700000815")]
    public void AccountClipboard_NrbAsDigitsOtherwiseCompactIban(string account, string expected) =>
        Assert.Equal(expected, TransferFieldRow.AccountClipboard(account));

    [Theory]
    [InlineData(Nrb, "93 0000 0000 2026 1117 0000 0815")]
    [InlineData("93000000002026111700000815", "93 0000 0000 2026 1117 0000 0815")]
    [InlineData("de57000000000532013000", "DE57 0000 0000 0532 0130 00")]
    public void AccountDisplay_Grouped(string account, string expected) =>
        Assert.Equal(expected, TransferFieldRow.AccountDisplay(account));

    [Theory]
    [InlineData("pl-PL", 12345.60, "12345,60")]
    [InlineData("en-US", 12345.60, "12345,60")]
    [InlineData("pl-PL", 0.5, "0,50")]
    [InlineData("en-US", 1234567.89, "1234567,89")]
    [InlineData("de-DE", 0, "0,00")]
    public void AmountClipboard_DecimalCommaNoGroupsUnderAnyCulture(string culture, double amount, string expected)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var text = TransferFieldRow.AmountClipboard((decimal)amount);

            Assert.Equal(expected, text);
            Assert.Equal(System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(text));
            Assert.DoesNotContain(' ', text);
            Assert.DoesNotContain(' ', text);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData("111-111-11-11", "1111111111")]
    [InlineData("PL 1111111111", "1111111111")]
    [InlineData("1111111111", "1111111111")]
    public void NipClipboard_DigitsOnly(string nip, string expected) =>
        Assert.Equal(expected, TransferFieldRow.NipClipboard(nip));

    [Fact]
    public void From_AllFieldsFromTransferInOrder()
    {
        var rows = TransferFieldRow.From(Transfer(), "QrCodePage");

        Assert.Equal(["Odbiorca", "Nr rachunku", "Kwota", "Tytuł", "NIP odbiorcy"], rows.Select(r => r.Caption));
        Assert.Equal(
            ["Faktor Testowy S.A.", "93000000002026111700000815", "12345,60", "FAKTURA/SPRZEDAZ/2026/03/ODDZIAL-WARSZAWA/000123", "1111111111"],
            rows.Select(r => r.ClipboardText));
        Assert.Equal(
            ["QrCodePageRowCopyRecipient", "QrCodePageRowCopyAccount", "QrCodePageRowCopyAmount", "QrCodePageRowCopyTitle", "QrCodePageRowCopyNip"],
            rows.Select(r => r.AutomationId));
        Assert.Equal(IssuingFormat.Money(12345.60m, "PLN"), Row(Transfer(), "Kwota").Value);
        Assert.Equal(Transfer().Title, Row(Transfer(), "Tytuł").ClipboardText);
    }

    [Fact]
    public void From_ForeignIbanEur_CopiesNumberOnly()
    {
        var transfer = Transfer("DE57 0000 0000 0532 0130 00", 0.50m, "EUR");

        Assert.Equal("DE57000000000532013000", Row(transfer, "Nr rachunku").ClipboardText);
        Assert.Equal("0,50", Row(transfer, "Kwota").ClipboardText);
        Assert.EndsWith(" EUR", Row(transfer, "Kwota").Value);
    }

    [Fact]
    public void From_BlankFields_HidesThoseRowsAmountStays()
    {
        var transfer = new TransferDetailsDto { RecipientName = " ", RecipientAccount = null, RecipientNip = "", Title = "", Amount = 0m, Currency = "PLN" };

        var rows = TransferFieldRow.From(transfer, "QrCodePage");

        Assert.Equal("Kwota", Assert.Single(rows).Caption);
        Assert.Equal("0,00", rows[0].ClipboardText);
    }

    [Fact]
    public void Row_ReadsAsLabelValueWithCopyHint()
    {
        var account = Row(Transfer(), "Nr rachunku");

        Assert.Equal("Nr rachunku: 93 0000 0000 2026 1117 0000 0815", account.Description);
        Assert.Equal("Dotknij dwukrotnie, aby skopiować", account.Hint);
    }

    [Fact]
    public async Task Copy_PutsClipboardTextShowsFeedbackAndCheckFor1500Ms()
    {
        var account = Row(Transfer(), "Nr rachunku");

        var copy = account.CopyCommand.ExecuteAsync(null);

        Assert.Equal("93000000002026111700000815", Clipboard.Default.Text);
        Assert.Equal(("Skopiowano numer rachunku", false), CopyFeedback.Shown.Single());
        Assert.True(account.IsCopied);
        Assert.False(account.ShowCopyIcon);
        Assert.Empty(Shell.Current.Alerts);

        await copy;

        Assert.False(account.IsCopied);
        Assert.True(account.ShowCopyIcon);
    }

    [Fact]
    public void LongPress_CopiesSameTextWithLongPressHaptic()
    {
        var amount = Row(Transfer(), "Kwota");

        _ = amount.LongPressCommand.ExecuteAsync(null);

        Assert.Equal("12345,60", Clipboard.Default.Text);
        Assert.Equal(("Skopiowano kwotę", true), CopyFeedback.Shown.Single());
    }

    [Fact]
    public async Task Copy_ClipboardFails_ShowsAlertNoFeedback()
    {
        var nip = Row(Transfer(), "NIP odbiorcy");
        Clipboard.Default.Failure = new InvalidOperationException("raw");

        await nip.CopyCommand.ExecuteAsync(null);
        Clipboard.Default.Failure = null;

        Assert.Equal("Nie udało się skopiować.", Shell.Current.LastAlertMessage);
        Assert.Empty(CopyFeedback.Shown);
        Assert.False(nip.IsCopied);
    }

    [Fact]
    public void InvoiceDetails_LoadsCardFromTransferNotInvoiceHeader_DueShownNotCopyable()
    {
        _api.GetInvoiceDetailsAsync(TenantId, InvoiceId).Returns(new InvoiceDto
        {
            Id = InvoiceId, VendorName = "Sprzedawca z naglowka", VendorNip = "1111111111", AmountGross = 99999m,
            DueDate = new DateOnly(2026, 12, 10)
        });
        _api.GetTransferDetailsAsync(TenantId, InvoiceId).Returns(Transfer());

        var vm = new InvoiceDetailsViewModel(_api) { TenantId = TenantId.ToString(), InvoiceId = InvoiceId.ToString() };

        Assert.True(vm.HasTransfer);
        Assert.Null(vm.TransferError);
        Assert.Equal("Faktor Testowy S.A.", vm.TransferRows[0].ClipboardText);
        Assert.Equal("12345,60", vm.TransferRows.Single(r => r.Caption == "Kwota").ClipboardText);
        Assert.Equal(["InvoiceDetailsPageRowCopyRecipient", "InvoiceDetailsPageRowCopyAccount", "InvoiceDetailsPageRowCopyAmount", "InvoiceDetailsPageRowCopyTitle", "InvoiceDetailsPageRowCopyNip", "InvoiceDetailsPageRowDue"],
            vm.TransferRows.Select(r => r.AutomationId));
        var due = vm.TransferRows[^1];
        Assert.Equal("10.12.2026", due.Value);
        Assert.False(due.IsCopyable);
        Assert.Null(due.Hint);
    }

    [Fact]
    public async Task InvoiceDetails_TransferFails_InvoiceShownErrorThenRetryLoadsCard()
    {
        _api.GetInvoiceDetailsAsync(TenantId, InvoiceId).Returns(new InvoiceDto { Id = InvoiceId, VendorName = "V", VendorNip = "1111111111" });
        _api.GetTransferDetailsAsync(TenantId, InvoiceId).ThrowsAsync(new ApiException(HttpStatusCode.ServiceUnavailable, "offline"));

        var vm = new InvoiceDetailsViewModel(_api) { TenantId = TenantId.ToString(), InvoiceId = InvoiceId.ToString() };

        Assert.NotNull(vm.Invoice);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.HasTransfer);
        Assert.Equal("Nie udało się pobrać danych przelewu.", vm.TransferError);

        _api.GetTransferDetailsAsync(TenantId, InvoiceId).Returns(Transfer());
        await vm.LoadTransferCommand.ExecuteAsync(null);

        Assert.True(vm.HasTransfer);
        Assert.Null(vm.TransferError);
        Assert.DoesNotContain(vm.TransferRows, r => r.Caption == "Termin płatności");
    }

    [Fact]
    public void QrCode_RowsFromTransferWithQr()
    {
        var transfer = Transfer();
        transfer.QrMessage = null;
        transfer.QrCodeBase64 = "data:image/png;base64,AQID";
        _api.GetTransferDetailsAsync(TenantId, InvoiceId).Returns(transfer);

        var vm = new QrCodeViewModel(_api) { TenantId = TenantId.ToString(), InvoiceId = InvoiceId.ToString() };

        Assert.NotNull(vm.QrImage);
        Assert.Equal(5, vm.TransferRows.Count);
        Assert.StartsWith("QrCodePageRowCopy", vm.TransferRows[0].AutomationId);
    }

    [Fact]
    public void QrCode_Offline_NoRows()
    {
        _api.GetTransferDetailsAsync(TenantId, InvoiceId).ThrowsAsync(new ApiException(HttpStatusCode.ServiceUnavailable, "offline"));

        var vm = new QrCodeViewModel(_api) { TenantId = TenantId.ToString(), InvoiceId = InvoiceId.ToString() };

        Assert.False(vm.HasTransfer);
        Assert.NotNull(vm.ErrorMessage);
    }
}
