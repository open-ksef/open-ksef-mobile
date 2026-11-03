using System.Net;
using System.Text.Json;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class QrCodeViewModelTests
{
    private const string OldServerMessage = "Kod QR wymaga nowszej wersji serwera. Skopiuj dane przelewu.";
    private const string TransferText = "Odbiorca: Dostawca Testowy\nRachunek: PL61 1090 1014 0000 0712 1981 2874\nKwota: 1 476,00 PLN\nTytul: FV 1/2026";
    private const string Png = "data:image/png;base64,AQID";
    private static readonly byte[] PngBytes = [1, 2, 3];
    private static readonly Guid TenantId = Guid.Parse("304f3ff7-06b6-43e8-bdf4-a0ad77f194e3");
    private static readonly Guid InvoiceA = Guid.Parse("0b7c9a62-3f6e-4d1a-9c55-1f2e3d4c5b6a");
    private static readonly Guid InvoiceB = Guid.Parse("1c8d0b73-4a7f-4e2b-8d66-2a3f4e5d6c7b");

    private readonly IApiService _api = Substitute.For<IApiService>();

    public QrCodeViewModelTests()
    {
        Shell.Current.Reset();
        Share.Default.Requests.Clear();
    }

    private static string V007(string currency = "PLN", decimal amount = 1476.00m) => $$"""
        {"recipientName":"Dostawca Testowy","recipientAccount":"PL61 1090 1014 0000 0712 1981 2874","recipientNip":"1234567890","amount":{{amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"currency":"{{currency}}","title":"FV 1/2026","transferText":{{JsonSerializer.Serialize(TransferText)}},"qrCodeBase64":"{{Png}}"}
        """;

    private static string Main(string? qr, string? message, string currency = "PLN") => $$"""
        {"recipientName":"Dostawca Testowy","recipientAccount":"PL61 1090 1014 0000 0712 1981 2874","recipientNip":"1234567890","amount":1476.00,"currency":"{{currency}}","title":"FV 1/2026","transferText":{{JsonSerializer.Serialize(TransferText)}},"qrCodeBase64":{{JsonSerializer.Serialize(qr)}},"qrMessage":{{JsonSerializer.Serialize(message)}}}
        """;

    private static TransferDetailsDto Dto(string json) =>
        JsonSerializer.Deserialize<TransferDetailsDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private void Server(Guid invoiceId, string json) =>
        _api.GetTransferDetailsAsync(TenantId, invoiceId).Returns(Dto(json));

    private QrCodeViewModel Open(Guid invoiceId) =>
        new(_api) { TenantId = TenantId.ToString(), InvoiceId = invoiceId.ToString() };

    private static byte[] Drawn(QrCodeViewModel vm)
    {
        using var stream = vm.QrImage!.Stream!();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static void AssertNoQr(QrCodeViewModel vm)
    {
        Assert.Null(vm.QrImage);
        vm.ShareQrCommand.Execute(null);
        Assert.Empty(Share.Default.Requests);
    }

    [Theory]
    [InlineData("PLN", 1476.00)]
    [InlineData("EUR", 123.45)]
    public void V007_NoQrMessageKey_NeverDrawsItsQr(string currency, double amount)
    {
        Server(InvoiceA, V007(currency, (decimal)amount));

        var vm = Open(InvoiceA);

        AssertNoQr(vm);
        Assert.Equal(OldServerMessage, vm.QrMessage);
        Assert.Equal(TransferText, vm.TransferText);
        Assert.True(vm.ShowTransferText);
    }

    [Fact]
    public void Main_QrAndExplicitNullMessage_DrawsServerBytes()
    {
        Server(InvoiceA, Main(Png, null));

        var vm = Open(InvoiceA);

        Assert.Equal(PngBytes, Drawn(vm));
        Assert.Null(vm.QrMessage);
        Assert.False(vm.ShowTransferText);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Main_QrAndMessage_ShowsBoth()
    {
        Server(InvoiceA, Main(Png, "Sprawdź, czy faktura nie wymaga podzielonej płatności (MPP)."));

        var vm = Open(InvoiceA);

        Assert.Equal(PngBytes, Drawn(vm));
        Assert.Equal("Sprawdź, czy faktura nie wymaga podzielonej płatności (MPP).", vm.QrMessage);
        Assert.False(vm.ShowTransferText);
    }

    [Theory]
    [InlineData("PLN")]
    [InlineData("EUR")]
    public void Main_NoQrBytes_NeverDrawsAndShowsServerMessage(string currency)
    {
        var message = $"Faktura w walucie {currency}: kod QR obsługuje tylko przelew w PLN.";
        Server(InvoiceA, Main(null, message, currency));

        var vm = Open(InvoiceA);

        AssertNoQr(vm);
        Assert.Equal(message, vm.QrMessage);
        Assert.Equal(TransferText, vm.TransferText);
        Assert.True(vm.ShowTransferText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("data:image/png;base64,")]
    public void Main_EmptyQrPayload_NeverDraws(string qr)
    {
        Server(InvoiceA, Main(qr, null));

        var vm = Open(InvoiceA);

        AssertNoQr(vm);
        Assert.True(vm.ShowTransferText);
    }

    [Fact]
    public void Title_ComesFromServerFieldsInPolishFormat()
    {
        Server(InvoiceA, Main(null, "x", "EUR"));

        var vm = Open(InvoiceA);

        Assert.Equal("Dostawca Testowy — 1 476,00 EUR", vm.Title);
    }

    [Fact]
    public void BadBase64_ShowsFixedTextNotRawException()
    {
        Server(InvoiceA, Main("data:image/png;base64,@@@not base64@@@", null));

        var vm = Open(InvoiceA);

        AssertNoQr(vm);
        Assert.Equal("Nie udało się wyświetlić kodu QR. Skopiuj dane przelewu albo spróbuj ponownie.", vm.ErrorMessage);
        Assert.True(vm.ShowTransferText);
    }

    [Fact]
    public async Task Offline_ShowsMessageNoImage_RetryThenShowsQr()
    {
        const string noConnection = "Brak połączenia z serwerem. Spróbuj ponownie po powrocie sieci.";
        _api.GetTransferDetailsAsync(TenantId, InvoiceA)
            .ThrowsAsync(new ApiException(HttpStatusCode.ServiceUnavailable, noConnection));

        var vm = Open(InvoiceA);

        AssertNoQr(vm);
        Assert.Equal(noConnection, vm.ErrorMessage);
        Assert.Null(vm.TransferText);
        Assert.False(vm.IsBusy);

        _api.GetTransferDetailsAsync(TenantId, InvoiceA).Returns(Dto(Main(Png, null)));
        await vm.GenerateQrCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(PngBytes, Drawn(vm));
    }

    [Fact]
    public async Task RetryThatFails_ClearsEarlierQrAndMessage()
    {
        Server(InvoiceA, Main(Png, "MPP"));
        var vm = Open(InvoiceA);
        Assert.NotNull(vm.QrImage);

        _api.GetTransferDetailsAsync(TenantId, InvoiceA)
            .ThrowsAsync(new ApiException(HttpStatusCode.ServiceUnavailable, "offline"));
        await vm.GenerateQrCommand.ExecuteAsync(null);

        AssertNoQr(vm);
        Assert.Null(vm.QrMessage);
        Assert.Null(vm.Title);
        Assert.Equal("offline", vm.ErrorMessage);
    }

    [Fact]
    public void MovingToNextInvoice_ClearsQrWhileItLoads()
    {
        Server(InvoiceA, Main(Png, "MPP"));
        var pending = new TaskCompletionSource<TransferDetailsDto>();
        _api.GetTransferDetailsAsync(TenantId, InvoiceB).Returns(pending.Task);
        var vm = Open(InvoiceA);
        Assert.NotNull(vm.QrImage);

        vm.InvoiceId = InvoiceB.ToString();

        AssertNoQr(vm);
        Assert.Null(vm.Title);
        Assert.Null(vm.QrMessage);
        Assert.Null(vm.TransferText);
        Assert.True(vm.IsBusy);
    }

    [Fact]
    public async Task MovingToNextInvoice_NoQrThere_LeavesNothingOfThePrevious()
    {
        Server(InvoiceA, Main(Png, null));
        Server(InvoiceB, Main(null, "Brak poprawnego numeru rachunku na fakturze."));
        var vm = Open(InvoiceA);

        vm.InvoiceId = InvoiceB.ToString();
        await Task.Yield();

        AssertNoQr(vm);
        Assert.Equal("Brak poprawnego numeru rachunku na fakturze.", vm.QrMessage);
    }

    [Fact]
    public void SlowAnswerForPreviousInvoice_NeverReplacesTheCurrentOne()
    {
        var slowA = new TaskCompletionSource<TransferDetailsDto>();
        _api.GetTransferDetailsAsync(TenantId, InvoiceA).Returns(slowA.Task);
        Server(InvoiceB, Main(null, "message for B"));
        var vm = Open(InvoiceA);

        vm.InvoiceId = InvoiceB.ToString();
        Assert.Equal("message for B", vm.QrMessage);
        Assert.False(vm.IsBusy);

        slowA.SetResult(Dto(Main(Png, null)));

        AssertNoQr(vm);
        Assert.Equal("message for B", vm.QrMessage);
    }

    [Fact]
    public void SlowFailureForPreviousInvoice_LeavesCurrentOneClean()
    {
        var slowA = new TaskCompletionSource<TransferDetailsDto>();
        _api.GetTransferDetailsAsync(TenantId, InvoiceA).Returns(slowA.Task);
        Server(InvoiceB, Main(Png, null));
        var vm = Open(InvoiceA);

        vm.InvoiceId = InvoiceB.ToString();
        slowA.SetException(new ApiException(HttpStatusCode.ServiceUnavailable, "offline"));

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(PngBytes, Drawn(vm));
    }

    [Fact]
    public void AnswerForPreviousInvoice_WhileCurrentStillLoads_KeepsBusy()
    {
        var slowA = new TaskCompletionSource<TransferDetailsDto>();
        var slowB = new TaskCompletionSource<TransferDetailsDto>();
        _api.GetTransferDetailsAsync(TenantId, InvoiceA).Returns(slowA.Task);
        _api.GetTransferDetailsAsync(TenantId, InvoiceB).Returns(slowB.Task);
        var vm = Open(InvoiceA);

        vm.InvoiceId = InvoiceB.ToString();
        slowA.SetResult(Dto(Main(Png, null)));

        Assert.True(vm.IsBusy);
        AssertNoQr(vm);
    }

    [Fact]
    public async Task Share_WritesTheDecodedServerBytes()
    {
        Server(InvoiceA, Main(Png, null));
        var vm = Open(InvoiceA);

        await vm.ShareQrCommand.ExecuteAsync(null);

        var request = Assert.Single(Share.Default.Requests);
        Assert.Equal(PngBytes, await File.ReadAllBytesAsync(request.File!.FullPath));
    }

    [Fact]
    public async Task Copy_PutsServerTextOnClipboardByteForByte()
    {
        Server(InvoiceA, Main(null, "x"));
        var vm = Open(InvoiceA);

        await vm.CopyTransferTextCommand.ExecuteAsync(null);

        Assert.Equal(TransferText, Clipboard.Default.Text);
        Assert.Equal("Dane przelewu skopiowane do schowka.", Shell.Current.LastAlertMessage);
    }

    [Fact]
    public async Task Copy_ClipboardFails_ShowsFixedAlertAndNoRetryError()
    {
        Server(InvoiceA, Main(null, "x"));
        var vm = Open(InvoiceA);
        Clipboard.Default.Failure = new InvalidOperationException("raw");

        try
        {
            await vm.CopyTransferTextCommand.ExecuteAsync(null);
        }
        finally
        {
            Clipboard.Default.Failure = null;
        }

        Assert.Equal("Nie udało się skopiować danych przelewu.", Shell.Current.LastAlertMessage);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Title_NoRecipientName_ShowsOnlyAmount()
    {
        Server(InvoiceA, Main(null, "x").Replace("\"recipientName\":\"Dostawca Testowy\"", "\"recipientName\":\"\""));

        var vm = Open(InvoiceA);

        Assert.Equal(IssuingFormat.Money(1476.00m, "PLN"), vm.Title);
    }

    [Fact]
    public async Task Copy_WithNothingLoaded_LeavesClipboardAlone()
    {
        var pending = new TaskCompletionSource<TransferDetailsDto>();
        _api.GetTransferDetailsAsync(TenantId, InvoiceA).Returns(pending.Task);
        var vm = Open(InvoiceA);
        await Clipboard.Default.SetTextAsync("before");

        await vm.CopyTransferTextCommand.ExecuteAsync(null);

        Assert.Equal("before", Clipboard.Default.Text);
        Assert.Null(Shell.Current.LastAlertMessage);
    }

    [Fact]
    public void AppSources_BuildNoPaymentPayload()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../OpenKSeF.Mobile"));
        var separator = Path.DirectorySeparatorChar;

        var offenders = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".xaml"))
            .Where(f => !f.Contains($"{separator}bin{separator}") && !f.Contains($"{separator}obj{separator}"))
            .Where(f => File.ReadAllText(f).Contains("|PL|"))
            .ToList();

        Assert.Empty(offenders);
    }
}
