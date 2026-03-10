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
    bool TryUpdateServerUrl(string url, out string normalizedUrl, out string? validationError);
    void MarkAsConfigured();
}

public interface ITestApiServiceForSetup
{
    Task<TestOnboardingStatus> GetOnboardingStatusAsync();
}

public class TestOnboardingStatus
{
    public bool IsComplete { get; set; }
}

/// <summary>
/// Mirrors the core logic of ScanSetupQrViewModel.ProcessBarcodeAsync
/// without MAUI Shell dependency. Tests the EXPECTED behavior after fix:
/// onboarding check + device registration after successful auto-login.
/// </summary>
public partial class TestScanSetupQrViewModel : ObservableObject
{
    private readonly ITestServerSettingsForSetup _serverSettings;
    private readonly ITestAuthServiceForSetup _authService;
    private readonly ITestApiServiceForSetup _apiService;
    private readonly ITestDeviceTokenService _deviceTokenService;
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
        ITestApiServiceForSetup apiService,
        ITestDeviceTokenService deviceTokenService)
    {
        _serverSettings = serverSettings;
        _authService = authService;
        _apiService = apiService;
        _deviceTokenService = deviceTokenService;
    }

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

            if (!_serverSettings.TryUpdateServerUrl(payload.ServerUrl, out var normalizedUrl, out var validationError))
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
                    await NavigateAfterAutoLogin();
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

    private async Task NavigateAfterAutoLogin()
    {
        bool needsOnboarding = false;

        try
        {
            var status = await _apiService.GetOnboardingStatusAsync();
            needsOnboarding = !status.IsComplete;
        }
        catch
        {
        }

        if (!needsOnboarding)
        {
            try { await _deviceTokenService.EnsureDeviceRegisteredAsync(); } catch { }
        }

        NavigatedRoute = needsOnboarding ? "//onboarding" : "//main/invoices";
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
    private readonly ITestApiServiceForSetup _apiService;
    private readonly ITestDeviceTokenService _deviceTokenService;

    public ScanSetupQrViewModelTests()
    {
        _serverSettings = Substitute.For<ITestServerSettingsForSetup>();
        _authService = Substitute.For<ITestAuthServiceForSetup>();
        _apiService = Substitute.For<ITestApiServiceForSetup>();
        _deviceTokenService = Substitute.For<ITestDeviceTokenService>();

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

    private TestScanSetupQrViewModel CreateVm() =>
        new(_serverSettings, _authService, _apiService, _deviceTokenService);

    private static string BuildQrPayload(string serverUrl, string? setupToken = null) =>
        JsonSerializer.Serialize(new
        {
            type = "openksef-setup",
            version = 1,
            serverUrl,
            setupToken
        });

    [Fact]
    public async Task ProcessBarcode_AutoLogin_OnboardingComplete_NavigatesToInvoices()
    {
        _authService.RedeemSetupTokenAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);
        _apiService.GetOnboardingStatusAsync()
            .Returns(new TestOnboardingStatus { IsComplete = true });

        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com", "valid-token"));

        Assert.Equal("//main/invoices", vm.NavigatedRoute);
        Assert.Equal("Zalogowano automatycznie!", vm.StatusText);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task ProcessBarcode_AutoLogin_OnboardingNeeded_NavigatesToOnboarding()
    {
        _authService.RedeemSetupTokenAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);
        _apiService.GetOnboardingStatusAsync()
            .Returns(new TestOnboardingStatus { IsComplete = false });

        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com", "valid-token"));

        Assert.Equal("//onboarding", vm.NavigatedRoute);
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
        _serverSettings.TryUpdateServerUrl(
            Arg.Any<string>(), out Arg.Any<string>(), out Arg.Any<string?>())
            .Returns(x =>
            {
                x[1] = "";
                x[2] = "Nieprawidłowy adres serwera.";
                return false;
            });

        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("bad-url"));

        Assert.Null(vm.NavigatedRoute);
        Assert.Equal("Nieprawidłowy adres serwera.", vm.ErrorMessage);
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
        _apiService.GetOnboardingStatusAsync()
            .Returns(new TestOnboardingStatus { IsComplete = true });

        var vm = CreateVm();
        var first = vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com", "token1"));
        var second = vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com", "token2"));

        await Task.WhenAll(first, second);

        await _authService.Received(1).RedeemSetupTokenAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ProcessBarcode_AutoLogin_OnboardingComplete_RegistersDevice()
    {
        _authService.RedeemSetupTokenAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);
        _apiService.GetOnboardingStatusAsync()
            .Returns(new TestOnboardingStatus { IsComplete = true });

        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com", "valid-token"));

        await _deviceTokenService.Received(1).EnsureDeviceRegisteredAsync();
    }

    [Fact]
    public async Task ProcessBarcode_AutoLogin_OnboardingNeeded_SkipsDeviceRegistration()
    {
        _authService.RedeemSetupTokenAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);
        _apiService.GetOnboardingStatusAsync()
            .Returns(new TestOnboardingStatus { IsComplete = false });

        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com", "valid-token"));

        await _deviceTokenService.DidNotReceive().EnsureDeviceRegisteredAsync();
    }

    [Fact]
    public async Task ProcessBarcode_AutoLogin_DeviceRegistrationFails_StillNavigates()
    {
        _authService.RedeemSetupTokenAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);
        _apiService.GetOnboardingStatusAsync()
            .Returns(new TestOnboardingStatus { IsComplete = true });
        _deviceTokenService.EnsureDeviceRegisteredAsync()
            .Returns<Task>(_ => throw new Exception("Network error"));

        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com", "valid-token"));

        Assert.Equal("//main/invoices", vm.NavigatedRoute);
    }
}
