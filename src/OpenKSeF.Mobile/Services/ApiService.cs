using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null)
    {
        var response = await ExecuteAsync(method, path, body);

        var result = await response.Content.ReadFromJsonAsync<T>();
        return result ?? throw new ApiException(HttpStatusCode.InternalServerError,
            "Otrzymano pusta odpowiedz z serwera.");
    }

    private async Task SendNoContentAsync(HttpMethod method, string path, object? body = null)
    {
        await ExecuteAsync(method, path, body);
    }

    private async Task<HttpResponseMessage> ExecuteAsync(HttpMethod method, string path, object? body = null)
    {
        await SetAuthHeaderAsync();

        var request = new HttpRequestMessage(method, BuildRequestUri(path));
        if (body is not null)
            request.Content = JsonContent.Create(body);

        HttpResponseMessage response;

        try
        {
            response = await _http.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(HttpStatusCode.ServiceUnavailable,
                $"Brak polaczenia z serwerem: {ex.Message}");
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var token = await _auth.GetAccessTokenAsync();
            if (token is not null)
            {
                request = new HttpRequestMessage(method, BuildRequestUri(path));
                if (body is not null)
                    request.Content = JsonContent.Create(body);

                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                try
                {
                    response = await _http.SendAsync(request);
                }
                catch (HttpRequestException ex)
                {
                    throw new ApiException(HttpStatusCode.ServiceUnavailable,
                        $"Brak polaczenia z serwerem: {ex.Message}");
                }
            }
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new ApiException(HttpStatusCode.Unauthorized,
                "Sesja wygasła. Zaloguj się ponownie.");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new ApiException(HttpStatusCode.Forbidden,
                "Brak uprawnien do tego zasobu.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            throw new ApiException(response.StatusCode,
                $"Blad serwera ({(int)response.StatusCode}): {responseBody}");
        }

        return response;
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
