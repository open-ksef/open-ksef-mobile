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
    private readonly IPostLoginNavigationService _postLoginNav;

    public ScanSetupQrViewModelTests()
    {
        Shell.Current.Reset();
        _serverSettings = Substitute.For<IServerSettingsService>();
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

    private ScanSetupQrViewModel CreateVm() => new(_serverSettings, _postLoginNav);

    private static string BuildQrPayload(string serverUrl, string? setupToken = null) =>
        JsonSerializer.Serialize(new
        {
            type = "openksef-setup",
            version = 1,
            serverUrl,
            setupToken
        });

    [Fact]
    public async Task ProcessBarcode_StaleSetupToken_SetsServerWithoutApiCallAndNavigatesToLogin()
    {
        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com", "stale-token"));

        _serverSettings.Received(1).TryUpdateServerUrl("https://example.com", out Arg.Any<string>(), out Arg.Any<string?>());
        Assert.Equal("//login", Shell.Current.LastRoute);
        Assert.Contains("Serwer ustawiony", vm.StatusText);
        Assert.Null(vm.ErrorMessage);
        Assert.Empty(_postLoginNav.ReceivedCalls());
        Assert.Equal(
            new[] { typeof(IServerSettingsService), typeof(IPostLoginNavigationService) },
            typeof(ScanSetupQrViewModel).GetConstructors().Single().GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public async Task ProcessBarcode_NoSetupToken_SetsServerAndNavigatesToLogin()
    {
        var vm = CreateVm();
        await vm.ProcessBarcodeAsync(BuildQrPayload("https://example.com"));

        Assert.Equal("//login", Shell.Current.LastRoute);
        Assert.Contains("Serwer ustawiony", vm.StatusText);
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
        ConfigureDefaultServerSettings("https://other.example.org");
        _postLoginNav.SignOutAsync().Returns(_ => Task.Delay(100));

        var vm = CreateVm();
        var first = vm.ProcessBarcodeAsync(BuildQrPayload("https://other.example.org"));
        var second = vm.ProcessBarcodeAsync(BuildQrPayload("https://other.example.org"));

        await Task.WhenAll(first, second);

        await _postLoginNav.Received(1).SignOutAsync();
    }

    [Fact]
    public void CanPickFromGallery_OnlyOnAndroid()
    {
        try
        {
            DeviceInfo.Platform = DevicePlatform.iOS;
            Assert.False(CreateVm().CanPickFromGallery);
            DeviceInfo.Platform = DevicePlatform.Android;
            Assert.True(CreateVm().CanPickFromGallery);
        }
        finally
        {
            DeviceInfo.Platform = DevicePlatform.Android;
        }
    }

    [Fact]
    public async Task PickFromGallery_StreamWithoutLength_ReadsWholeImage()
    {
        var stream = new UnknownLengthStream(new byte[10_000]);
        FilePicker.Default.Result = new FileResult(() => stream);
        try
        {
            var vm = CreateVm();
            await vm.PickFromGalleryCommand.ExecuteAsync(null);

            Assert.Equal("Nie udało się odczytać kodu QR z obrazu.", vm.ErrorMessage);
            Assert.Equal(10_000, stream.BytesRead);
        }
        finally
        {
            FilePicker.Default.Result = null;
        }
    }

    private sealed class UnknownLengthStream(byte[] data) : Stream
    {
        private int _position;

        public int BytesRead => _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(Math.Min(count, 1024), data.Length - _position);
            Array.Copy(data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
