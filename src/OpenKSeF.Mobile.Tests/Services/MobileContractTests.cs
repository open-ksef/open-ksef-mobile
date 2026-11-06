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
public class MobileContractTests
{
    private const string Detail = "GET /api/tenants/{id}/invoices/{invoiceId}";
    private const string DetailLinesNull = "GET /api/tenants/{id}/invoices/{invoiceId} lines null";

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
    };

    [Fact]
    public void Golden_CoversEveryMappedEndpoint()
    {
        var mapped = GoldenTypes.Select(row => (string)row[0]).Append("SignalR ReceiveNotification");

        Assert.Equal(Golden.Select(p => p.Key).Order(), mapped.Order());
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
        AssertFullInvoice(Assert.Single(page.Items));
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
        Assert.Equal("9876543210-20261105-0D1E2F000000-22", invoice.DisplayNumber);
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

    [Fact(Skip = "Defect: sqlite-net ticks storage reads DateTime back as Unspecified (T-127 QA report)")]
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
        Assert.Equal(DateTimeKind.Utc, back.IssueDate.Kind);
        Assert.Equal(DateTimeKind.Utc, back.AcquisitionDate!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, back.FirstSeenAt.Kind);
    }

    private static void AssertFullInvoice(InvoiceDto invoice)
    {
        Assert.Equal(InvoiceId, invoice.Id);
        Assert.Equal("9876543210-20261103-0A1B2C000000-11", invoice.KSeFInvoiceNumber);
        Assert.Equal("20261103-EE-0A1B2C0000-11", invoice.KSeFReferenceNumber);
        Assert.Equal("FV/12/11/2026", invoice.InvoiceNumber);
        Assert.Equal("Dostawca Testowy", invoice.VendorName);
        Assert.Equal("9876543210", invoice.VendorNip);
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

    private static ApiService Api(string endpoint)
    {
        var auth = Substitute.For<IAuthService>();
        auth.GetAccessTokenAsync().Returns("token");
        var settings = Substitute.For<IServerSettingsService>();
        settings.ServerUrl.Returns("https://ksef.example.com");
        return new ApiService(new HttpClient(new FixtureHandler(Payloads[endpoint]!.ToJsonString())), auth, settings, NullLogger<ApiService>.Instance);
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
                p.GetCustomAttribute<JsonIgnoreAttribute>() is null &&
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

    private sealed class FixtureHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
    }
}
