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
        NotificationHubService.TakeTapped();
        Shell.Current.Reset();
    }

    public void Dispose()
    {
        Preferences.Default.Clear();
        NotificationHubService.TakeTapped();
    }

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
        Assert.Equal("Błąd serwera (500). Spróbuj ponownie później.", vm.ErrorMessage);
        await vm.LoadMoreCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
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

    private static readonly Guid OtherTenantId = Guid.Parse("2b9e4f61-8c3d-4e7a-a1b2-c3d4e5f60718");

    private void OwnsTenants() => _api.GetTenantsAsync().Returns(
    [
        new TenantDto { Id = TenantId, Nip = "1111111111" },
        new TenantDto { Id = OtherTenantId, Nip = "1111111112" }
    ]);

    private void OtherTenantHas(string vendor) => _api.GetInvoicesAsync(OtherTenantId, 1).Returns(
        new PagedResult<InvoiceDto> { Items = [new InvoiceDto { Id = Guid.NewGuid(), VendorName = vendor }], PageSize = 20, TotalCount = 1 });

    [Fact]
    public async Task SummaryTap_OwnedTenant_OpensThatTenantsList()
    {
        OwnsTenants();
        OtherTenantHas("Other");
        Assert.True(NotificationHubService.OnNotificationTapped("newInvoicesSummary", OtherTenantId.ToString()));
        var vm = new InvoiceListViewModel(_api, new LocalDbService());

        await vm.LoadInvoicesCommand.ExecuteAsync(null);

        Assert.Equal(["Other"], vm.Invoices.Select(i => i.VendorName));
        Assert.Equal("1111111112", vm.TenantNip);
        Assert.Equal(OtherTenantId.ToString(), Preferences.Default.Get("SelectedTenantId", ""));
        Assert.False(NotificationHubService.HasTappedTenant);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("4xx")]
    [InlineData("5xx")]
    [InlineData("timeout")]
    [InlineData("network")]
    [InlineData("cancel")]
    public async Task SummaryTap_TenantsCallFailsOrTenantNotOwned_KeepsCurrentTenant(string outcome)
    {
        _api.GetTenantsAsync().Returns(outcome == "success"
            ? Task.FromResult(new List<TenantDto> { new() { Id = TenantId, Nip = "1111111111" } })
            : Outcome(outcome).ContinueWith(t => { t.Wait(); return new List<TenantDto>(); }));
        NotificationHubService.OnNotificationTapped("newInvoicesSummary", OtherTenantId.ToString());
        var vm = new InvoiceListViewModel(_api, new LocalDbService());

        await vm.LoadInvoicesCommand.ExecuteAsync(null);

        Assert.Equal(["Test"], vm.Invoices.Select(i => i.VendorName));
        Assert.Equal(TenantId.ToString(), Preferences.Default.Get("SelectedTenantId", ""));
        await _api.DidNotReceive().GetInvoicesAsync(OtherTenantId, Arg.Any<int>());
    }

    [Fact]
    public async Task SummaryTap_DuringLoad_ReloadsForTappedTenant()
    {
        OwnsTenants();
        OtherTenantHas("Other");
        var first = new TaskCompletionSource();
        _api.GetInvoicesAsync(TenantId, 1).Returns(async _ => { await first.Task; return Page(); });
        var vm = new InvoiceListViewModel(_api, new LocalDbService());

        var load = vm.LoadInvoicesCommand.ExecuteAsync(null);
        NotificationHubService.OnNotificationTapped("newInvoicesSummary", OtherTenantId.ToString());
        await vm.LoadInvoicesCommand.ExecuteAsync(null);
        first.SetResult();
        await load;

        Assert.Equal(["Other"], vm.Invoices.Select(i => i.VendorName));
        Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "7d1c3a52-1f7e-4a59-9b0c-2f3d4e5a6b7c")]
    [InlineData("newInvoicesSummary", null)]
    [InlineData("newInvoicesSummary", "not-a-guid")]
    [InlineData("newInvoice", "7d1c3a52-1f7e-4a59-9b0c-2f3d4e5a6b7c")]
    public void NotificationTap_NotASummaryWithTenant_Ignored(string? type, string? tenantId)
    {
        Assert.False(NotificationHubService.OnNotificationTapped(type, tenantId));
        Assert.False(NotificationHubService.HasTappedTenant);
    }

    private static readonly Guid TappedInvoiceId = Guid.Parse("5c2f8e13-9a4b-4d6c-8e7f-0a1b2c3d4e5f");

    [Theory]
    [InlineData(null, "7d1c3a52-1f7e-4a59-9b0c-2f3d4e5a6b7c", "not-a-guid")]
    [InlineData(null, "not-a-guid", "5c2f8e13-9a4b-4d6c-8e7f-0a1b2c3d4e5f")]
    [InlineData(null, null, "5c2f8e13-9a4b-4d6c-8e7f-0a1b2c3d4e5f")]
    public void InvoiceTap_WithoutValidTenantAndInvoice_Ignored(string? type, string? tenantId, string? invoiceId)
    {
        Assert.False(NotificationHubService.OnNotificationTapped(type, tenantId, invoiceId));
        Assert.False(NotificationHubService.HasTappedTenant);
    }

    [Fact]
    public void SummaryTap_WithInvoiceId_OpensListOnly()
    {
        Assert.True(NotificationHubService.OnNotificationTapped("newInvoicesSummary", TenantId.ToString(), TappedInvoiceId.ToString()));
        Assert.Equal((TenantId, (Guid?)null), NotificationHubService.TakeTapped());
    }

    [Fact]
    public async Task InvoiceTap_OwnedTenant_OpensInvoiceDetails()
    {
        OwnsTenants();
        OtherTenantHas("Other");
        _api.GetInvoiceDetailsAsync(OtherTenantId, TappedInvoiceId).Returns(new InvoiceDto { Id = TappedInvoiceId });
        Assert.True(NotificationHubService.OnNotificationTapped(null, OtherTenantId.ToString(), TappedInvoiceId.ToString()));
        var vm = new InvoiceListViewModel(_api, new LocalDbService());

        await vm.LoadInvoicesCommand.ExecuteAsync(null);

        Assert.Equal($"invoiceDetails?tenantId={OtherTenantId}&invoiceId={TappedInvoiceId}", Shell.Current.LastRoute);
        Assert.Equal(OtherTenantId.ToString(), Preferences.Default.Get("SelectedTenantId", ""));
        Assert.Empty(Shell.Current.Alerts);
        Assert.False(NotificationHubService.HasTappedTenant);
    }

    [Fact]
    public async Task InvoiceTap_SelectedTenant_OpensDetailsWithoutTenantsCall()
    {
        _api.GetInvoiceDetailsAsync(TenantId, TappedInvoiceId).Returns(new InvoiceDto { Id = TappedInvoiceId });
        NotificationHubService.OnNotificationTapped(null, TenantId.ToString(), TappedInvoiceId.ToString());
        var vm = new InvoiceListViewModel(_api, new LocalDbService());

        await vm.LoadInvoicesCommand.ExecuteAsync(null);

        Assert.Equal($"invoiceDetails?tenantId={TenantId}&invoiceId={TappedInvoiceId}", Shell.Current.LastRoute);
        await _api.DidNotReceive().GetTenantsAsync();
    }

    [Theory]
    [InlineData("404")]
    [InlineData("5xx")]
    [InlineData("timeout")]
    [InlineData("network")]
    [InlineData("cancel")]
    public async Task InvoiceTap_InvoiceUnavailable_StaysOnListWithMessage(string outcome)
    {
        _api.GetInvoiceDetailsAsync(TenantId, TappedInvoiceId).Returns(outcome == "404"
            ? Task.FromException<InvoiceDto>(new ApiException(System.Net.HttpStatusCode.NotFound, "404"))
            : Outcome(outcome).ContinueWith(t => { t.Wait(); return new InvoiceDto(); }));
        NotificationHubService.OnNotificationTapped(null, TenantId.ToString(), TappedInvoiceId.ToString());
        var vm = new InvoiceListViewModel(_api, new LocalDbService());

        await vm.LoadInvoicesCommand.ExecuteAsync(null);

        Assert.Equal(["Test"], vm.Invoices.Select(i => i.VendorName));
        Assert.Null(Shell.Current.LastRoute);
        Assert.Equal("Faktura niedostępna", Shell.Current.LastAlertTitle);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("4xx")]
    [InlineData("5xx")]
    [InlineData("timeout")]
    [InlineData("network")]
    [InlineData("cancel")]
    public async Task InvoiceTap_TenantNotOwnedOrTenantsCallFails_StaysOnCurrentListWithMessage(string outcome)
    {
        _api.GetTenantsAsync().Returns(outcome == "success"
            ? Task.FromResult(new List<TenantDto> { new() { Id = TenantId, Nip = "1111111111" } })
            : Outcome(outcome).ContinueWith(t => { t.Wait(); return new List<TenantDto>(); }));
        NotificationHubService.OnNotificationTapped(null, OtherTenantId.ToString(), TappedInvoiceId.ToString());
        var vm = new InvoiceListViewModel(_api, new LocalDbService());

        await vm.LoadInvoicesCommand.ExecuteAsync(null);

        Assert.Equal(["Test"], vm.Invoices.Select(i => i.VendorName));
        Assert.Equal(TenantId.ToString(), Preferences.Default.Get("SelectedTenantId", ""));
        Assert.Null(Shell.Current.LastRoute);
        Assert.Equal("Faktura niedostępna", Shell.Current.LastAlertTitle);
        await _api.DidNotReceive().GetInvoiceDetailsAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    private static Task<InvoiceDto> DetailsOutcome(string outcome) => outcome switch
    {
        "403" => Task.FromException<InvoiceDto>(new ApiException(System.Net.HttpStatusCode.Forbidden, "Faktura 1111111111")),
        "404" => Task.FromException<InvoiceDto>(new ApiException(System.Net.HttpStatusCode.NotFound, "Faktura 1111111111")),
        "5xx" => Task.FromException<InvoiceDto>(new ApiException(System.Net.HttpStatusCode.InternalServerError, "Faktura 1111111111")),
        "timeout" => Task.FromException<InvoiceDto>(new TaskCanceledException("Faktura 1111111111", new TimeoutException())),
        "network" => Task.FromException<InvoiceDto>(new HttpRequestException("Faktura 1111111111")),
        _ => Task.FromCanceled<InvoiceDto>(new CancellationToken(true))
    };

    private sealed class TraceCapture : System.Diagnostics.TraceListener
    {
        public List<string> Lines { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (message?.Contains("invoice pre-check") == true)
                lock (Lines) Lines.Add(message);
        }
    }

    [Theory]
    [InlineData("403", "HTTP 403")]
    [InlineData("404", "HTTP 404")]
    [InlineData("5xx", "HTTP 500")]
    [InlineData("timeout", "TaskCanceledException")]
    [InlineData("network", "HttpRequestException")]
    [InlineData("cancel", "TaskCanceledException")]
    public async Task InvoiceTap_PreCheckFails_LogsStatusOrExceptionTypeWithoutIds(string outcome, string reason)
    {
        _api.GetInvoiceDetailsAsync(TenantId, TappedInvoiceId).Returns(DetailsOutcome(outcome));
        NotificationHubService.OnNotificationTapped(null, TenantId.ToString(), TappedInvoiceId.ToString());
        var vm = new InvoiceListViewModel(_api, new LocalDbService());
        var capture = new TraceCapture();
        System.Diagnostics.Trace.Listeners.Add(capture);
        try
        {
            await vm.LoadInvoicesCommand.ExecuteAsync(null);
        }
        finally
        {
            System.Diagnostics.Trace.Listeners.Remove(capture);
        }

        Assert.Equal([$"[OpenKSeF] Notification tap invoice pre-check failed: {reason}"], capture.Lines);
        Assert.Equal("Faktura niedostępna", Shell.Current.LastAlertTitle);
    }

    [Theory]
    [InlineData("403")]
    [InlineData("404")]
    [InlineData("5xx")]
    [InlineData("timeout")]
    [InlineData("network")]
    [InlineData("cancel")]
    public async Task InvoiceTap_OwnedTenantInvoiceUnavailable_KeepsSelectedTenant(string outcome)
    {
        OwnsTenants();
        OtherTenantHas("Other");
        _api.GetInvoiceDetailsAsync(OtherTenantId, TappedInvoiceId).Returns(DetailsOutcome(outcome));
        NotificationHubService.OnNotificationTapped(null, OtherTenantId.ToString(), TappedInvoiceId.ToString());
        var vm = new InvoiceListViewModel(_api, new LocalDbService());

        await vm.LoadInvoicesCommand.ExecuteAsync(null);

        Assert.Equal(TenantId.ToString(), Preferences.Default.Get("SelectedTenantId", ""));
        Assert.Equal("1111111111", Preferences.Default.Get("SelectedTenantNip", ""));
        Assert.Equal(["Test"], vm.Invoices.Select(i => i.VendorName));
        await _api.DidNotReceive().GetInvoicesAsync(OtherTenantId, Arg.Any<int>());
        Assert.Null(Shell.Current.LastRoute);
        Assert.Equal("Faktura niedostępna", Shell.Current.LastAlertTitle);
    }

    [Fact]
    public async Task InvoiceTap_NewerTapDuringLoad_OpensOnlyTheNewerInvoice()
    {
        var newer = Guid.NewGuid();
        _api.GetInvoiceDetailsAsync(TenantId, Arg.Any<Guid>()).Returns(ci => new InvoiceDto { Id = ci.ArgAt<Guid>(1) });
        var first = new TaskCompletionSource();
        _api.GetInvoicesAsync(TenantId, 1).Returns(async _ => { await first.Task; return Page(); }, _ => Task.FromResult(Page()));
        NotificationHubService.OnNotificationTapped(null, TenantId.ToString(), TappedInvoiceId.ToString());
        var vm = new InvoiceListViewModel(_api, new LocalDbService());

        var load = vm.LoadInvoicesCommand.ExecuteAsync(null);
        NotificationHubService.OnNotificationTapped(null, TenantId.ToString(), newer.ToString());
        first.SetResult();
        await load;

        Assert.Equal([$"invoiceDetails?tenantId={TenantId}&invoiceId={newer}"], Shell.Current.Routes);
    }

    private static Task<PagedResult<InvoiceDto>> Outcome(string outcome) => outcome switch
    {
        "success" => Task.FromResult(Page()),
        "4xx" => Task.FromException<PagedResult<InvoiceDto>>(new ApiException(System.Net.HttpStatusCode.BadRequest, "4xx")),
        "5xx" => Task.FromException<PagedResult<InvoiceDto>>(new ApiException(System.Net.HttpStatusCode.InternalServerError, "5xx")),
        "timeout" => Task.FromException<PagedResult<InvoiceDto>>(new TaskCanceledException("timeout", new TimeoutException())),
        "network" => Task.FromException<PagedResult<InvoiceDto>>(new HttpRequestException("reset")),
        _ => Task.FromCanceled<PagedResult<InvoiceDto>>(new CancellationToken(true))
    };

    [Theory]
    [InlineData("success")]
    [InlineData("4xx")]
    [InlineData("5xx")]
    [InlineData("timeout")]
    [InlineData("network")]
    [InlineData("cancel")]
    public async Task LoadMore_TenantSwitchedDuringAwait_DiscardsStaleResult(string outcome)
    {
        var vm = await LoadedVm();
        var stale = new TaskCompletionSource();
        _api.GetInvoicesAsync(TenantId, 2).Returns(async _ => { await stale.Task; return await Outcome(outcome); });
        var other = new InvoiceDto { Id = Guid.NewGuid(), VendorName = "Other" };
        _api.GetInvoicesAsync(OtherTenantId, 1).Returns(new PagedResult<InvoiceDto> { Items = [other], PageSize = 20, TotalCount = 60 });

        var loadMore = vm.LoadMoreCommand.ExecuteAsync(null);
        Preferences.Default.Set("SelectedTenantId", OtherTenantId.ToString());
        await vm.LoadInvoicesCommand.ExecuteAsync(null);
        stale.SetResult();
        await loadMore;

        Assert.Equal([other.Id], vm.Invoices.Select(i => i.Id));
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsLoadingMore);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("5xx")]
    [InlineData("network")]
    public async Task LoadMore_RefreshDuringAwait_DiscardsStaleResultAndKeepsPage(string outcome)
    {
        var vm = await LoadedVm();
        var stale = new TaskCompletionSource();
        _api.GetInvoicesAsync(TenantId, 2).Returns(async _ => { await stale.Task; return await Outcome(outcome); }, _ => Task.FromResult(Page()));

        var loadMore = vm.LoadMoreCommand.ExecuteAsync(null);
        await vm.RefreshCommand.ExecuteAsync(null);
        stale.SetResult();
        await loadMore;

        Assert.Single(vm.Invoices);
        Assert.Null(vm.ErrorMessage);

        await vm.LoadMoreCommand.ExecuteAsync(null);
        await _api.Received(2).GetInvoicesAsync(TenantId, 2);
        Assert.Equal(2, vm.Invoices.Count);
    }

    [Fact]
    public async Task LoadInvoices_TenantCleared_ShowsOnlyNoTenantBlock()
    {
        _api.GetInvoicesAsync(TenantId, 1).Returns(new PagedResult<InvoiceDto> { Items = [], PageSize = 20, TotalCount = 0 });
        var vm = new InvoiceListViewModel(_api, new LocalDbService());
        await vm.LoadInvoicesCommand.ExecuteAsync(null);
        Assert.True(vm.IsEmpty);

        Preferences.Default.Remove("SelectedTenantId");
        await vm.LoadInvoicesCommand.ExecuteAsync(null);

        Assert.True(vm.IsNoTenantSelected);
        Assert.False(vm.IsEmpty);
    }
}
