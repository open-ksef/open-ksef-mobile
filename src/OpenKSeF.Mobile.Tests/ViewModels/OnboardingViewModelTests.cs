using System.Net;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class OnboardingViewModelTests
{
    private readonly IApiService _api = Substitute.For<IApiService>();

    [Theory]
    [InlineData("1111111112")]
    [InlineData("1234567890")]
    public async Task SubmitStep1_InvalidNipChecksum_RejectsWithoutApiCall(string nip)
    {
        var vm = new OnboardingViewModel(_api, Substitute.For<IPostLoginNavigationService>()) { Nip = nip };

        await vm.SubmitStep1Command.ExecuteAsync(null);

        Assert.Equal("Niepoprawny NIP. Wpisz 10 cyfr z poprawną sumą kontrolną.", vm.ErrorMessage);
        Assert.Equal(1, vm.CurrentStep);
        await _api.DidNotReceiveWithAnyArgs().CreateTenantAsync(default!);
    }

    [Fact]
    public async Task SubmitStep1_ValidNip_CreatesTenant()
    {
        _api.CreateTenantAsync(Arg.Any<CreateTenantRequest>()).Returns(new TenantDto { Id = Guid.NewGuid(), Nip = "1111111111" });
        var vm = new OnboardingViewModel(_api, Substitute.For<IPostLoginNavigationService>()) { Nip = " 1111111111 " };

        await vm.SubmitStep1Command.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(2, vm.CurrentStep);
        await _api.Received(1).CreateTenantAsync(Arg.Is<CreateTenantRequest>(r => r.Nip == "1111111111"));
    }

    [Fact]
    public async Task SubmitStep1_NoCredentialPermission_SkipsTokenStep()
    {
        _api.CreateTenantAsync(Arg.Any<CreateTenantRequest>()).Returns(new TenantDto { Id = Guid.NewGuid(), Nip = "1111111111", Role = "Employee", Permissions = ["invoices.read"] });
        var vm = new OnboardingViewModel(_api, Substitute.For<IPostLoginNavigationService>()) { Nip = "1111111111" };

        await vm.SubmitStep1Command.ExecuteAsync(null);

        Assert.Equal(3, vm.CurrentStep);
        Assert.True(vm.TokenSkipped);
    }

    [Fact]
    public async Task GoToInvoices_AfterTenantCreated_StoresTenantIdAndNip()
    {
        var tenantId = Guid.NewGuid();
        Preferences.Default.Set("SelectedTenantNip", "stale");
        _api.CreateTenantAsync(Arg.Any<CreateTenantRequest>()).Returns(new TenantDto { Id = tenantId, Nip = "1111111111" });
        var vm = new OnboardingViewModel(_api, Substitute.For<IPostLoginNavigationService>()) { Nip = "1111111111" };
        await vm.SubmitStep1Command.ExecuteAsync(null);

        await vm.GoToInvoicesCommand.ExecuteAsync(null);

        Assert.Equal(tenantId.ToString(), Preferences.Default.Get("SelectedTenantId", ""));
        Assert.Equal("1111111111", Preferences.Default.Get("SelectedTenantNip", ""));
    }

    [Fact]
    public async Task SubmitStep2_TokenRejected_ShowsFieldErrorAndStays()
    {
        _api.CreateTenantAsync(Arg.Any<CreateTenantRequest>()).Returns(new TenantDto { Id = Guid.NewGuid(), Nip = "1111111111" });
        _api.AddOrUpdateCredentialAsync(Arg.Any<Guid>(), Arg.Any<string>()).ThrowsAsync(new ApiException(HttpStatusCode.BadRequest, "Token is required."));
        var vm = new OnboardingViewModel(_api, Substitute.For<IPostLoginNavigationService>()) { Nip = "1111111111" };
        await vm.SubmitStep1Command.ExecuteAsync(null);
        vm.KsefToken = "bad-token";

        await vm.SubmitStep2Command.ExecuteAsync(null);

        Assert.Equal("KSeF nie przyjął tokenu. Sprawdź, czy skopiowano go w całości i czy nie wygasł.", vm.TokenError);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(2, vm.CurrentStep);

        vm.KsefToken = "bad-token2";

        Assert.Null(vm.TokenError);
    }

    [Fact]
    public async Task SubmitStep2_SyncRejectsToken_BackToTokenStepWithFieldError()
    {
        _api.CreateTenantAsync(Arg.Any<CreateTenantRequest>()).Returns(new TenantDto { Id = Guid.NewGuid(), Nip = "1111111111" });
        _api.ForceCredentialSyncAsync(Arg.Any<Guid>()).ThrowsAsync(new ApiException(HttpStatusCode.BadGateway, "KSeF odrzucił token (nieprawidłowy lub unieważniony). Wygeneruj nowy token i podmień go w ustawieniach firmy."));
        var vm = new OnboardingViewModel(_api, Substitute.For<IPostLoginNavigationService>()) { Nip = "1111111111" };
        await vm.SubmitStep1Command.ExecuteAsync(null);
        vm.KsefToken = "expired-token";

        await vm.SubmitStep2Command.ExecuteAsync(null);

        Assert.Equal(2, vm.CurrentStep);
        Assert.Equal("KSeF nie przyjął tokenu. Sprawdź, czy skopiowano go w całości i czy nie wygasł.", vm.TokenError);
        Assert.Null(vm.SyncStatusText);
    }

    [Fact]
    public async Task SubmitStep2_ServerError_KeepsBannerNotFieldError()
    {
        _api.CreateTenantAsync(Arg.Any<CreateTenantRequest>()).Returns(new TenantDto { Id = Guid.NewGuid(), Nip = "1111111111" });
        _api.AddOrUpdateCredentialAsync(Arg.Any<Guid>(), Arg.Any<string>()).ThrowsAsync(new ApiException(HttpStatusCode.InternalServerError, "Błąd serwera"));
        var vm = new OnboardingViewModel(_api, Substitute.For<IPostLoginNavigationService>()) { Nip = "1111111111" };
        await vm.SubmitStep1Command.ExecuteAsync(null);
        vm.KsefToken = "token";

        await vm.SubmitStep2Command.ExecuteAsync(null);

        Assert.Null(vm.TokenError);
        Assert.Equal("Błąd zapisywania tokenu: Błąd serwera", vm.ErrorMessage);
    }

    [Fact]
    public async Task OpenTokenDocs_OpensSiteTokenPage()
    {
        var vm = new OnboardingViewModel(_api, Substitute.For<IPostLoginNavigationService>());

        await vm.OpenTokenDocsCommand.ExecuteAsync(null);

        Assert.Equal("https://open-ksef.pl/docs/token-ksef", Browser.Default.LastUri);
    }
}
