using System.Net;
using System.Net.Http.Json;
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
            {"items":[{"id":"6f2a3a0e-8f3e-4a8e-9f1e-7d2b3c4d5e6f","kSeFInvoiceNumber":"FV/2026/001","vendorName":"Vendor Sp. z o.o.","amountGross":1230.00,"currency":"PLN","issueDate":"2026-01-15T00:00:00Z","isPaid":true}],
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

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SentRequest(request.Method.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            return _responses.Dequeue()();
        }
    }
}
