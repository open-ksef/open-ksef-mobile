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
        await localDb.SaveInvoicesAsync(deleted.Id, [new InvoiceDto { Id = Guid.NewGuid() }]);
        await localDb.SaveInvoicesAsync(kept, [new InvoiceDto { Id = Guid.NewGuid() }]);
        var api = Substitute.For<IApiService>();
        var vm = new TenantsViewModel(api, localDb) { Tenants = [deleted] };

        await vm.DeleteTenantCommand.ExecuteAsync(deleted);

        await api.Received(1).DeleteTenantAsync(deleted.Id);
        Assert.Empty(await localDb.GetInvoicesAsync(deleted.Id));
        Assert.Single(await localDb.GetInvoicesAsync(kept));
    }
}
