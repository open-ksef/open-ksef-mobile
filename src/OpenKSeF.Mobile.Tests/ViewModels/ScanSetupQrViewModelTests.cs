using System.Text.Json;
using NSubstitute;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class ScanSetupQrViewModelTests
{
    private readonly IServerSettingsService _serverSettings;
    private readonly IAuthService _authService;
    private readonly IPostLoginNavigationService _postLoginNav;

    public ScanSetupQrViewModelTests()
    {
        Shell.Current.Reset();
        _serverSettings = Substitute.For<IServerSettingsService>();
        _authService = Substitute.For<IAuthService>();
        _postLoginNav = Substitute.For<IPostLoginNavigationService>();

        _serverSettings.ServerUrl.Returns("https://example.com");
        ConfigureDefaultServerSettings("https://example.com");
    }

    private void ConfigureDefaultServerSettings(string normalizedUrl)
    {
        _serverSettings.TryUpdateServerUrl(
            Arg.Any<string>(), out Arg.Any<string>(), out Arg.Any<string?>())
            .Returns(x =>
            {
                x[1] = normalizedUrl;
                x[2] = (string?)null;
                return true;
            });
    }

    private ScanSetupQrViewModel CreateVm() => new(_serverSettings, _authService, _postLoginNav);

    private static string BuildQrPayload(string serverUrl, string? setupToken = null) =>
        JsonSerializer.Serialize(new
        {
            type = "openksef-setup",
            version = 1,
            serverUrl,
            setupToken
        });

    [Fact]
    public async Task ProcessBarcode_AutoLogin_CallsPostLoginNavigation()
    {
        _authService.RedeemSetupTokenAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);

        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com", "valid-token"));

        await _postLoginNav.Received(1).NavigateAsync();
        Assert.Equal("Zalogowano automatycznie!", vm.StatusText);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task ProcessBarcode_RedeemFails_ShowsErrorAndNavigatesToLogin()
    {
        _authService.RedeemSetupTokenAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);

        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com", "expired-token"));

        Assert.Equal("//login", Shell.Current.LastRoute);
        Assert.Contains("Token wygasł", vm.ErrorMessage);
        await _postLoginNav.DidNotReceive().NavigateAsync();
    }

    [Fact]
    public async Task ProcessBarcode_NoSetupToken_SetsServerAndNavigatesToLogin()
    {
        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com"));

        Assert.Equal("//login", Shell.Current.LastRoute);
        Assert.Contains("Serwer ustawiony", vm.StatusText);
        await _authService.DidNotReceive().RedeemSetupTokenAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ProcessBarcode_InvalidJson_ShowsError()
    {
        var vm = CreateVm();
        await vm.ProcessBarcodeAsync("not-json-at-all");

        Assert.Null(Shell.Current.LastRoute);
        Assert.Contains("Nieprawidłowy kod QR", vm.ErrorMessage);
    }

    [Fact]
    public async Task ProcessBarcode_ServerValidationFails_ShowsError()
    {
        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("bad-url"));

        Assert.Null(Shell.Current.LastRoute);
        Assert.Contains("poprawny adres URL", vm.ErrorMessage);
        _serverSettings.DidNotReceiveWithAnyArgs().TryUpdateServerUrl(default!, out _, out _);
    }

    [Fact]
    public async Task ProcessBarcode_UrlWithUserInfo_Rejected()
    {
        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com@rogue.example.org"));

        Assert.NotNull(vm.ErrorMessage);
        Assert.Null(Shell.Current.LastAlertMessage);
        _serverSettings.DidNotReceiveWithAnyArgs().TryUpdateServerUrl(default!, out _, out _);
    }

    [Fact]
    public async Task ProcessBarcode_DifferentServer_Rejected_LeavesServerUnchanged()
    {
        Shell.Current.AlertAnswer = false;

        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://rogue.example.org", "token"));

        Assert.Contains("rogue.example.org", Shell.Current.LastAlertMessage);
        Assert.Equal("//login", Shell.Current.LastRoute);
        Assert.Equal("Serwer nie został zmieniony.", vm.StatusText);
        _serverSettings.DidNotReceiveWithAnyArgs().TryUpdateServerUrl(default!, out _, out _);
        await _postLoginNav.DidNotReceive().SignOutAsync();
        await _authService.DidNotReceiveWithAnyArgs().RedeemSetupTokenAsync(default!, default!);
    }

    [Fact]
    public async Task ProcessBarcode_DifferentServer_ConfirmationShowsPunycodeHost()
    {
        Shell.Current.AlertAnswer = false;

        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://pаypal.example/"));

        Assert.Contains("xn--", Shell.Current.LastAlertMessage);
        Assert.DoesNotContain("pаypal", Shell.Current.LastAlertMessage);
    }

    [Fact]
    public async Task ProcessBarcode_DifferentServer_Confirmed_SignsOutThenSwitches()
    {
        ConfigureDefaultServerSettings("https://other.example.org");

        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://other.example.org/"));

        Received.InOrder(() =>
        {
            _postLoginNav.SignOutAsync();
            _serverSettings.TryUpdateServerUrl("https://other.example.org", out Arg.Any<string>(), out Arg.Any<string?>());
        });
        Assert.Equal("//login", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task ProcessBarcode_SameServer_NoConfirmation()
    {
        Shell.Current.AlertAnswer = false;

        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://EXAMPLE.com:443/"));

        Assert.Null(Shell.Current.LastAlertMessage);
        await _postLoginNav.DidNotReceive().SignOutAsync();
        Assert.Contains("Serwer ustawiony", vm.StatusText);
    }

    [Fact]
    public async Task ProcessBarcode_DuplicateCall_IgnoredWhileBusy()
    {
        _authService.RedeemSetupTokenAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(async _ =>
            {
                await Task.Delay(100);
                return true;
            });

        var vm = CreateVm();
        var first = vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com", "token1"));
        var second = vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com", "token2"));

        await Task.WhenAll(first, second);

        await _authService.Received(1).RedeemSetupTokenAsync(Arg.Any<string>(), Arg.Any<string>());
    }
}
