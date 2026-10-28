using OpenKSeF.Mobile.Services;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using NSubstitute;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

public interface ITestAuthServiceForSetup
{
    Task<bool> RedeemSetupTokenAsync(string serverUrl, string setupToken);
}

public interface ITestServerSettingsForSetup
{
    string ServerUrl { get; }
    bool TryUpdateServerUrl(string url, out string normalizedUrl, out string? validationError);
    void MarkAsConfigured();
}

public interface ITestPostLoginNavigation
{
    Task NavigateAsync();
    Task SignOutAsync();
}

/// <summary>
/// Mirrors the core logic of ScanSetupQrViewModel.ProcessBarcodeAsync
/// without MAUI Shell dependency. Keep in sync with
/// src/OpenKSeF.Mobile/ViewModels/ScanSetupQrViewModel.cs.
/// </summary>
public partial class TestScanSetupQrViewModel : ObservableObject
{
    private readonly ITestServerSettingsForSetup _serverSettings;
    private readonly ITestAuthServiceForSetup _authService;
    private readonly ITestPostLoginNavigation _postLoginNav;
    private readonly Func<string, Task<bool>> _confirmSwitch;
    private bool _processed;

    [ObservableProperty]
    private string _statusText = "Skieruj kamerę na kod QR";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public string? NavigatedRoute { get; private set; }

    public TestScanSetupQrViewModel(
        ITestServerSettingsForSetup serverSettings,
        ITestAuthServiceForSetup authService,
        ITestPostLoginNavigation postLoginNav,
        Func<string, Task<bool>> confirmSwitch)
    {
        _serverSettings = serverSettings;
        _authService = authService;
        _postLoginNav = postLoginNav;
        _confirmSwitch = confirmSwitch;
    }

    public string? ConfirmMessage { get; private set; }

    public async Task ProcessBarcodeAsync(string rawValue)
    {
        if (_processed || IsBusy)
            return;

        _processed = true;
        IsBusy = true;
        StatusText = "Przetwarzanie kodu QR...";
        ErrorMessage = null;

        try
        {
            var payload = ParsePayload(rawValue);
            if (payload is null)
            {
                ErrorMessage = "Nieprawidłowy kod QR. Użyj kodu z portalu OpenKSeF.";
                _processed = false;
                return;
            }

            if (!ServerUrlRules.TryNormalize(payload.ServerUrl, ServerUrlRules.AllowDevCleartext, out var normalizedUrl, out var validationError))
            {
                ErrorMessage = validationError;
                _processed = false;
                return;
            }

            if (ServerUrlRules.IsDifferentServer(_serverSettings.ServerUrl, normalizedUrl))
            {
                ConfirmMessage = $"Przełączyć na serwer {ServerUrlRules.DisplayHost(normalizedUrl)}? Wpisuj hasło tylko, jeśli ufasz temu adresowi.";
                if (!await _confirmSwitch(ConfirmMessage))
                {
                    StatusText = "Serwer nie został zmieniony.";
                    NavigatedRoute = "//login";
                    return;
                }

                StatusText = "Przełączanie serwera...";
                await _postLoginNav.SignOutAsync();
            }

            if (!_serverSettings.TryUpdateServerUrl(normalizedUrl, out normalizedUrl, out validationError))
            {
                ErrorMessage = validationError;
                _processed = false;
                return;
            }

            if (!string.IsNullOrEmpty(payload.SetupToken))
            {
                var redeemed = await _authService.RedeemSetupTokenAsync(normalizedUrl, payload.SetupToken);
                if (redeemed)
                {
                    StatusText = "Zalogowano automatycznie!";
                    await _postLoginNav.NavigateAsync();
                    return;
                }

                ErrorMessage = "Token wygasł lub jest nieprawidłowy. Zaloguj się ręcznie.";
            }

            StatusText = $"Serwer ustawiony: {normalizedUrl}";
            NavigatedRoute = "//login";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Błąd: {ex.Message}";
            _processed = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static TestQrSetupPayloadForVm? ParsePayload(string rawValue)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<TestQrSetupPayloadForVm>(rawValue);
            return payload?.IsValid == true ? payload : null;
        }
        catch
        {
            return null;
        }
    }
}

internal class TestQrSetupPayloadForVm
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("serverUrl")]
    public string ServerUrl { get; set; } = "";

    [JsonPropertyName("setupToken")]
    public string? SetupToken { get; set; }

    public bool IsValid =>
        Type == "openksef-setup" && Version >= 1 && !string.IsNullOrWhiteSpace(ServerUrl);
}

public class ScanSetupQrViewModelTests
{
    private readonly ITestServerSettingsForSetup _serverSettings;
    private readonly ITestAuthServiceForSetup _authService;
    private readonly ITestPostLoginNavigation _postLoginNav;

    public ScanSetupQrViewModelTests()
    {
        _serverSettings = Substitute.For<ITestServerSettingsForSetup>();
        _authService = Substitute.For<ITestAuthServiceForSetup>();
        _postLoginNav = Substitute.For<ITestPostLoginNavigation>();

        _serverSettings.ServerUrl.Returns("https://example.com");
        ConfigureDefaultServerSettings("https://example.com");
    }

    private bool _confirmAnswer = true;

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

    private TestScanSetupQrViewModel CreateVm() =>
        new(_serverSettings, _authService, _postLoginNav, _ => Task.FromResult(_confirmAnswer));

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

        Assert.Equal("//login", vm.NavigatedRoute);
        Assert.Contains("Token wygasł", vm.ErrorMessage);
    }

    [Fact]
    public async Task ProcessBarcode_NoSetupToken_SetsServerAndNavigatesToLogin()
    {
        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com"));

        Assert.Equal("//login", vm.NavigatedRoute);
        Assert.Contains("Serwer ustawiony", vm.StatusText);
        await _authService.DidNotReceive().RedeemSetupTokenAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ProcessBarcode_InvalidJson_ShowsError()
    {
        var vm = CreateVm();
        await vm.ProcessBarcodeAsync("not-json-at-all");

        Assert.Null(vm.NavigatedRoute);
        Assert.Contains("Nieprawidłowy kod QR", vm.ErrorMessage);
    }

    [Fact]
    public async Task ProcessBarcode_ServerValidationFails_ShowsError()
    {
        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("bad-url"));

        Assert.Null(vm.NavigatedRoute);
        Assert.Contains("poprawny adres URL", vm.ErrorMessage);
        _serverSettings.DidNotReceiveWithAnyArgs().TryUpdateServerUrl(default!, out _, out _);
    }

    [Fact]
    public async Task ProcessBarcode_DifferentServer_Rejected_LeavesServerUnchanged()
    {
        _confirmAnswer = false;

        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://rogue.example.org", "token"));

        Assert.Contains("rogue.example.org", vm.ConfirmMessage);
        Assert.Equal("//login", vm.NavigatedRoute);
        _serverSettings.DidNotReceiveWithAnyArgs().TryUpdateServerUrl(default!, out _, out _);
        await _postLoginNav.DidNotReceive().SignOutAsync();
        await _authService.DidNotReceiveWithAnyArgs().RedeemSetupTokenAsync(default!, default!);
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
        Assert.Equal("//login", vm.NavigatedRoute);
    }

    [Fact]
    public async Task ProcessBarcode_SameServer_NoConfirmation()
    {
        _confirmAnswer = false;

        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://EXAMPLE.com/"));

        Assert.Null(vm.ConfirmMessage);
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

    [Fact]
    public async Task ProcessBarcode_AutoLogin_DoesNotCallPostLoginNav_WhenRedeemFails()
    {
        _authService.RedeemSetupTokenAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);

        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com", "expired-token"));

        await _postLoginNav.DidNotReceive().NavigateAsync();
    }
}
