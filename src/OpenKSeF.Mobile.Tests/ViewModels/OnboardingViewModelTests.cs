using NSubstitute;
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
        var vm = new OnboardingViewModel(_api, Substitute.For<IDeviceTokenService>()) { Nip = nip };

        await vm.SubmitStep1Command.ExecuteAsync(null);

        Assert.Equal("Niepoprawny NIP. Wpisz 10 cyfr z poprawną sumą kontrolną.", vm.ErrorMessage);
        Assert.Equal(1, vm.CurrentStep);
        await _api.DidNotReceiveWithAnyArgs().CreateTenantAsync(default!);
    }

    [Fact]
    public async Task SubmitStep1_ValidNip_CreatesTenant()
    {
        _api.CreateTenantAsync(Arg.Any<CreateTenantRequest>()).Returns(new TenantDto { Id = Guid.NewGuid(), Nip = "1111111111" });
        var vm = new OnboardingViewModel(_api, Substitute.For<IDeviceTokenService>()) { Nip = " 1111111111 " };

        await vm.SubmitStep1Command.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(2, vm.CurrentStep);
        await _api.Received(1).CreateTenantAsync(Arg.Is<CreateTenantRequest>(r => r.Nip == "1111111111"));
    }

    [Fact]
    public async Task OpenTokenDocs_OpensSiteTokenPage()
    {
        var vm = new OnboardingViewModel(_api, Substitute.For<IDeviceTokenService>());

        await vm.OpenTokenDocsCommand.ExecuteAsync(null);

        Assert.Equal("https://open-ksef.pl/docs/token-ksef", Browser.Default.LastUri);
    }
}
