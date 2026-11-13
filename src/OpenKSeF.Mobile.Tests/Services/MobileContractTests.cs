using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using SQLite;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

/// <summary>Consumer contract: backend golden field list (open-ksef MobileContract/responses.json) and fake payloads in its shape, read by the app's ApiService into the app's Models.</summary>
[Collection("Shell")]
public class MobileContractTests
{
    private const string Detail = "GET /api/tenants/{id}/invoices/{invoiceId}";
    private const string DetailLinesNull = "GET /api/tenants/{id}/invoices/{invoiceId} lines null";
    private const string Unpaid = "GET /api/tenants/{id}/invoices?isPaid=false";
    private const string Transfer = "GET /api/tenants/{id}/invoices/{invoiceId}/transfer";
    private const string TransferQrBlocked = "GET /api/tenants/{id}/invoices/{invoiceId}/transfer (QR blocked)";
    private const string IssuedDetail = "GET /api/tenants/{id}/issued-invoices/{invoiceId}";
    private const string NipLookup = "GET /api/nip-lookup/{nip}";
    private const string NipLookupInvalid = "GET /api/nip-lookup/{nip} (invalid)";
    private const string Hub = "SignalR ReceiveNotification";
    private const string HubSummary = "SignalR ReceiveNotification (newInvoicesSummary)";
    private const string GoldenSha256 = "FFBBD803CD866B41174C6E00CAFDD80056EADE87F36B54418FDD4C8E21633098";

    private static readonly JsonObject Golden = Load("responses.json");
    private static readonly JsonObject Payloads = Load("payloads.json");
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid InvoiceId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    private static JsonObject Load(string name) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MobileContract", name)))!.AsObject();

    public static TheoryData<string, Type> GoldenTypes => new()
    {
        { "GET /api/account/me", typeof(UserInfo) },
        { "GET /api/account/onboarding-status", typeof(OnboardingStatusDto) },
        { "POST /api/tenants", typeof(TenantDto) },
        { "GET /api/tenants", typeof(List<TenantDto>) },
        { "PUT /api/tenants/{id}", typeof(TenantDto) },
        { "GET /api/tenants/{id}/invoices", typeof(PagedResult<InvoiceDto>) },
        { Detail, typeof(InvoiceDto) },
        { "GET /api/devices", typeof(List<DeviceTokenDto>) },
        { Transfer, typeof(TransferDetailsDto) },
        { TransferQrBlocked, typeof(TransferDetailsDto) },
        { "GET /api/tenants/{id}/seller-profile", typeof(SellerProfileDto) },
        { "PUT /api/tenants/{id}/seller-profile", typeof(SellerProfileDto) },
        { "POST /api/tenants/{id}/issued-invoices", typeof(IssuedInvoiceDto) },
        { IssuedDetail, typeof(IssuedInvoiceDto) },
        { "POST /api/tenants/{id}/issued-invoices/{invoiceId}/copy", typeof(IssuedInvoiceDto) },
        { "POST /api/tenants/{id}/issued-invoices/{invoiceId}/issue", typeof(IssuedInvoiceDto) },
        { "GET /api/tenants/{id}/issued-invoices", typeof(PagedResult<IssuedInvoiceListItemDto>) },
        { NipLookup, typeof(NipLookupDto) },
        { NipLookupInvalid, typeof(ValidationEnvelope) },
    };

    [Fact]
    public void Golden_CoversEveryMappedEndpoint()
    {
        var mapped = GoldenTypes.Select(row => (string)row[0]).Append(Hub).Append(HubSummary);

        Assert.Equal(Golden.Select(p => p.Key).Order(), mapped.Order());
    }

    [Theory]
    [InlineData(Hub)]
    [InlineData(HubSummary)]
    public void GoldenHubArguments_BindToNotificationPayload(string entry)
    {
        var missing = Golden[entry]!.AsObject()
            .Select(f => f.Key)
            .Where(path => path.StartsWith("arguments[].") && !path.StartsWith("arguments[].data."))
            .Where(path => Resolve(typeof(NotificationHubService.NotificationPayload), path["arguments[].".Length..]) is null)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void GoldenSummaryHubPayload_ParsesWithoutInvoiceId_AndOpensTenantList()
    {
        var data = Golden[HubSummary]!.AsObject()
            .Select(f => f.Key)
            .Where(path => path.StartsWith("arguments[].data."))
            .Select(path => path["arguments[].data.".Length..])
            .ToList();
        Assert.DoesNotContain("invoiceId", data);
        var values = new Dictionary<string, string> { ["type"] = "newInvoicesSummary", ["tenantId"] = TenantId.ToString(), ["count"] = "15" };
        var json = new JsonObject
        {
            ["title"] = "Więcej nowych faktur",
            ["body"] = "Firma: pozostałe nowe faktury: 15. Zobacz je w aplikacji.",
            ["data"] = new JsonObject(data.Select(k => KeyValuePair.Create(k, (JsonNode?)values[k]))),
            ["nativePush"] = true
        };

        var payload = json.Deserialize<NotificationHubService.NotificationPayload>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(["count", "tenantId", "type"], payload.Data!.Keys.Order());
        Assert.True(payload.NativePush);
        try
        {
            Assert.True(NotificationHubService.OnNotificationTapped(payload.Data["type"], payload.Data["tenantId"]));
            Assert.Equal((TenantId, (Guid?)null), NotificationHubService.TakeTapped());
        }
        finally
        {
            NotificationHubService.TakeTapped();
        }
    }

    [Fact]
    public void GoldenInvoiceHubPayload_OpensThatInvoice()
    {
        var invoiceId = Guid.Parse("5c2f8e13-9a4b-4d6c-8e7f-0a1b2c3d4e5f");
        var data = Golden[Hub]!.AsObject()
            .Select(f => f.Key)
            .Where(path => path.StartsWith("arguments[].data."))
            .Select(path => path["arguments[].data.".Length..])
            .ToList();
        var values = new Dictionary<string, string> { ["tenantId"] = TenantId.ToString(), ["invoiceId"] = invoiceId.ToString() };
        var payload = new JsonObject
        {
            ["title"] = "New invoice received",
            ["body"] = "New invoice from 'Firma'",
            ["data"] = new JsonObject(data.Select(k => KeyValuePair.Create(k, (JsonNode?)values[k])))
        }.Deserialize<NotificationHubService.NotificationPayload>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        payload.Data!.TryGetValue("type", out var type);
        try
        {
            Assert.True(NotificationHubService.OnNotificationTapped(type, payload.Data["tenantId"], payload.Data["invoiceId"]));
            Assert.Equal((TenantId, (Guid?)invoiceId), NotificationHubService.TakeTapped());
        }
        finally
        {
            NotificationHubService.TakeTapped();
        }
    }

    [Theory]
    [MemberData(nameof(GoldenTypes))]
    public void GoldenField_BindsToMobileProperty(string endpoint, Type type)
    {
        var missing = Golden[endpoint]!.AsObject()
            .Select(f => f.Key)
            .Where(path => Resolve(type, path) is null)
            .ToList();

        Assert.True(missing.Count == 0, $"{endpoint}: no mobile property for {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(GoldenTypes))]
    public void Payload_HasExactlyTheGoldenFields(string endpoint, Type _)
    {
        Assert.Equal(Golden[endpoint]!.AsObject().Select(f => f.Key).Order(), Paths(Payloads[endpoint]!).Order());
    }

    [Fact]
    public void LinesNullPayload_DiffersFromGoldenOnlyInLines()
    {
        var expected = Golden[Detail]!.AsObject().Select(f => f.Key).Where(k => !k.StartsWith("lines[].")).Append("lines");

        Assert.Equal(expected.Order(), Paths(Payloads[DetailLinesNull]!).Order());
    }

    [Fact]
    public async Task Me_MapsUser()
    {
        var me = await Api("GET /api/account/me").GetMeAsync();

        Assert.Equal("6f1d2c3b-0000-4000-8000-000000000001", me.UserId);
        Assert.Equal("jan.testowy@example.com", me.Email);
        Assert.Equal("Jan Testowy", me.Name);
    }

    [Fact]
    public async Task OnboardingStatus_MapsFlags()
    {
        var status = await Api("GET /api/account/onboarding-status").GetOnboardingStatusAsync();

        Assert.True(status.IsComplete);
        Assert.True(status.HasTenant);
        Assert.False(status.HasCredential);
        Assert.Equal(TenantId.ToString(), status.FirstTenantId);
    }

    [Fact]
    public async Task Tenants_MapsListWithNullableNames()
    {
        var tenants = await Api("GET /api/tenants").GetTenantsAsync();

        Assert.Equal(2, tenants.Count);
        Assert.Equal(TenantId, tenants[0].Id);
        Assert.Equal("1234567890", tenants[0].Nip);
        Assert.Equal("Firma Testowa", tenants[0].DisplayName);
        Assert.Equal("faktury@example.com", tenants[0].NotificationEmail);
        Assert.Equal(new DateTime(2026, 11, 1, 8, 15, 30, DateTimeKind.Utc).AddTicks(1234560), tenants[0].CreatedAt);
        Assert.Equal(DateTimeKind.Utc, tenants[0].CreatedAt.Kind);
        Assert.Null(tenants[1].DisplayName);
        Assert.Null(tenants[1].NotificationEmail);
    }

    [Fact]
    public async Task CreateAndUpdateTenant_MapResponse()
    {
        var created = await Api("POST /api/tenants").CreateTenantAsync(new CreateTenantRequest());
        var updated = await Api("PUT /api/tenants/{id}").UpdateTenantAsync(TenantId, new UpdateTenantRequest());

        Assert.Equal(TenantId, created.Id);
        Assert.Equal("Firma Testowa", created.DisplayName);
        Assert.Equal("Firma Testowa Zmieniona", updated.DisplayName);
        Assert.Equal("nowe@example.com", updated.NotificationEmail);
    }

    [Fact]
    public async Task InvoicePage_MapsItemsAndPaging()
    {
        var page = await Api("GET /api/tenants/{id}/invoices").GetInvoicesAsync(TenantId, 1, 20);

        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(41, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        var invoice = Assert.Single(page.Items);
        AssertFullInvoice(invoice);
        Assert.Null(invoice.DueDate);
        Assert.False(invoice.IsOverdue);
        Assert.Equal("—", invoice.DueText);
    }

    [Fact]
    public async Task UnpaidPage_MapsDueDateAndOverdue_InServerOrder()
    {
        var page = await Api(Unpaid).GetInvoicesAsync(TenantId, 2, 20, isPaid: false);

        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(["FV/1/11/2026", "FV/2/11/2026"], page.Items.Select(i => i.InvoiceNumber));
        Assert.All(page.Items, i => Assert.False(i.IsPaid));
        Assert.Equal(new DateOnly(2026, 11, 5), page.Items[0].DueDate);
        Assert.True(page.Items[0].IsOverdue);
        Assert.Equal("05.11.2026 po terminie", page.Items[0].DueText);
        Assert.Null(page.Items[1].DueDate);
        Assert.False(page.Items[1].IsOverdue);
        Assert.Equal("—", page.Items[1].DueText);
    }

    [Fact]
    public async Task UnpaidPage_ThroughSqlite_KeepsDueDateAndRecomputesOverdue()
    {
        var dto = (await Api(Unpaid).GetInvoicesAsync(TenantId, 2, 20, isPaid: false)).Items[0];
        var path = Path.Combine(Path.GetTempPath(), $"contract-{Guid.NewGuid():N}.db");
        CachedInvoice stored;
        using (var db = new SQLiteConnection(path))
        {
            db.CreateTable<CachedInvoice>();
            db.Insert(CachedInvoice.FromDto(dto, TenantId));
            stored = db.Table<CachedInvoice>().Single();
        }
        File.Delete(path);

        var before = stored.ToDto(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
        var after = stored.ToDto(new DateTimeOffset(2026, 11, 5, 23, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 11, 5), before.DueDate);
        Assert.False(before.IsPaid);
        Assert.False(before.IsOverdue);
        Assert.True(after.IsOverdue);
    }

    [Fact]
    public async Task InvoiceDetail_MapsHeaderAndLines()
    {
        var invoice = await Api(Detail).GetInvoiceDetailsAsync(TenantId, InvoiceId);

        AssertFullInvoice(invoice);
        var line = Assert.Single(invoice.Lines);
        Assert.Equal(1, line.LineNumber);
        Assert.Equal("Usluga testowa", line.Name);
        Assert.Equal("szt.", line.Unit);
        Assert.Equal(2.5m, line.Quantity);
        Assert.Equal(400.20m, line.UnitPriceNet);
        Assert.Equal(492.25m, line.UnitPriceGross);
        Assert.Equal(1000.50m, line.AmountNet);
        Assert.Equal(1230.62m, line.AmountGross);
        Assert.Equal(230.12m, line.AmountVat);
        Assert.Equal("23", line.VatRate);
    }

    [Fact]
    public async Task InvoiceDetail_WithNullLinesAndOptionals()
    {
        var invoice = await Api(DetailLinesNull).GetInvoiceDetailsAsync(TenantId, InvoiceId);

        Assert.Null(invoice.Lines);
        Assert.Null(invoice.InvoiceNumber);
        Assert.Equal("1111111112-20261105-0D1E2F000000-22", invoice.DisplayNumber);
        Assert.Null(invoice.VendorBankAccount);
        Assert.Null(invoice.BuyerName);
        Assert.Null(invoice.BuyerNip);
        Assert.Null(invoice.AcquisitionDate);
        Assert.Null(invoice.InvoiceType);
        Assert.Null(invoice.PaidAt);
        Assert.False(invoice.IsPaid);
        Assert.Equal("EUR", invoice.Currency);
        Assert.Equal(12.30m, invoice.AmountGross);
    }

    [Fact]
    public async Task Devices_MapPlatformAsInt()
    {
        var devices = await Api("GET /api/devices").GetDevicesAsync();

        Assert.Equal(2, devices.Count);
        Assert.Equal(Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"), devices[0].Id);
        Assert.Equal("fake-fcm-token-android", devices[0].Token);
        Assert.Equal(0, devices[0].Platform);
        Assert.Equal(TenantId, devices[0].TenantId);
        Assert.Equal(new DateTime(2026, 11, 3, 8, 16, 0, DateTimeKind.Utc), devices[0].UpdatedAt);
        Assert.Equal(1, devices[1].Platform);
        Assert.Null(devices[1].TenantId);
    }

    [Fact]
    public async Task Sync_MapsCounters()
    {
        var sync = await Api("POST /api/tenants/{id}/credentials/sync").ForceCredentialSyncAsync(TenantId);

        Assert.Equal(7, sync.FetchedInvoices);
        Assert.Equal(3, sync.NewInvoices);
    }

    [Fact]
    public async Task CachedInvoice_FromDtoToDto_KeepsMappedFieldsAndUtcKind()
    {
        var dto = await Api(Detail).GetInvoiceDetailsAsync(TenantId, InvoiceId);

        AssertCachedRoundTrip(dto, CachedInvoice.FromDto(dto, TenantId));
    }

    [Fact]
    public async Task CachedInvoice_ThroughSqlite_KeepsMappedFieldsAndUtcKind()
    {
        var dto = await Api(Detail).GetInvoiceDetailsAsync(TenantId, InvoiceId);
        var path = Path.Combine(Path.GetTempPath(), $"contract-{Guid.NewGuid():N}.db");
        CachedInvoice stored;
        using (var db = new SQLiteConnection(path))
        {
            db.CreateTable<CachedInvoice>();
            db.Insert(CachedInvoice.FromDto(dto, TenantId));
            stored = db.Table<CachedInvoice>().Single();
        }
        File.Delete(path);

        AssertCachedRoundTrip(dto, stored);
    }

    [Fact]
    public void Golden_IsByteIdenticalToBackend()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MobileContract", "responses.json"));

        Assert.Equal(GoldenSha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
    }

    [Fact]
    public async Task Transfer_ExplicitNullQrMessage_MarksServerQrRules()
    {
        var transfer = await Api(Transfer).GetTransferDetailsAsync(TenantId, InvoiceId);

        Assert.True(transfer.ServerHasQrRules);
        Assert.Null(transfer.QrMessage);
        Assert.Equal("iVBORw0KGgo=", transfer.QrCodeBase64);
        Assert.Equal("Dostawca Testowy", transfer.RecipientName);
        Assert.Equal("61109010140000071219812874", transfer.RecipientAccount);
        Assert.Equal("1111111111", transfer.RecipientNip);
        Assert.Equal(1230.62m, transfer.Amount);
        Assert.Equal("PLN", transfer.Currency);
        Assert.Equal("FV/12/11/2026", transfer.Title);
        Assert.StartsWith("Dostawca Testowy\n", transfer.TransferText);
    }

    [Fact]
    public async Task Transfer_Eur_HasMessageAndNoQr()
    {
        var transfer = await Api(TransferQrBlocked).GetTransferDetailsAsync(TenantId, InvoiceId);

        Assert.True(transfer.ServerHasQrRules);
        Assert.Null(transfer.QrCodeBase64);
        Assert.Null(transfer.RecipientAccount);
        Assert.Equal("Kod QR tylko dla PLN.", transfer.QrMessage);
        Assert.Equal("EUR", transfer.Currency);
        Assert.Equal(12.30m, transfer.Amount);
    }

    [Fact]
    public async Task SellerProfile_MapsCompleteAndEmpty()
    {
        var saved = await Api("PUT /api/tenants/{id}/seller-profile").UpdateSellerProfileAsync(TenantId, new SellerProfileRequest());
        var empty = await Api("GET /api/tenants/{id}/seller-profile").GetSellerProfileAsync(TenantId);

        Assert.Equal("Firma Testowa Sp. z o.o.", saved.LegalName);
        Assert.Equal("ul. Testowa 1", saved.AddressLine1);
        Assert.Equal("00-001 Warszawa", saved.AddressLine2);
        Assert.Equal("61109010140000071219812874", saved.BankAccount);
        Assert.True(saved.CashAccounting);
        Assert.Equal(13, saved.NextNumber);
        Assert.Equal("2026-11", saved.NextNumberPeriod);
        Assert.Equal("FV/{n}/{MM}/{yyyy}", saved.NumberingPattern);
        Assert.Equal("FV/13/11/2026", saved.NextNumberPreview);
        Assert.True(saved.IsComplete);
        Assert.Equal("Test", saved.KsefEnvironment);
        Assert.NotNull(empty);
        Assert.Equal("1111111111", empty.Nip);
        Assert.False(empty.IsComplete);
        Assert.Null(empty.LegalName);
        Assert.Null(empty.NextNumber);
        Assert.Null(empty.NextNumberPreview);
    }

    [Fact]
    public async Task IssuedInvoiceCreate_MapsDraftWithExemption()
    {
        var draft = await Api("POST /api/tenants/{id}/issued-invoices").CreateIssuedInvoiceAsync(TenantId, new IssuedInvoiceDraftRequest());

        Assert.Equal(Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"), draft.Id);
        Assert.Equal("Draft", draft.Status);
        Assert.Null(draft.Number);
        Assert.Equal("1111111111", draft.Seller.Nip);
        Assert.Equal("Company", draft.Buyer.Kind);
        Assert.Equal("1234567890", draft.Buyer.Nip);
        Assert.Equal(new DateOnly(2026, 11, 5), draft.IssueDate);
        Assert.Equal(new DateOnly(2026, 11, 4), draft.SaleDate);
        Assert.Equal(new DateOnly(2026, 11, 19), draft.DueDate);
        Assert.Equal("Act", draft.ExemptionBasis!.Kind);
        Assert.Equal("art. 113 ust. 1 ustawy o VAT", draft.ExemptionBasis.Text);
        var line = Assert.Single(draft.Lines);
        Assert.Equal(1, line.LineNo);
        Assert.Equal(2.5m, line.Quantity);
        Assert.Equal(400.20m, line.UnitNetPrice);
        Assert.Equal("zw", line.VatRate);
        Assert.Equal(1000.50m, line.NetAmount);
        Assert.Equal(0m, line.VatAmount);
        Assert.Equal(1000.50m, Assert.Single(draft.VatSummary).Net);
        Assert.Equal(1000.50m, draft.TotalGross);
        Assert.False(draft.Ksef.HasUpo);
        Assert.Null(draft.Ksef.KsefNumber);
        Assert.Null(draft.IssuedAt);
        Assert.Null(draft.ExchangeRate);
        Assert.Null(draft.ExchangeRateDate);
        Assert.Equal(1u, draft.Version);
        Assert.Equal(DateTimeKind.Utc, draft.CreatedAt.Kind);
    }

    [Fact]
    public async Task IssuedInvoiceDetail_MapsForeignAcceptedEur()
    {
        var invoice = await Api(IssuedDetail).GetIssuedInvoiceAsync(TenantId, InvoiceId);

        Assert.Equal("Accepted", invoice.Status);
        Assert.Equal("FV/13/11/2026", invoice.Number);
        Assert.Equal("PL61109010140000071219812874", invoice.Seller.Iban);
        Assert.Equal("TESTPLPW", invoice.Seller.Swift);
        Assert.Equal("Bank Testowy", invoice.Seller.BankName);
        Assert.True(invoice.Seller.CashAccounting);
        Assert.Equal("Foreign", invoice.Buyer.Kind);
        Assert.Null(invoice.Buyer.Nip);
        Assert.Equal("DE", invoice.Buyer.CountryCode);
        Assert.Equal("VatUe", invoice.Buyer.IdType);
        Assert.Equal("DE000000000", invoice.Buyer.ForeignId);
        Assert.Equal("Directive", invoice.ExemptionBasis!.Kind);
        Assert.Null(Assert.Single(invoice.Lines).Unit);
        Assert.Equal("EUR", invoice.Currency);
        Assert.Equal(4.2512m, invoice.ExchangeRate);
        Assert.Equal(new DateOnly(2026, 11, 4), invoice.ExchangeRateDate);
        Assert.Equal(3u, invoice.Version);
        Assert.Equal("1111111111-20261105-0F1E2D000000-33", invoice.Ksef.KsefNumber);
        Assert.Equal(new DateTime(2026, 11, 5, 9, 5, 0, DateTimeKind.Utc), invoice.Ksef.AcceptedAt);
        Assert.Equal(200, invoice.Ksef.StatusCode);
        Assert.Equal("Sukces", invoice.Ksef.StatusMessage);
        Assert.Equal("https://ksef-test.example.com/verify/1", invoice.Ksef.VerificationUrl);
        Assert.True(invoice.Ksef.HasUpo);
        Assert.Equal(new DateTime(2026, 11, 5, 9, 4, 0, DateTimeKind.Utc), invoice.IssuedAt);
    }

    [Fact]
    public async Task IssuedInvoiceCopyAndIssue_MapResponse()
    {
        var copy = await Api("POST /api/tenants/{id}/issued-invoices/{invoiceId}/copy").CopyIssuedInvoiceAsync(TenantId, InvoiceId);
        var issued = await Api("POST /api/tenants/{id}/issued-invoices/{invoiceId}/issue").IssueInvoiceAsync(TenantId, InvoiceId, 3);

        Assert.Equal(Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"), copy.Id);
        Assert.Equal("Person", copy.Buyer.Kind);
        Assert.Equal("Cash", copy.PaymentMethod);
        Assert.Equal("kg", Assert.Single(copy.Lines).Unit);
        Assert.Equal("Rejected", issued.Status);
        Assert.True(issued.SplitPayment);
        Assert.Equal("Other", issued.ExemptionBasis!.Kind);
        Assert.Equal(440, issued.Ksef.StatusCode);
        Assert.Equal("Faktura o tym numerze juz istnieje.", issued.Ksef.StatusDetails);
        Assert.Equal("Duplikat faktury", issued.Ksef.LastError);
        Assert.Equal("1111111111-20261104-0A0B0C000000-44", issued.Ksef.DuplicateKsefNumber);
        Assert.Equal(230.12m, issued.TotalVat);
        Assert.Equal(1230.62m, issued.TotalGross);
        Assert.Equal(4u, issued.Version);
    }

    [Fact]
    public async Task IssuedInvoiceList_MapsItemsAndPaging()
    {
        var page = await Api("GET /api/tenants/{id}/issued-invoices").GetIssuedInvoicesAsync(TenantId);

        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(21, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(2, page.Items.Count);
        var foreign = page.Items[0];
        Assert.Equal("Accepted", foreign.Status);
        Assert.Equal("Example GmbH", foreign.BuyerName);
        Assert.Null(foreign.BuyerNip);
        Assert.Equal(new DateOnly(2026, 11, 5), foreign.IssueDate);
        Assert.Equal(new DateOnly(2026, 11, 19), foreign.DueDate);
        Assert.Equal(100.00m, foreign.TotalNet);
        Assert.Equal("EUR", foreign.Currency);
        Assert.Equal("1111111111-20261105-0F1E2D000000-33", foreign.KsefNumber);
        Assert.Equal("DE", foreign.BuyerCountryCode);
        Assert.Equal("VatUe", foreign.BuyerIdType);
        Assert.Equal("DE000000000", foreign.BuyerForeignId);
        Assert.Null(page.Items[1].Number);
        Assert.Null(page.Items[1].KsefNumber);
        Assert.Null(page.Items[1].BuyerCountryCode);
        Assert.Equal(1230.62m, page.Items[1].TotalGross);
    }

    [Fact]
    public async Task NipLookup_MapsCompanyAndAccounts()
    {
        var company = await Api(NipLookup).LookupNipAsync("1111111111");

        Assert.NotNull(company);
        Assert.Equal("1111111111", company.Nip);
        Assert.Equal("Firma Testowa Sp. z o.o.", company.Name);
        Assert.Equal("ul. Testowa 1", company.AddressLine1);
        Assert.Null(company.AddressLine2);
        Assert.Equal("Czynny", company.VatStatus);
        Assert.Equal(["61109010140000071219812874", "27114020040000300201355387"], company.BankAccounts);
    }

    [Fact]
    public async Task NipLookup_Invalid400_LandsInValidation()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => Api(NipLookupInvalid, HttpStatusCode.BadRequest).LookupNipAsync("123"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal(2, ex.Validation!.Errors.Count);
        Assert.Equal("NIP-001", ex.Validation.Errors[0].Code);
        Assert.Equal("nip", ex.Validation.Errors[0].Field);
        Assert.Equal("Nieprawidlowy NIP.", ex.Validation.Errors[0].Message);
        Assert.Null(ex.Validation.Errors[1].Field);
    }

    private static void AssertCachedRoundTrip(InvoiceDto dto, CachedInvoice stored)
    {
        Assert.Equal(TenantId.ToString(), stored.TenantId);
        var back = stored.ToDto();
        Assert.Equal(dto.Id, back.Id);
        Assert.Equal(dto.KSeFInvoiceNumber, back.KSeFInvoiceNumber);
        Assert.Equal(dto.KSeFReferenceNumber, back.KSeFReferenceNumber);
        Assert.Equal("FV/12/11/2026", back.InvoiceNumber);
        Assert.Equal("FV/12/11/2026", back.DisplayNumber);
        Assert.Equal(dto.VendorName, back.VendorName);
        Assert.Equal(dto.VendorNip, back.VendorNip);
        Assert.Equal(dto.BuyerName, back.BuyerName);
        Assert.Equal(dto.BuyerNip, back.BuyerNip);
        Assert.Equal(dto.AmountNet, back.AmountNet);
        Assert.Equal(dto.AmountVat, back.AmountVat);
        Assert.Equal(dto.AmountGross, back.AmountGross);
        Assert.Equal(dto.Currency, back.Currency);
        Assert.Equal(dto.InvoiceType, back.InvoiceType);
        Assert.Equal(dto.IssueDate, back.IssueDate);
        Assert.Equal(dto.AcquisitionDate, back.AcquisitionDate);
        Assert.Equal(dto.FirstSeenAt, back.FirstSeenAt);
        Assert.True(back.IsPaid);
        Assert.False(back.IsOverdue);
        Assert.Equal(DateTimeKind.Utc, back.IssueDate.Kind);
        Assert.Equal(DateTimeKind.Utc, back.AcquisitionDate!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, back.FirstSeenAt.Kind);
    }

    private static void AssertFullInvoice(InvoiceDto invoice)
    {
        Assert.Equal(InvoiceId, invoice.Id);
        Assert.Equal("1111111112-20261103-0A1B2C000000-11", invoice.KSeFInvoiceNumber);
        Assert.Equal("20261103-EE-0A1B2C0000-11", invoice.KSeFReferenceNumber);
        Assert.Equal("FV/12/11/2026", invoice.InvoiceNumber);
        Assert.Equal("Dostawca Testowy", invoice.VendorName);
        Assert.Equal("1111111112", invoice.VendorNip);
        Assert.Equal("61109010140000071219812874", invoice.VendorBankAccount);
        Assert.Equal("Firma Testowa", invoice.BuyerName);
        Assert.Equal("1234567890", invoice.BuyerNip);
        Assert.Equal(1000.50m, invoice.AmountNet);
        Assert.Equal(230.12m, invoice.AmountVat);
        Assert.Equal(1230.62m, invoice.AmountGross);
        Assert.Equal("PLN", invoice.Currency);
        Assert.Equal(new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc), invoice.IssueDate);
        Assert.Equal(DateTimeKind.Utc, invoice.IssueDate.Kind);
        Assert.Equal(new DateTime(2026, 11, 3, 10, 20, 30, DateTimeKind.Utc), invoice.AcquisitionDate);
        Assert.Equal("VAT", invoice.InvoiceType);
        Assert.Equal(new DateTime(2026, 11, 3, 10, 25, 0, 500, DateTimeKind.Utc), invoice.FirstSeenAt);
        Assert.True(invoice.IsPaid);
        Assert.Equal(new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc), invoice.PaidAt);
    }

    private static ApiService Api(string endpoint, HttpStatusCode status = HttpStatusCode.OK)
    {
        var auth = Substitute.For<IAuthService>();
        auth.GetAccessTokenAsync().Returns("token");
        var settings = Substitute.For<IServerSettingsService>();
        settings.ServerUrl.Returns("https://ksef.example.com");
        return new ApiService(new HttpClient(new FixtureHandler(Payloads[endpoint]!.ToJsonString(), status)), auth, settings, NullLogger<ApiService>.Instance);
    }

    private static PropertyInfo? Resolve(Type type, string path)
    {
        PropertyInfo? property = null;
        foreach (var segment in path.Split('.'))
        {
            var name = segment.Replace("[]", "");
            if (name.Length == 0)
            {
                type = Element(type);
                continue;
            }
            property = type.GetProperties().FirstOrDefault(p =>
                p.GetCustomAttribute<JsonIgnoreAttribute>() is not { Condition: JsonIgnoreCondition.Always } &&
                string.Equals(p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (property is null)
                return null;
            type = segment.EndsWith("[]") ? Element(property.PropertyType) : property.PropertyType;
        }
        return property;
    }

    private static Type Element(Type type) => type.IsGenericType ? type.GetGenericArguments()[0] : type;

    private static IEnumerable<string> Paths(JsonNode node, string prefix = "") => node switch
    {
        JsonArray array => array.SelectMany(item => Paths(item!, prefix + "[]")).Distinct(),
        JsonObject obj => obj.SelectMany(p => p.Value is JsonObject or JsonArray
            ? Paths(p.Value, Join(prefix, p.Key))
            : [Join(prefix, p.Key)]).Distinct(),
        _ => [prefix],
    };

    private static string Join(string prefix, string key) => prefix.Length == 0 ? key : prefix + "." + key;

    private sealed class FixtureHandler(string json, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
    }
}
