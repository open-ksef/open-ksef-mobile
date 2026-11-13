using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
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
    private readonly RecordingLogger _logger = new();

    public ApiServiceTests()
    {
        _auth.GetAccessTokenAsync().Returns("test-access-token");
        _serverSettings.ServerUrl.Returns("https://ksef.example.com");
    }

    private ApiService CreateService() => new(new HttpClient(_handler), _auth, _serverSettings, _logger);

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

    [Theory]
    [InlineData(",\"qrMessage\":null", true)]
    [InlineData(",\"qrMessage\":\"x\"", true)]
    [InlineData("", false)]
    public async Task GetTransferDetails_FlagsServerRulesFromTheKeyNotItsValue(string messagePart, bool expected)
    {
        var tenantId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        _handler.RespondJson(HttpStatusCode.OK, "{\"recipientName\":\"Dostawca\",\"recipientAccount\":null,\"recipientNip\":\"1234567890\",\"amount\":12.00,\"currency\":\"PLN\",\"title\":\"FV 1\",\"transferText\":\"text\",\"qrCodeBase64\":\"data:image/png;base64,AQID\"" + messagePart + "}");

        var transfer = await CreateService().GetTransferDetailsAsync(tenantId, invoiceId);

        Assert.Equal($"https://ksef.example.com/api/tenants/{tenantId}/invoices/{invoiceId}/transfer", Assert.Single(_handler.Requests).Uri);
        Assert.Equal(expected, transfer.ServerHasQrRules);
        Assert.Equal("Dostawca", transfer.RecipientName);
        Assert.Equal("1234567890", transfer.RecipientNip);
        Assert.Equal(12.00m, transfer.Amount);
        Assert.Equal("PLN", transfer.Currency);
        Assert.Equal("FV 1", transfer.Title);
        Assert.Equal("text", transfer.TransferText);
        Assert.Equal("data:image/png;base64,AQID", transfer.QrCodeBase64);
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
    public async Task RegisterDevice_SignOutDuringRefresh_RetryCancelled()
    {
        using var session = new CancellationTokenSource();
        _auth.RefreshAccessTokenAsync().Returns(_ =>
        {
            session.Cancel();
            return "refreshed-token";
        });
        _handler.Respond(HttpStatusCode.Unauthorized);
        _handler.Respond(HttpStatusCode.OK);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            CreateService().RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest { Token = "fcm-1" }, session.Token));

        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task Unauthorized_ForcesRefreshOnce_RetriesWithNewToken()
    {
        _auth.RefreshAccessTokenAsync().Returns("refreshed-token");
        _handler.Respond(HttpStatusCode.Unauthorized);
        _handler.Respond(HttpStatusCode.OK, new List<TenantDto>());

        await CreateService().GetTenantsAsync();

        Assert.Equal(["Bearer test-access-token", "Bearer refreshed-token"], _handler.Requests.Select(r => r.Authorization));
        await _auth.Received(1).RefreshAccessTokenAsync();
    }

    [Fact]
    public async Task Unauthorized_OnPost_RetryResendsBodyWithNewToken()
    {
        _auth.RefreshAccessTokenAsync().Returns("refreshed-token");
        _handler.Respond(HttpStatusCode.Unauthorized);
        _handler.Respond(HttpStatusCode.OK, new TenantDto());

        await CreateService().CreateTenantAsync(new CreateTenantRequest { Nip = "1111111111" });

        Assert.Equal(2, _handler.Requests.Count);
        Assert.All(_handler.Requests, r => Assert.Equal("POST", r.Method));
        Assert.Contains("1111111111", _handler.Requests[0].Body);
        Assert.Equal(_handler.Requests[0].Body, _handler.Requests[1].Body);
        Assert.Equal("Bearer refreshed-token", _handler.Requests[1].Authorization);
    }

    [Fact]
    public async Task Unauthorized_AfterRetry_ThrowsTokenRejected()
    {
        _auth.RefreshAccessTokenAsync().Returns("refreshed-token");
        _handler.Respond(HttpStatusCode.Unauthorized);
        _handler.Respond(HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().GetMeAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Contains("Serwer nie przyjął logowania", ex.Message);
        Assert.Equal(2, _handler.Requests.Count);
        await _auth.Received(1).RefreshAccessTokenAsync();
    }

    [Fact]
    public async Task EachRequest_CarriesCurrentToken_DefaultHeadersStayClear()
    {
        var http = new HttpClient(_handler);
        var api = new ApiService(http, _auth, _serverSettings, _logger);
        _auth.GetAccessTokenAsync().Returns("token-a", "token-b", (string?)null);
        _handler.Respond(HttpStatusCode.OK, new List<TenantDto>());
        _handler.Respond(HttpStatusCode.OK, new List<TenantDto>());
        _handler.Respond(HttpStatusCode.OK, new List<TenantDto>());

        await api.GetTenantsAsync();
        await api.GetTenantsAsync();
        await api.GetTenantsAsync();

        Assert.Equal(["Bearer token-a", "Bearer token-b", null], _handler.Requests.Select(r => r.Authorization));
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task Unauthorized_RefreshFails_ThrowsSessionExpired_WithoutRetry()
    {
        _auth.RefreshAccessTokenAsync().Returns((string?)null);
        _handler.Respond(HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().GetMeAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal("Sesja wygasła. Zaloguj się ponownie.", ex.Message);
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
    public async Task ServerError_ShowsFixedText_LogsBodyWithoutTokenOrPersonalData()
    {
        const string jwt = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.c2lnbmF0dXJlLXZhbHVlLWhlcmU";
        _auth.GetAccessTokenAsync().Returns(jwt);
        _handler.RespondJson(HttpStatusCode.InternalServerError, $$"""{"detail":"Bearer {{jwt}} jan.kowalski@firma.pl NIP 526-104-08-28 boom"}""");

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().GetIssuedInvoicesAsync(Guid.NewGuid(), q: "Kowalski"));

        Assert.Equal("Błąd serwera (500). Spróbuj ponownie później.", ex.Message);
        var log = Assert.Single(_logger.Lines);
        Assert.Contains("500", log);
        Assert.Contains("boom", log);
        Assert.DoesNotContain("eyJ", log);
        Assert.DoesNotContain("kowalski", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("526-104", log);
    }

    [Fact]
    public async Task NipLookup_NotFound_LogDoesNotCarryNip()
    {
        _handler.Respond(HttpStatusCode.NotFound);

        Assert.Null(await CreateService().LookupNipAsync("5261040828"));

        Assert.DoesNotContain("5261040828", Assert.Single(_logger.Lines));
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, """{"error":"A tenant with this NIP already exists for your account."}""", "A tenant with this NIP already exists for your account.")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"Invalid NIP format. Must be 10 digits."}""", "Invalid NIP format. Must be 10 digits.")]
    [InlineData(HttpStatusCode.Conflict, """{"message":"Numer jest już użyty."}""", "Numer jest już użyty.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"error":"Serwer nie ma prawidłowego klucza szyfrowania, więc nie może zapisać ani odczytać poświadczeń KSeF. Skontaktuj się z administratorem serwera."}""", "Serwer nie ma prawidłowego klucza szyfrowania, więc nie może zapisać ani odczytać poświadczeń KSeF. Skontaktuj się z administratorem serwera.")]
    public async Task ServerRuleMessage_ReachesUser_NotLogged(HttpStatusCode status, string body, string shown)
    {
        _handler.RespondJson(status, body);

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().CreateTenantAsync(new CreateTenantRequest { Nip = "1111111111" }));

        Assert.Equal(shown, ex.Message);
        Assert.Empty(_logger.Lines);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, """{"error":"KSeF said: secret upstream detail"}""", "Błąd serwera (502). Spróbuj ponownie później.")]
    [InlineData(HttpStatusCode.BadRequest, "<html>proxy secret upstream detail</html>", "Serwer odrzucił żądanie (400).")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "<html>proxy secret upstream detail</html>", "Błąd serwera (503). Spróbuj ponownie później.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"title":"secret upstream detail"}""", "Błąd serwera (503). Spróbuj ponownie później.")]
    [InlineData(HttpStatusCode.BadRequest, """{"title":"secret upstream detail","errors":{"Nip":["Invalid"]}}""", "Serwer odrzucił żądanie (400).")]
    public async Task UnstructuredBody_ShowsFixedText_BodyOnlyLogged(HttpStatusCode status, string body, string shown)
    {
        _handler.RespondJson(status, body);

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().GetTenantsAsync());

        Assert.Equal(shown, ex.Message);
        Assert.Contains("secret upstream detail", Assert.Single(_logger.Lines));
    }

    [Fact]
    public async Task ValidationEnvelope_ReachesCaller_FixedMessage_NotLogged()
    {
        _handler.RespondJson(HttpStatusCode.BadRequest, """{"errors":[{"code":"INV-VAL-001","field":"buyer.nip","message":"Niepoprawny NIP nabywcy"}],"warnings":[]}""");

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().CreateIssuedInvoiceAsync(Guid.NewGuid(), new IssuedInvoiceDraftRequest()));

        Assert.Equal("Niepoprawny NIP nabywcy", Assert.Single(ex.Validation!.Errors).Message);
        Assert.Equal("Serwer odrzucił żądanie (400).", ex.Message);
        Assert.Empty(_logger.Lines);
    }

    [Fact]
    public async Task Timeout_ThrowsNetworkFailure_WithFixedText()
    {
        _handler.Throw(new TaskCanceledException("HttpClient.Timeout of 30 seconds elapsing"));

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().GetTenantsAsync());

        Assert.True(ApiException.IsNetworkFailure(ex));
        Assert.Equal("Brak połączenia z serwerem. Spróbuj ponownie po powrocie sieci.", ex.Message);
    }

    [Fact]
    public async Task ConnectionFailure_ThrowsNetworkFailure()
    {
        _handler.Throw(new HttpRequestException("no route"));

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().GetTenantsAsync());

        Assert.True(ApiException.IsNetworkFailure(ex));
        Assert.DoesNotContain("no route", ex.Message);
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
    public async Task GetInvoices_Unpaid_AddsIsPaidFalseAndReadsDueDate()
    {
        var tenantId = Guid.NewGuid();
        _handler.RespondJson(HttpStatusCode.OK, """
            {"items":[{"id":"6f2a3a0e-8f3e-4a8e-9f1e-7d2b3c4d5e6f","isPaid":false,"dueDate":"2026-11-19","isOverdue":true}],"page":2,"pageSize":20,"totalCount":21}
            """);

        var result = await CreateService().GetInvoicesAsync(tenantId, page: 2, isPaid: false);

        Assert.Equal($"https://ksef.example.com/api/tenants/{tenantId}/invoices?page=2&pageSize=20&isPaid=false", Assert.Single(_handler.Requests).Uri);
        var invoice = Assert.Single(result.Items);
        Assert.Equal(new DateOnly(2026, 11, 19), invoice.DueDate);
        Assert.True(invoice.IsOverdue);
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
    public async Task GetIssuedInvoices_AddsMonthAndSearchFilters()
    {
        var tenantId = Guid.NewGuid();
        _handler.RespondJson(HttpStatusCode.OK, $$"""{"items":[{{IssuedInvoiceContractTests.ListItemJson}}],"page":1,"pageSize":20,"totalCount":1}""");

        var result = await CreateService().GetIssuedInvoicesAsync(tenantId, month: "2026-11", q: "FV/1 Nabywca");

        Assert.Equal($"https://ksef.example.com/api/tenants/{tenantId}/issued-invoices?page=1&pageSize=20&month=2026-11&q=FV%2F1 Nabywca", Assert.Single(_handler.Requests).Uri);
        var item = Assert.Single(result.Items);
        Assert.Equal((new DateOnly(2026, 11, 17), 100.00m), (item.DueDate, item.TotalNet));
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
    public async Task Conflict_WithMessageBody_UsesMessageWithoutValidation()
    {
        _handler.RespondJson(HttpStatusCode.Conflict, """{"message":"Numer FV/12/11/2026 jest już użyty."}""");

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().IssueInvoiceAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal("Numer FV/12/11/2026 jest już użyty.", ex.Message);
        Assert.Empty(ex.Validation?.Errors ?? []);
    }

    [Fact]
    public async Task Conflict_WithEnvelope_ExposesValidation()
    {
        _handler.RespondJson(HttpStatusCode.Conflict, """{"errors":[{"code":"SP-VAL-006","field":"nextNumber","message":"Numer 3 jest już użyty."}],"warnings":[]}""");

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().UpdateSellerProfileAsync(Guid.NewGuid(), new SellerProfileRequest()));

        Assert.Equal(("SP-VAL-006", "nextNumber"), (Assert.Single(ex.Validation!.Errors).Code, ex.Validation.Errors[0].Field));
    }

    [Fact]
    public async Task GetIssuedInvoiceXml_ReturnsStoredBytes()
    {
        var bytes = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Faktura/>"u8.ToArray();
        _handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

        Assert.Equal(bytes, await CreateService().GetIssuedInvoiceXmlAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    public static TheoryData<uint?, string> VersionQueries => new() { { 4000000000u, "?version=4000000000" }, { null, "" } };

    [Theory]
    [MemberData(nameof(VersionQueries))]
    public async Task UpdateIssuedInvoice_SendsVersionOnlyWhenKnown(uint? version, string query)
    {
        var (tenantId, invoiceId) = (Guid.NewGuid(), Guid.NewGuid());
        _handler.RespondJson(HttpStatusCode.OK, IssuedInvoiceContractTests.InvoiceResponseJson);

        await CreateService().UpdateIssuedInvoiceAsync(tenantId, invoiceId, new IssuedInvoiceDraftRequest(), version);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("PUT", request.Method);
        Assert.Equal($"https://ksef.example.com/api/tenants/{tenantId}/issued-invoices/{invoiceId}{query}", request.Uri);
        Assert.DoesNotContain("version", request.Body);
    }

    [Theory]
    [MemberData(nameof(VersionQueries))]
    public async Task DeleteIssuedInvoice_SendsVersionOnlyWhenKnown(uint? version, string query)
    {
        var (tenantId, invoiceId) = (Guid.NewGuid(), Guid.NewGuid());
        _handler.Respond(HttpStatusCode.NoContent);

        await CreateService().DeleteIssuedInvoiceAsync(tenantId, invoiceId, version);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("DELETE", request.Method);
        Assert.Equal($"https://ksef.example.com/api/tenants/{tenantId}/issued-invoices/{invoiceId}{query}", request.Uri);
    }

    [Theory]
    [MemberData(nameof(VersionQueries))]
    public async Task IssueInvoice_SendsVersionOnlyWhenKnown(uint? version, string query)
    {
        var (tenantId, invoiceId) = (Guid.NewGuid(), Guid.NewGuid());
        _handler.RespondJson(HttpStatusCode.Accepted, IssuedInvoiceContractTests.InvoiceResponseJson);

        await CreateService().IssueInvoiceAsync(tenantId, invoiceId, version);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal($"https://ksef.example.com/api/tenants/{tenantId}/issued-invoices/{invoiceId}/issue{query}", request.Uri);
    }

    [Fact]
    public async Task StaleVersion_ConflictShowsServerMessage()
    {
        _handler.RespondJson(HttpStatusCode.Conflict, "{\"message\":\"Faktura została równocześnie zmieniona, odśwież stronę\"}");

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().DeleteIssuedInvoiceAsync(Guid.NewGuid(), Guid.NewGuid(), 7));

        Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
        Assert.Equal("Faktura została równocześnie zmieniona, odśwież stronę", ex.Message);
        Assert.Empty(ex.Validation?.Errors ?? []);
    }

    [Theory]
    [InlineData("application/json", "{\"id\":")]
    [InlineData("text/html", "<html>proxy</html>")]
    public async Task Unreadable2xxBody_ThrowsApiExceptionWithPolishText(string mediaType, string body)
    {
        _handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType) });

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().IssueInvoiceAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Equal("Odpowiedź serwera jest nieczytelna. Nie wiadomo, czy operacja się udała. Sprawdź aktualny stan, zanim spróbujesz ponownie.", ex.Message);
    }

    [Fact]
    public async Task NullBody2xx_KeepsSuccessStatus()
    {
        _handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json") });

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().CopyIssuedInvoiceAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Created, ex.StatusCode);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("delete")]
    public async Task ExplicitTokenDeviceCalls_HonourCancellation(string call)
    {
        var api = new ApiService(new HttpClient(new HangingHandler()), _auth, _serverSettings, _logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Task request = call == "get"
            ? api.GetDevicesAsync("old-access", cts.Token)
            : api.UnregisterDeviceAsync(Guid.NewGuid(), "old-access", cts.Token);

        Assert.IsType<ApiException>(await Record.ExceptionAsync(() => request.WaitAsync(TimeSpan.FromSeconds(5))));
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException();
        }
    }

    private sealed class RecordingLogger : ILogger<ApiService>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
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
