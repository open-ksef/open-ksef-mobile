using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenKSeF.Mobile.Models;

namespace OpenKSeF.Mobile.Services;

public class ApiService : IApiService
{
    private const string SessionExpired = "Sesja wygasła. Zaloguj się ponownie.";
    private const string NoConnection = "Brak połączenia z serwerem. Spróbuj ponownie po powrocie sieci.";
    private const string TokenRejected = "Serwer nie przyjął logowania. Spróbuj ponownie później, a jeśli błąd wróci, wyloguj się i zaloguj ponownie.";

    private static readonly Regex Sensitive = new(
        @"Bearer\s+\S+|[\w+/=.-]{32,}|[^\s""'<>@:,;]+@[^\s""'<>@:,;]+|\d(?:[ -]?\d){8,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly HttpClient _http;
    private readonly IAuthService _auth;
    private readonly IServerSettingsService _serverSettings;
    private readonly ILogger<ApiService> _logger;

    public ApiService(HttpClient http, IAuthService auth, IServerSettingsService serverSettings, ILogger<ApiService> logger)
    {
        _http = http;
        _http.DefaultRequestHeaders.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");
        _auth = auth;
        _serverSettings = serverSettings;
        _logger = logger;
    }

    public async Task<UserInfo> GetMeAsync()
    {
        return await SendAsync<UserInfo>(HttpMethod.Get, "/api/account/me");
    }

    public async Task<OnboardingStatusDto> GetOnboardingStatusAsync()
    {
        return await SendAsync<OnboardingStatusDto>(HttpMethod.Get, "/api/account/onboarding-status");
    }

    public async Task<List<TenantDto>> GetTenantsAsync()
    {
        return await SendAsync<List<TenantDto>>(HttpMethod.Get, "/api/tenants");
    }

    public async Task<TenantDto> CreateTenantAsync(CreateTenantRequest request)
    {
        return await SendAsync<TenantDto>(HttpMethod.Post, "/api/tenants", request);
    }

    public async Task<TenantDto> UpdateTenantAsync(Guid tenantId, UpdateTenantRequest request)
    {
        return await SendAsync<TenantDto>(HttpMethod.Put, $"/api/tenants/{tenantId}", request);
    }

    public async Task DeleteTenantAsync(Guid tenantId)
    {
        await SendNoContentAsync(HttpMethod.Delete, $"/api/tenants/{tenantId}");
    }

    public async Task<PagedResult<InvoiceDto>> GetInvoicesAsync(Guid tenantId, int page = 1, int pageSize = 20, bool? isPaid = null)
    {
        var filter = isPaid is { } paid ? $"&isPaid={(paid ? "true" : "false")}" : string.Empty;
        return await SendAsync<PagedResult<InvoiceDto>>(
            HttpMethod.Get,
            $"/api/tenants/{tenantId}/invoices?page={page}&pageSize={pageSize}{filter}");
    }

    public async Task<InvoiceDto> GetInvoiceDetailsAsync(Guid tenantId, Guid invoiceId)
    {
        return await SendAsync<InvoiceDto>(
            HttpMethod.Get,
            $"/api/tenants/{tenantId}/invoices/{invoiceId}");
    }

    public async Task<TransferDetailsDto> GetTransferDetailsAsync(Guid tenantId, Guid invoiceId)
    {
        return await SendAsync<TransferDetailsDto>(
            HttpMethod.Get,
            $"/api/tenants/{tenantId}/invoices/{invoiceId}/transfer");
    }

    public async Task AddOrUpdateCredentialAsync(Guid tenantId, string token)
    {
        await SendNoContentAsync(HttpMethod.Post, $"/api/tenants/{tenantId}/credentials",
            new AddCredentialRequest { Token = token });
    }

    public async Task<SyncResultDto> ForceCredentialSyncAsync(Guid tenantId)
    {
        return await SendAsync<SyncResultDto>(HttpMethod.Post, $"/api/tenants/{tenantId}/credentials/sync");
    }

    public async Task RegisterDeviceTokenAsync(RegisterDeviceTokenRequest request, CancellationToken cancellationToken = default)
    {
        await SendNoContentAsync(HttpMethod.Post, "/api/devices/register", request, cancellationToken: cancellationToken);
    }

    public async Task<List<DeviceTokenDto>> GetDevicesAsync(string? accessToken = null, CancellationToken cancellationToken = default)
    {
        return await SendAsync<List<DeviceTokenDto>>(HttpMethod.Get, "/api/devices", accessToken: accessToken, cancellationToken: cancellationToken);
    }

    public async Task UnregisterDeviceAsync(Guid deviceId, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        await SendNoContentAsync(HttpMethod.Delete, $"/api/devices/{deviceId}", accessToken: accessToken, cancellationToken: cancellationToken);
    }

    public async Task<SellerProfileDto?> GetSellerProfileAsync(Guid tenantId)
    {
        try
        {
            return await SendAsync<SellerProfileDto>(HttpMethod.Get, $"/api/tenants/{tenantId}/seller-profile");
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<SellerProfileDto> UpdateSellerProfileAsync(Guid tenantId, SellerProfileRequest request)
    {
        return await SendAsync<SellerProfileDto>(HttpMethod.Put, $"/api/tenants/{tenantId}/seller-profile", request);
    }

    public async Task<NipLookupDto?> LookupNipAsync(string nip)
    {
        try
        {
            return await SendAsync<NipLookupDto>(HttpMethod.Get, $"/api/nip-lookup/{Uri.EscapeDataString(nip)}");
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<PagedResult<IssuedInvoiceListItemDto>> GetIssuedInvoicesAsync(Guid tenantId, int page = 1, int pageSize = 20, string? status = null, string? month = null, string? q = null)
    {
        var filters = string.Concat(
            new[] { ("status", status), ("month", month), ("q", q) }
                .Where(f => !string.IsNullOrEmpty(f.Item2))
                .Select(f => $"&{f.Item1}={Uri.EscapeDataString(f.Item2!)}"));
        return await SendAsync<PagedResult<IssuedInvoiceListItemDto>>(
            HttpMethod.Get,
            $"/api/tenants/{tenantId}/issued-invoices?page={page}&pageSize={pageSize}{filters}");
    }

    public async Task<IssuedInvoiceDto> GetIssuedInvoiceAsync(Guid tenantId, Guid invoiceId)
    {
        return await SendAsync<IssuedInvoiceDto>(HttpMethod.Get, $"/api/tenants/{tenantId}/issued-invoices/{invoiceId}");
    }

    public async Task<IssuedInvoiceDto> CreateIssuedInvoiceAsync(Guid tenantId, IssuedInvoiceDraftRequest request)
    {
        return await SendAsync<IssuedInvoiceDto>(HttpMethod.Post, $"/api/tenants/{tenantId}/issued-invoices", request);
    }

    public async Task<IssuedInvoiceDto> UpdateIssuedInvoiceAsync(Guid tenantId, Guid invoiceId, IssuedInvoiceDraftRequest request, uint? version = null)
    {
        return await SendAsync<IssuedInvoiceDto>(HttpMethod.Put, $"/api/tenants/{tenantId}/issued-invoices/{invoiceId}{VersionQuery(version)}", request);
    }

    public async Task DeleteIssuedInvoiceAsync(Guid tenantId, Guid invoiceId, uint? version = null)
    {
        await SendNoContentAsync(HttpMethod.Delete, $"/api/tenants/{tenantId}/issued-invoices/{invoiceId}{VersionQuery(version)}");
    }

    public async Task<IssuedInvoiceDto> IssueInvoiceAsync(Guid tenantId, Guid invoiceId, uint? version = null)
    {
        return await SendAsync<IssuedInvoiceDto>(HttpMethod.Post, $"/api/tenants/{tenantId}/issued-invoices/{invoiceId}/issue{VersionQuery(version)}");
    }

    public async Task<IssuedInvoiceDto> CopyIssuedInvoiceAsync(Guid tenantId, Guid invoiceId)
    {
        return await SendAsync<IssuedInvoiceDto>(HttpMethod.Post, $"/api/tenants/{tenantId}/issued-invoices/{invoiceId}/copy");
    }

    public async Task<byte[]> GetIssuedInvoiceXmlAsync(Guid tenantId, Guid invoiceId)
    {
        return await SendBytesAsync($"/api/tenants/{tenantId}/issued-invoices/{invoiceId}/xml");
    }

    public async Task<byte[]> GetIssuedInvoiceUpoAsync(Guid tenantId, Guid invoiceId)
    {
        return await SendBytesAsync($"/api/tenants/{tenantId}/issued-invoices/{invoiceId}/upo");
    }

    private static string VersionQuery(uint? version) => version is null ? string.Empty : $"?version={version}";

    private async Task<byte[]> SendBytesAsync(string path)
    {
        using var response = await ExecuteAsync(HttpMethod.Get, path);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        using var response = await ExecuteAsync(method, path, body, accessToken, cancellationToken);

        T? result;
        try
        {
            result = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new ApiException(response.StatusCode, "Odpowiedź serwera jest nieczytelna. Nie wiadomo, czy operacja się udała. Sprawdź aktualny stan, zanim spróbujesz ponownie.");
        }
        return result ?? throw new ApiException(response.StatusCode,
            "Otrzymano pusta odpowiedz z serwera.");
    }

    private async Task SendNoContentAsync(HttpMethod method, string path, object? body = null, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        using var response = await ExecuteAsync(method, path, body, accessToken, cancellationToken);
    }

    /// <summary>An explicit access token belongs to a signed-out session (deferred device cleanup): no refresh and no session check against the current one.</summary>
    private async Task<HttpResponseMessage> ExecuteAsync(HttpMethod method, string path, object? body = null, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        if (accessToken is not null)
        {
            response = await SendRequestAsync(method, path, body, accessToken, cancellationToken);
        }
        else
        {
            var session = _auth.SessionGeneration;
            var token = await _auth.GetAccessTokenAsync();
            ThrowIfSessionChanged(session);

            response = await SendRequestAsync(method, path, body, token, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                response = await RefreshAndRetryAsync(response, method, path, body, session, cancellationToken);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new ApiException(HttpStatusCode.Forbidden, "Brak uprawnien do tego zasobu.");

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            var rejected = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict;
            var ruleMessage = rejected || response.StatusCode == HttpStatusCode.ServiceUnavailable ? ParseRuleMessage(responseBody) : null;
            var validation = rejected ? ParseValidation(responseBody) : null;

            if (ruleMessage is null && validation is not { Errors.Count: > 0 })
                _logger.LogWarning("{Method} {Path} returned {Status}: {Body}", method, Redact(StripQuery(path)), (int)response.StatusCode, Redact(responseBody));

            throw new ApiException(response.StatusCode, ruleMessage ?? FixedMessage(response.StatusCode), validation);
        }

        return response;
    }

    private static string FixedMessage(HttpStatusCode status) => (int)status >= 500
        ? $"Błąd serwera ({(int)status}). Spróbuj ponownie później."
        : $"Serwer odrzucił żądanie ({(int)status}).";

    private static string StripQuery(string path) => path.Split('?')[0];

    private static string Redact(string text) => Sensitive.Replace(text.Length > 1000 ? text[..1000] : text, "[redacted]");

    private static string? ParseRuleMessage(string body)
    {
        try
        {
            var rule = JsonSerializer.Deserialize<RuleBody>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return rule?.Message ?? rule?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record RuleBody(string? Message, string? Error);

    private static ValidationEnvelope? ParseValidation(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<ValidationEnvelope>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<HttpResponseMessage> RefreshAndRetryAsync(
        HttpResponseMessage rejected, HttpMethod method, string path, object? body, int session, CancellationToken cancellationToken)
    {
        rejected.Dispose();
        ThrowIfSessionChanged(session);

        var token = await _auth.RefreshAccessTokenAsync();
        ThrowIfSessionChanged(session);

        if (token is null)
        {
            throw _auth.HasSession
                ? new ApiException(HttpStatusCode.ServiceUnavailable, NoConnection)
                : new ApiException(HttpStatusCode.Unauthorized, SessionExpired);
        }

        var retried = await SendRequestAsync(method, path, body, token, cancellationToken);
        if (retried.StatusCode != HttpStatusCode.Unauthorized)
            return retried;

        retried.Dispose();
        throw new ApiException(HttpStatusCode.Unauthorized, TokenRejected);
    }

    private void ThrowIfSessionChanged(int session)
    {
        if (_auth.SessionGeneration != session)
            throw new ApiException(HttpStatusCode.Unauthorized, SessionExpired);
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, string path, object? body, string? token, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(method, BuildRequestUri(path));
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _logger.LogWarning("{Method} {Path} failed: {Error}", method, Redact(StripQuery(path)), ex.GetType().Name);
            throw new ApiException(HttpStatusCode.ServiceUnavailable, NoConnection);
        }
    }

    private Uri BuildRequestUri(string path)
    {
        var serverRoot = _serverSettings.ServerUrl;
        return new Uri(new Uri($"{serverRoot}/"), path.TrimStart('/'));
    }
}
