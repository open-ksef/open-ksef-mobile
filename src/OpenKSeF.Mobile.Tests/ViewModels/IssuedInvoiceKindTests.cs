using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using NSubstitute;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

/// <summary>Non-VAT kinds (KOR, ZAL, ROZ, KOR_ZAL, KOR_ROZ) on the backend fixtures: open-ksef 140b683 (KOR) and tick/T-845-zal-roz b6031a7.</summary>
[Collection("Shell")]
public class IssuedInvoiceKindTests : IDisposable
{
    private static readonly Guid TenantId = Guid.Parse("304f3ff7-06b6-43e8-bdf4-a0ad77f194e3");
    private static readonly Guid InvoiceId = Guid.Parse("0b7c9a62-3f6e-4d1a-9c55-1f2e3d4c5b6a");
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly IApiService _api = Substitute.For<IApiService>();
    private readonly IServerSettingsService _serverSettings = Substitute.For<IServerSettingsService>();
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public IssuedInvoiceKindTests()
    {
        Shell.Current.Reset();
        Preferences.Default.Set("SelectedTenantId", TenantId.ToString());
        _serverSettings.ServerUrl.Returns("https://portal.example.test");
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        Preferences.Default.Clear();
    }

    private static string Fixture(string name)
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "IssuedInvoices", $"issued-invoice-{name}.json"));
        return Regex.Replace(json, "\"<(\\w+)>\"", m => m.Groups[1].Value switch
        {
            "version" or "sellerVersion" => "1",
            "nip" => "\"1111111111\"",
            "verificationUrl" => "\"https://qr-test.ksef.mf.gov.pl/invoice/1111111111/20-12-2026/abc\"",
            "createdAt" or "updatedAt" or "issuedAt" or "acceptedAt" => "\"2026-12-20T10:00:00Z\"",
            _ => $"\"{Guid.NewGuid()}\""
        });
    }

    private static IssuedInvoiceDto Invoice(string name)
    {
        var invoice = JsonSerializer.Deserialize<IssuedInvoiceDto>(Fixture(name), Web)!;
        invoice.Id = InvoiceId;
        return invoice;
    }

    private async Task<IssuedInvoiceDetailsViewModel> DetailsOf(IssuedInvoiceDto invoice)
    {
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(invoice);
        var vm = new IssuedInvoiceDetailsViewModel(_api, _serverSettings, new LocalDbService()) { TenantId = TenantId.ToString(), InvoiceId = InvoiceId.ToString() };
        await vm.LoadCommand.ExecuteAsync(null);
        return vm;
    }

    private static string Plain(string text) => text.Replace(' ', ' ');

    [Theory]
    [InlineData("correction-accepted", "KOR", "Korekta")]
    [InlineData("correction-draft", "KOR", "Korekta")]
    [InlineData("original-with-corrections", "VAT", null)]
    [InlineData("zal", "ZAL", "Zaliczka")]
    [InlineData("roz", "ROZ", "Rozliczenie")]
    [InlineData("kor-zal", "KOR_ZAL", "Korekta zaliczki")]
    [InlineData("kor-roz", "KOR_ROZ", "Korekta rozliczenia")]
    [InlineData("chain", "ZAL", "Zaliczka")]
    public void Fixture_Deserializes_WithKindAndPortalOnly(string name, string type, string? label)
    {
        var invoice = Invoice(name);

        Assert.Equal(type, invoice.InvoiceType);
        Assert.Equal(label, IssuingFormat.KindLabel(invoice.InvoiceType));
        Assert.Equal(type != "VAT", IssuingFormat.IsPortalOnly(invoice));
    }

    [Fact]
    public void ListFixture_CarriesInvoiceType()
    {
        var page = JsonSerializer.Deserialize<PagedResult<IssuedInvoiceListItemDto>>(Fixture("list"), Web)!;

        Assert.Equal("VAT", Assert.Single(page.Items).InvoiceType);
    }

    [Fact]
    public void KorFixture_MapsReferenceRowsAndSignedTotal()
    {
        var invoice = Invoice("correction-accepted");

        Assert.Equal(-123m, invoice.TotalGross);
        Assert.Equal(("FV/1/11/2026", "VAT"), (invoice.CorrectedInvoice!.Number, invoice.CorrectedInvoice.InvoiceType));
        Assert.Equal([true, false], invoice.CorrectionRows!.Select(r => r.IsBefore));
        Assert.Equal("Zwrot towaru", invoice.CorrectionReason);
    }

    [Fact]
    public void ChainFixture_MapsAdvancesFinalAndCorrections()
    {
        var chain = Invoice("chain").Chain!;

        Assert.Equal((1230m, 615m), (chain.OrderValue, chain.Remaining));
        Assert.Equal(["ZAL/1/12/2026", "ZAL/2/12/2026"], chain.Advances.Select(a => a.Number));
        Assert.Equal("ROZ", chain.Final!.InvoiceType);
        Assert.Equal("KOR_ROZ", Assert.Single(chain.Corrections).InvoiceType);
    }

    [Theory]
    [InlineData("pl-PL")]
    [InlineData("en-US")]
    public async Task KorAccepted_ShowsMinusKorektaAndBeforeAfterRows(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        var vm = await DetailsOf(Invoice("correction-accepted"));

        Assert.Equal("Korekta", vm.KindText);
        Assert.Equal("Kwota korekty", vm.TotalCaption);
        Assert.Equal("-123,00 PLN", Plain(vm.GrossText));
        Assert.Equal("Faktura korygowana: FV/1/11/2026 z 03.11.2026\nKSeF 5213456789-20261103-ABCDEF123456-AB\nPrzyczyna: Zwrot towaru", vm.ReferenceText);
        Assert.Equal(["Przed: Usługa", "Po: Usługa"], vm.CorrectionLines.Select(l => l.Name));
        Assert.Equal(["100,00 PLN", "0,00 PLN"], vm.CorrectionLines.Select(l => Plain(l.Gross)));
        Assert.False(vm.HasLines);
    }

    [Theory]
    [InlineData("pl-PL")]
    [InlineData("en-US")]
    public void ListRow_Kor_ShowsMinusAndKorekta(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        var row = new IssuedInvoiceRow(new IssuedInvoiceListItemDto
        {
            Status = "Accepted", Number = "KOR/1/12/2026", IssueDate = new DateOnly(2026, 12, 20), TotalGross = -1234.5m, InvoiceType = "KOR"
        });

        Assert.Equal("Korekta · KOR/1/12/2026 · 20.12.2026", row.Subtitle);
        Assert.Equal("-1 234,50 PLN", Plain(row.Gross));
    }

    [Fact]
    public async Task KorAccepted_OnlyActionIsCorrectInPortal()
    {
        var invoice = Invoice("correction-accepted");
        var vm = await DetailsOf(invoice);

        Assert.True(vm.IsPortalOnly);
        Assert.False(vm.IsEditableDraft);
        Assert.False(vm.CanCopyAsNew);
        Assert.True(vm.CanOpenInPortal);
        Assert.Equal("Koryguj w portalu", vm.PortalButtonText);

        await vm.OpenInPortalCommand.ExecuteAsync(null);

        Assert.Equal($"https://portal.example.test/issued/{invoice.CorrectedInvoice!.Id}?tenantId={TenantId}", Browser.Default.LastUri);
    }

    [Fact]
    public async Task KorDraft_NoSaveOrIssue_CorrectInPortalOpensCorrectionPage()
    {
        _api.GetSellerProfileAsync(TenantId).Returns(new SellerProfileDto { IsComplete = true, KsefEnvironment = "test" });
        var vm = await DetailsOf(Invoice("correction-draft"));

        Assert.False(vm.IsEditableDraft);
        Assert.False(vm.CanFixRejection);
        Assert.Equal("Koryguj w portalu", vm.PortalButtonText);

        await vm.IssueCommand.ExecuteAsync(null);
        await vm.EditCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(null);
        await vm.CopyAsNewCommand.ExecuteAsync(null);
        await vm.OpenInPortalCommand.ExecuteAsync(null);

        await _api.DidNotReceiveWithAnyArgs().IssueInvoiceAsync(default, default);
        await _api.DidNotReceiveWithAnyArgs().DeleteIssuedInvoiceAsync(default, default, default);
        await _api.DidNotReceiveWithAnyArgs().CopyIssuedInvoiceAsync(default, default);
        Assert.Null(Shell.Current.LastRoute);
        Assert.Equal($"https://portal.example.test/issued/{InvoiceId}/correct?tenantId={TenantId}", Browser.Default.LastUri);
    }

    [Fact]
    public async Task DraftForm_KorDraft_SavesNothing()
    {
        _api.GetSellerProfileAsync(TenantId).Returns(new SellerProfileDto { IsComplete = true, KsefEnvironment = "test" });
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(Invoice("correction-draft"));
        var vm = new InvoiceDraftViewModel(_api) { TenantId = TenantId.ToString(), InvoiceId = InvoiceId.ToString() };

        await vm.LoadCommand.ExecuteAsync(null);
        await vm.SaveDraftCommand.ExecuteAsync(null);
        await vm.IssueCommand.ExecuteAsync(null);

        Assert.Equal("Tę fakturę edytujesz w portalu.", vm.ErrorMessage);
        await _api.DidNotReceiveWithAnyArgs().UpdateIssuedInvoiceAsync(default, default, default!, default);
        await _api.DidNotReceiveWithAnyArgs().CreateIssuedInvoiceAsync(default, default!);
        await _api.DidNotReceiveWithAnyArgs().IssueInvoiceAsync(default, default);
    }

    [Fact]
    public async Task ZalDraft_ShowsOrderAndChain_EditInPortalOpensChainDraft()
    {
        var vm = await DetailsOf(Invoice("zal"));

        Assert.Equal("Zaliczka", vm.KindText);
        Assert.Equal("Kwota zaliczki", vm.TotalCaption);
        Assert.Equal("369,00 PLN", Plain(vm.GrossText));
        Assert.Equal("Wystawiona 06.12.2026 · zapłata 05.12.2026", vm.DatesText);
        Assert.Equal("Zamówienie", vm.LinesCaption);
        Assert.True(vm.HasLines);
        Assert.Equal("Zamówienie 1 230,00 PLN · pozostało 1 230,00 PLN", Plain(vm.RelatedCaption!));
        Assert.Equal("Zaliczka bez numeru", Assert.Single(vm.RelatedRows).Name);
        Assert.False(vm.IsEditableDraft);
        Assert.Equal("Edytuj w portalu", vm.PortalButtonText);

        await vm.OpenInPortalCommand.ExecuteAsync(null);

        Assert.Equal($"https://portal.example.test/issued/{InvoiceId}/chain?tenantId={TenantId}", Browser.Default.LastUri);
    }

    [Fact]
    public async Task Roz_ShowsDeductedAdvancesAndChain()
    {
        var vm = await DetailsOf(Invoice("roz"));

        Assert.Equal("Rozliczenie", vm.KindText);
        Assert.Equal("Do zapłaty", vm.TotalCaption);
        Assert.Equal("Rozlicza zaliczki: ZAL/1/12/2026, ZAL/2/12/2026", vm.ReferenceText);
        Assert.Equal(
            ["Zaliczka ZAL/1/12/2026|369,00 PLN", "Zaliczka ZAL/2/12/2026|246,00 PLN", "Rozliczenie FV/1/12/2026|615,00 PLN"],
            vm.RelatedRows.Select(r => $"{r.Name}|{Plain(r.Gross)}"));
        Assert.Equal("Koryguj w portalu", vm.PortalButtonText);
    }

    [Fact]
    public async Task KorZal_ShowsAdvanceBeforeAndAfter()
    {
        var vm = await DetailsOf(Invoice("kor-zal"));

        Assert.Equal("Korekta zaliczki", vm.KindText);
        Assert.Equal("-123,00 PLN", Plain(vm.GrossText));
        Assert.Contains("Faktura korygowana: ZAL/1/12/2026 z 06.12.2026", vm.ReferenceText);
        Assert.Contains("Zaliczka przed korektą 369,00 PLN · po korekcie 246,00 PLN", Plain(vm.ReferenceText!));
        Assert.Equal(2, vm.CorrectionLines.Count);
    }

    [Fact]
    public async Task KorRoz_ShowsAmountBefore()
    {
        var vm = await DetailsOf(Invoice("kor-roz"));

        Assert.Equal("Korekta rozliczenia", vm.KindText);
        Assert.Contains("Do zapłaty przed korektą 615,00 PLN", Plain(vm.ReferenceText!));
        Assert.Equal(["Przed: Projekt strony", "Po: Projekt strony"], vm.CorrectionLines.Select(l => l.Name));
    }

    [Fact]
    public async Task OriginalWithCorrections_ListsCorrections_CorrectInPortal()
    {
        var vm = await DetailsOf(Invoice("original-with-corrections"));

        Assert.Null(vm.KindText);
        Assert.False(vm.IsPortalOnly);
        Assert.Equal("Korekty · po korektach 0,00 PLN", Plain(vm.RelatedCaption!));
        Assert.Equal("Korekta KOR/1/12/2026|-123,00 PLN", vm.RelatedRows.Select(r => $"{r.Name}|{Plain(r.Gross)}").Single());
        Assert.True(vm.CanCorrectInPortal);
        Assert.Equal("Koryguj w portalu", vm.PortalButtonText);
    }

    [Fact]
    public async Task UnknownKind_IsReadOnly_NeverVat()
    {
        var draft = Invoice("correction-draft");
        draft.InvoiceType = "FUTURE";
        var vm = await DetailsOf(draft);

        Assert.True(vm.IsPortalOnly);
        Assert.False(vm.IsEditableDraft);
        Assert.Equal("FUTURE", vm.KindText);
        Assert.Equal("Edytuj w portalu", vm.PortalButtonText);

        draft.Status = "Accepted";
        vm = await DetailsOf(draft);

        Assert.False(vm.CanCorrectInPortal);
        Assert.False(vm.CanOpenInPortal);
    }

    [Fact]
    public async Task ServerBeforeKor_NoInvoiceTypeKey_StaysVatWithoutCorrectAction()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(Fixture("original-with-corrections"))!.AsObject();
        foreach (var key in new[] { "invoiceType", "correctedInvoice", "correctionReason", "correctionType", "correctionRows", "buyerBefore", "currentState", "corrections", "openCorrectionId", "orderChainId", "advanceAmount", "amountBefore", "advanceInvoices", "chain" })
            node.Remove(key);
        var invoice = node.Deserialize<IssuedInvoiceDto>(Web)!;
        invoice.Id = InvoiceId;

        var vm = await DetailsOf(invoice);

        Assert.Null(invoice.InvoiceType);
        Assert.False(vm.IsPortalOnly);
        Assert.False(vm.CanOpenInPortal);
        Assert.True(vm.CanCopyAsNew);
        Assert.Null(vm.RelatedCaption);
    }

    [Fact]
    public async Task NullDueDate_Parses_AndDatesOmitTerm()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(Fixture("correction-accepted"))!;
        node["dueDate"] = null;
        var list = System.Text.Json.Nodes.JsonNode.Parse(Fixture("list"))!;
        list["items"]![0]!["dueDate"] = null;

        var invoice = node.Deserialize<IssuedInvoiceDto>(Web)!;
        invoice.Id = InvoiceId;
        var vm = await DetailsOf(invoice);

        Assert.Null(list.Deserialize<PagedResult<IssuedInvoiceListItemDto>>(Web)!.Items[0].DueDate);
        Assert.Equal("Wystawiona 20.12.2026", vm.DatesText);
    }
}
