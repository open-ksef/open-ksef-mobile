using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OpenKSeF.Mobile.Models;

namespace OpenKSeF.Mobile.Services;

public class ApiService : IApiService
{
    private readonly HttpClient _http;
    private readonly IAuthService _auth;
    private readonly IServerSettingsService _serverSettings;

    public ApiService(HttpClient http, IAuthService auth, IServerSettingsService serverSettings)
    {
        _http = http;
        _http.DefaultRequestHeaders.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");
        _auth = auth;
        _serverSettings = serverSettings;
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

    public async Task<PagedResult<InvoiceDto>> GetInvoicesAsync(Guid tenantId, int page = 1, int pageSize = 20)
    {
        return await SendAsync<PagedResult<InvoiceDto>>(
            HttpMethod.Get,
            $"/api/tenants/{tenantId}/invoices?page={page}&pageSize={pageSize}");
    }

    public async Task<InvoiceDto> GetInvoiceDetailsAsync(Guid tenantId, Guid invoiceId)
    {
        return await SendAsync<InvoiceDto>(
            HttpMethod.Get,
            $"/api/tenants/{tenantId}/invoices/{invoiceId}");
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

    public async Task RegisterDeviceTokenAsync(RegisterDeviceTokenRequest request)
    {
        await SendNoContentAsync(HttpMethod.Post, "/api/devices/register", request);
    }

    public async Task<List<DeviceTokenDto>> GetDevicesAsync()
    {
        return await SendAsync<List<DeviceTokenDto>>(HttpMethod.Get, "/api/devices");
    }

    public async Task UnregisterDeviceAsync(Guid deviceId)
    {
        await SendNoContentAsync(HttpMethod.Delete, $"/api/devices/{deviceId}");
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

    public async Task<IssuedInvoiceDto> UpdateIssuedInvoiceAsync(Guid tenantId, Guid invoiceId, IssuedInvoiceDraftRequest request)
    {
        return await SendAsync<IssuedInvoiceDto>(HttpMethod.Put, $"/api/tenants/{tenantId}/issued-invoices/{invoiceId}", request);
    }

    public async Task DeleteIssuedInvoiceAsync(Guid tenantId, Guid invoiceId)
    {
        await SendNoContentAsync(HttpMethod.Delete, $"/api/tenants/{tenantId}/issued-invoices/{invoiceId}");
    }

    public async Task<IssuedInvoiceDto> IssueInvoiceAsync(Guid tenantId, Guid invoiceId)
    {
        return await SendAsync<IssuedInvoiceDto>(HttpMethod.Post, $"/api/tenants/{tenantId}/issued-invoices/{invoiceId}/issue");
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

    private async Task<byte[]> SendBytesAsync(string path)
    {
        using var response = await ExecuteAsync(HttpMethod.Get, path);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null)
    {
        using var response = await ExecuteAsync(method, path, body);

        var result = await response.Content.ReadFromJsonAsync<T>();
        return result ?? throw new ApiException(HttpStatusCode.InternalServerError,
            "Otrzymano pusta odpowiedz z serwera.");
    }

    private async Task SendNoContentAsync(HttpMethod method, string path, object? body = null)
    {
        using var response = await ExecuteAsync(method, path, body);
    }

    private async Task<HttpResponseMessage> ExecuteAsync(HttpMethod method, string path, object? body = null)
    {
        await SetAuthHeaderAsync();

        var response = await SendRequestAsync(method, path, body);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            response = await TryRefreshAndRetryAsync(response, method, path, body);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new ApiException(HttpStatusCode.Unauthorized, "Sesja wygasła. Zaloguj się ponownie.");

        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new ApiException(HttpStatusCode.Forbidden, "Brak uprawnien do tego zasobu.");

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            throw new ApiException(response.StatusCode, $"Blad serwera ({(int)response.StatusCode}): {responseBody}",
                response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity ? ParseValidation(responseBody) : null);
        }

        return response;
    }

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

    private async Task<HttpResponseMessage> TryRefreshAndRetryAsync(
        HttpResponseMessage originalResponse, HttpMethod method, string path, object? body)
    {
        var token = await _auth.GetAccessTokenAsync();
        if (token is null)
            return originalResponse;

        originalResponse.Dispose();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await SendRequestAsync(method, path, body);
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, BuildRequestUri(path));
        if (body is not null)
            request.Content = JsonContent.Create(body);

        try
        {
            return await _http.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(HttpStatusCode.ServiceUnavailable, $"Brak polaczenia z serwerem: {ex.Message}");
        }
    }

    private Uri BuildRequestUri(string path)
    {
        var serverRoot = _serverSettings.ServerUrl;
        return new Uri(new Uri($"{serverRoot}/"), path.TrimStart('/'));
    }

    private async Task SetAuthHeaderAsync()
    {
        var token = await _auth.GetAccessTokenAsync();
        if (token is not null)
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}
