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
    Task UnregisterDeviceAsync(Guid deviceId);
    Task<SellerProfileDto?> GetSellerProfileAsync(Guid tenantId);
    Task<SellerProfileDto> UpdateSellerProfileAsync(Guid tenantId, SellerProfileRequest request);
    Task<NipLookupDto?> LookupNipAsync(string nip);
    Task<PagedResult<IssuedInvoiceListItemDto>> GetIssuedInvoicesAsync(Guid tenantId, int page = 1, int pageSize = 20, string? status = null, string? month = null, string? q = null);
    Task<IssuedInvoiceDto> GetIssuedInvoiceAsync(Guid tenantId, Guid invoiceId);
    Task<IssuedInvoiceDto> CreateIssuedInvoiceAsync(Guid tenantId, IssuedInvoiceDraftRequest request);
    Task<IssuedInvoiceDto> UpdateIssuedInvoiceAsync(Guid tenantId, Guid invoiceId, IssuedInvoiceDraftRequest request);
    Task DeleteIssuedInvoiceAsync(Guid tenantId, Guid invoiceId);
    Task<IssuedInvoiceDto> IssueInvoiceAsync(Guid tenantId, Guid invoiceId);
    Task<IssuedInvoiceDto> CopyIssuedInvoiceAsync(Guid tenantId, Guid invoiceId);
    Task<byte[]> GetIssuedInvoiceXmlAsync(Guid tenantId, Guid invoiceId);
    Task<byte[]> GetIssuedInvoiceUpoAsync(Guid tenantId, Guid invoiceId);
}
