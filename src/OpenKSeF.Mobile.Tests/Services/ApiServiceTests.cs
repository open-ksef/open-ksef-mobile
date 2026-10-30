using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using NSubstitute;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

public class ApiServiceTests
{
    private readonly IAuthService _auth = Substitute.For<IAuthService>();
    private readonly IServerSettingsService _serverSettings = Substitute.For<IServerSettingsService>();
    private readonly FakeHandler _handler = new();

    public ApiServiceTests()
    {
        _auth.GetAccessTokenAsync().Returns("test-access-token");
        _serverSettings.ServerUrl.Returns("https://ksef.example.com");
    }

    private ApiService CreateService() => new(new HttpClient(_handler), _auth, _serverSettings);

    [Fact]
    public async Task GetMe_SendsBearerTokenToConfiguredServer()
    {
        _handler.Respond(HttpStatusCode.OK, new UserInfo { UserId = "u1", Email = "test@example.com" });

        var me = await CreateService().GetMeAsync();

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("https://ksef.example.com/api/account/me", request.Uri);
        Assert.Equal("Bearer test-access-token", request.Authorization);
        Assert.Equal("u1", me.UserId);
    }

    [Fact]
    public async Task GetInvoices_BuildsPagedUrlAndDeserializesBackendJson()
    {
        var tenantId = Guid.NewGuid();
        _handler.RespondJson(HttpStatusCode.OK, """
            {"items":[{"id":"6f2a3a0e-8f3e-4a8e-9f1e-7d2b3c4d5e6f","ksefInvoiceNumber":"FV/2026/001","vendorName":"Vendor Sp. z o.o.","amountGross":1230.00,"currency":"PLN","issueDate":"2026-01-15T00:00:00Z","isPaid":true}],
             "page":2,"pageSize":20,"totalCount":21}
            """);

        var result = await CreateService().GetInvoicesAsync(tenantId, page: 2);

        Assert.Equal($"https://ksef.example.com/api/tenants/{tenantId}/invoices?page=2&pageSize=20", Assert.Single(_handler.Requests).Uri);
        var invoice = Assert.Single(result.Items);
        Assert.Equal("FV/2026/001", invoice.KSeFInvoiceNumber);
        Assert.Equal(1230.00m, invoice.AmountGross);
        Assert.True(invoice.IsPaid);
        Assert.Equal(21, result.TotalCount);
    }

    [Fact]
    public async Task Unauthorized_RetriesOnceWithFreshToken()
    {
        _auth.GetAccessTokenAsync().Returns("expired-token", "refreshed-token");
        _handler.Respond(HttpStatusCode.Unauthorized);
        _handler.Respond(HttpStatusCode.OK, new List<TenantDto>());

        await CreateService().GetTenantsAsync();

        Assert.Equal(["Bearer expired-token", "Bearer refreshed-token"], _handler.Requests.Select(r => r.Authorization));
    }

    [Fact]
    public async Task Unauthorized_AfterRetry_ThrowsSessionExpired()
    {
        _handler.Respond(HttpStatusCode.Unauthorized);
        _handler.Respond(HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().GetMeAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task Unauthorized_NoToken_DoesNotRetry()
    {
        _auth.GetAccessTokenAsync().Returns((string?)null);
        _handler.Respond(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<ApiException>(() => CreateService().GetMeAsync());

        Assert.Single(_handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ErrorStatus_ThrowsApiExceptionWithStatus(HttpStatusCode status)
    {
        _handler.Respond(status);

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().GetTenantsAsync());

        Assert.Equal(status, ex.StatusCode);
        Assert.False(ApiException.IsNetworkFailure(ex));
    }

    [Fact]
    public async Task ConnectionFailure_ThrowsNetworkFailure()
    {
        _handler.Throw(new HttpRequestException("no route"));

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().GetTenantsAsync());

        Assert.True(ApiException.IsNetworkFailure(ex));
    }

    [Fact]
    public async Task RegisterDeviceToken_PostsBackendContract()
    {
        _handler.Respond(HttpStatusCode.OK);

        await CreateService().RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest { Token = "fcm-1", Platform = 0, TenantId = null });

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://ksef.example.com/api/devices/register", request.Uri);
        Assert.Equal("""{"token":"fcm-1","platform":0,"tenantId":null}""", request.Body);
    }

    [Fact]
    public async Task GetSellerProfile_NotFound_ReturnsNullAsIssuingNotSupported()
    {
        var tenantId = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.NotFound);

        var profile = await CreateService().GetSellerProfileAsync(tenantId);

        Assert.Null(profile);
        Assert.Equal($"https://ksef.example.com/api/tenants/{tenantId}/seller-profile", Assert.Single(_handler.Requests).Uri);
    }

    [Fact]
    public async Task GetSellerProfile_DeserializesResponse()
    {
        _handler.RespondJson(HttpStatusCode.OK, IssuedInvoiceContractTests.SellerProfileResponseJson);

        var profile = await CreateService().GetSellerProfileAsync(Guid.NewGuid());

        Assert.NotNull(profile);
        Assert.Equal("FV/57/11/2026", profile.NextNumberPreview);
        Assert.Equal(57, profile.NextNumber);
    }

    [Fact]
    public async Task LookupNip_NotFound_ReturnsNull()
    {
        _handler.Respond(HttpStatusCode.NotFound);

        Assert.Null(await CreateService().LookupNipAsync("1234567890"));
        Assert.Equal("https://ksef.example.com/api/nip-lookup/1234567890", Assert.Single(_handler.Requests).Uri);
    }

    [Fact]
    public async Task CreateIssuedInvoice_PostsDraftContract()
    {
        var tenantId = Guid.NewGuid();
        _handler.RespondJson(HttpStatusCode.Created, IssuedInvoiceContractTests.InvoiceResponseJson);
        var draft = JsonSerializer.Deserialize<IssuedInvoiceDraftRequest>(IssuedInvoiceContractTests.DraftRequestJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var invoice = await CreateService().CreateIssuedInvoiceAsync(tenantId, draft);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal($"https://ksef.example.com/api/tenants/{tenantId}/issued-invoices", request.Uri);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(IssuedInvoiceContractTests.DraftRequestJson), JsonNode.Parse(request.Body!)), request.Body);
        Assert.Equal(123.00m, invoice.TotalGross);
    }

    [Fact]
    public async Task GetIssuedInvoices_AddsStatusFilter()
    {
        var tenantId = Guid.NewGuid();
        _handler.RespondJson(HttpStatusCode.OK, $$"""{"items":[{{IssuedInvoiceContractTests.ListItemJson}}],"page":1,"pageSize":20,"totalCount":1}""");

        var result = await CreateService().GetIssuedInvoicesAsync(tenantId, status: "Accepted");

        Assert.Equal($"https://ksef.example.com/api/tenants/{tenantId}/issued-invoices?page=1&pageSize=20&status=Accepted", Assert.Single(_handler.Requests).Uri);
        Assert.Equal("FV/1/11/2026", Assert.Single(result.Items).Number);
    }

    [Fact]
    public async Task IssueInvoice_Unprocessable_ExposesValidationEnvelope()
    {
        var tenantId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        _handler.RespondJson(HttpStatusCode.UnprocessableEntity, IssuedInvoiceContractTests.ValidationEnvelopeJson);

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().IssueInvoiceAsync(tenantId, invoiceId));

        Assert.Equal($"https://ksef.example.com/api/tenants/{tenantId}/issued-invoices/{invoiceId}/issue", Assert.Single(_handler.Requests).Uri);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        var error = Assert.Single(ex.Validation!.Errors);
        Assert.Equal(("INV-VAL-020", "buyer.nip"), (error.Code, error.Field));
    }

    [Fact]
    public async Task BadRequest_WithProblemDetails_HasNoValidationEnvelope()
    {
        _handler.RespondJson(HttpStatusCode.BadRequest, """{"title":"One or more validation errors occurred.","errors":{"Nip":["Invalid"]}}""");

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().GetTenantsAsync());

        Assert.Null(ex.Validation);
    }

    [Fact]
    public async Task GetIssuedInvoiceXml_ReturnsStoredBytes()
    {
        var bytes = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Faktura/>"u8.ToArray();
        _handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

        Assert.Equal(bytes, await CreateService().GetIssuedInvoiceXmlAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    private sealed record SentRequest(string Method, string Uri, string? Authorization, string? Body);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();

        public List<SentRequest> Requests { get; } = [];

        public void Respond(HttpStatusCode status, object? body = null) =>
            _responses.Enqueue(() => new HttpResponseMessage(status) { Content = body is null ? null : JsonContent.Create(body) });

        public void RespondJson(HttpStatusCode status, string json) =>
            _responses.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });

        public void Throw(Exception ex) => _responses.Enqueue(() => throw ex);

        public void Enqueue(Func<HttpResponseMessage> response) => _responses.Enqueue(response);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SentRequest(request.Method.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            return _responses.Dequeue()();
        }
    }
}
