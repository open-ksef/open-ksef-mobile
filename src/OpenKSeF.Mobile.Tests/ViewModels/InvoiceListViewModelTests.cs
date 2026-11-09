using System.Text.Json;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class InvoiceListViewModelTests : IDisposable
{
    private static readonly Guid TenantId = Guid.Parse("7d1c3a52-1f7e-4a59-9b0c-2f3d4e5a6b7c");
    private readonly IApiService _api = Substitute.For<IApiService>();

    public InvoiceListViewModelTests()
    {
        Preferences.Default.Clear();
        Preferences.Default.Set("SelectedTenantId", TenantId.ToString());
        Preferences.Default.Set("SelectedTenantNip", "1111111111");
        _api.GetInvoicesAsync(TenantId, 1).Returns(Page());
    }

    public void Dispose() => Preferences.Default.Clear();

    private static PagedResult<InvoiceDto> Page() => new()
    {
        Items = [new InvoiceDto { Id = Guid.NewGuid(), VendorName = "Test" }],
        PageSize = 20,
        TotalCount = 60
    };

    private async Task<InvoiceListViewModel> LoadedVm()
    {
        var vm = new InvoiceListViewModel(_api, new LocalDbService());
        await vm.LoadInvoicesCommand.ExecuteAsync(null);
        Assert.True(vm.HasMorePages);
        return vm;
    }

    [Fact]
    public async Task LoadMore_UnexpectedException_ShowsErrorAndRetriesSamePage()
    {
        var vm = await LoadedVm();
        _api.GetInvoicesAsync(TenantId, 2).Returns(
            _ => throw new JsonException("bad page"),
            _ => Page());

        await vm.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal("Nie udało się załadować faktur: bad page", vm.ErrorMessage);
        Assert.Single(vm.Invoices);

        await vm.LoadMoreCommand.ExecuteAsync(null);

        await _api.Received(2).GetInvoicesAsync(TenantId, 2);
        await _api.DidNotReceive().GetInvoicesAsync(TenantId, 3);
        Assert.Equal(2, vm.Invoices.Count);
    }

    [Fact]
    public async Task LoadMore_ServerError_RetriesSamePage()
    {
        var vm = await LoadedVm();
        _api.GetInvoicesAsync(TenantId, 2).Returns(
            _ => throw new ApiException(System.Net.HttpStatusCode.InternalServerError, "Błąd serwera (500). Spróbuj ponownie później."),
            _ => Page());

        await vm.LoadMoreCommand.ExecuteAsync(null);
        await vm.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal("Błąd serwera (500). Spróbuj ponownie później.", vm.ErrorMessage);
        await _api.Received(2).GetInvoicesAsync(TenantId, 2);
        Assert.Equal(2, vm.Invoices.Count);
    }

    [Fact]
    public async Task LoadMore_NoTenantSelected_DoesNotAdvancePage()
    {
        var vm = await LoadedVm();
        _api.GetInvoicesAsync(TenantId, 2).Returns(Page());

        Preferences.Default.Remove("SelectedTenantId");
        await vm.LoadMoreCommand.ExecuteAsync(null);
        Preferences.Default.Set("SelectedTenantId", TenantId.ToString());
        await vm.LoadMoreCommand.ExecuteAsync(null);

        await _api.Received(1).GetInvoicesAsync(TenantId, 2);
        await _api.DidNotReceive().GetInvoicesAsync(TenantId, 3);
    }
}
