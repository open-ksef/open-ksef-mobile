using NSubstitute;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class TenantsViewModelTests : IDisposable
{
    public void Dispose() => Preferences.Default.Clear();

    [Fact]
    public async Task DeleteTenant_ClearsOnlyThatTenantsCachedInvoices()
    {
        var deleted = new TenantDto { Id = Guid.NewGuid(), Nip = "1111111111" };
        var kept = Guid.NewGuid();
        var localDb = new LocalDbService();
        await localDb.SaveInvoicesAsync(deleted.Id, [new InvoiceDto { Id = Guid.NewGuid() }], localDb.Generation);
        await localDb.SaveInvoicesAsync(kept, [new InvoiceDto { Id = Guid.NewGuid() }], localDb.Generation);
        var api = Substitute.For<IApiService>();
        var vm = new TenantsViewModel(api, localDb) { Tenants = [deleted] };

        await vm.DeleteTenantCommand.ExecuteAsync(deleted);

        await api.Received(1).DeleteTenantAsync(deleted.Id);
        Assert.Empty(await localDb.GetInvoicesAsync(deleted.Id));
        Assert.Single(await localDb.GetInvoicesAsync(kept));
    }

    [Fact]
    public async Task DeleteTenant_Forbidden_KeepsTenant_ShowsServerMessage()
    {
        var tenant = new TenantDto { Id = Guid.NewGuid(), Nip = "1111111111" };
        var api = Substitute.For<IApiService>();
        api.DeleteTenantAsync(tenant.Id).Returns(Task.FromException(new ApiException(System.Net.HttpStatusCode.Forbidden, "Brak uprawnień do tej operacji w tej firmie.")));
        var vm = new TenantsViewModel(api, new LocalDbService()) { Tenants = [tenant] };

        await vm.DeleteTenantCommand.ExecuteAsync(tenant);

        Assert.Single(vm.Tenants);
        Assert.Equal("Brak uprawnień do tej operacji w tej firmie.", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    private static readonly string[] EveryRole = ["invoices.read", "invoices.markPaid", "sync.run", "notifications.self", "issuing.draft"];
    private static readonly string[] Managers = ["issuing.submit", "sync.fullResync", "credential.manage", "tenant.edit", "members.manage"];

    public static TheoryData<string?, string[]?, bool, bool> Roles => new()
    {
        { "Owner", [.. EveryRole, .. Managers, "tenant.delete"], true, true },
        { "Administrator", [.. EveryRole, .. Managers], true, true },
        { "Accountant", [.. EveryRole, "issuing.submit"], true, false },
        { "Employee", EveryRole, true, false },
        { null, null, true, true },
    };

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task OpenSellerProfile_ByRole_ReadableAndReadOnlyWithoutEdit(string? role, string[]? permissions, bool canRead, bool canEdit)
    {
        Shell.Current.Reset();
        var tenant = new TenantDto { Id = Guid.NewGuid(), Nip = "1111111111", Role = role, Permissions = permissions?.ToList() };
        var vm = new TenantsViewModel(Substitute.For<IApiService>(), new LocalDbService());

        await vm.OpenSellerProfileCommand.ExecuteAsync(tenant);

        Assert.Equal(canRead, tenant.CanReadSellerProfile);
        Assert.Equal(canEdit, tenant.CanEdit);
        Assert.Equal(canEdit ? $"sellerProfile?tenantId={tenant.Id}" : $"sellerProfile?tenantId={tenant.Id}&readOnly=true", Shell.Current.LastRoute);
    }

    [Fact]
    public void CanReadSellerProfile_WithoutInvoicesRead_False()
    {
        Assert.False(new TenantDto { Permissions = ["tenant.delete"] }.CanReadSellerProfile);
    }
}
