using OpenKSeF.Mobile.Models;

namespace OpenKSeF.Mobile.Services;

public interface IApiService
{
    Task<UserInfo> GetMeAsync();
    Task<OnboardingStatusDto> GetOnboardingStatusAsync();
    Task<List<TenantDto>> GetTenantsAsync();
    Task<TenantDto> CreateTenantAsync(CreateTenantRequest request);
    Task<TenantDto> UpdateTenantAsync(Guid tenantId, UpdateTenantRequest request);
    Task DeleteTenantAsync(Guid tenantId);
    Task<PagedResult<InvoiceDto>> GetInvoicesAsync(Guid tenantId, int page = 1, int pageSize = 20);
    Task<InvoiceDto> GetInvoiceDetailsAsync(Guid tenantId, Guid invoiceId);
    Task AddOrUpdateCredentialAsync(Guid tenantId, string token);
    Task<SyncResultDto> ForceCredentialSyncAsync(Guid tenantId);
    Task RegisterDeviceTokenAsync(RegisterDeviceTokenRequest request);
    Task<List<DeviceTokenDto>> GetDevicesAsync();
}
