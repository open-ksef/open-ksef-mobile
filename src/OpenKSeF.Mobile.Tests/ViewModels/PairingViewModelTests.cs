using NSubstitute;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class PairingViewModelTests
{
    private readonly IAuthService _auth = Substitute.For<IAuthService>();
    private readonly IServerSettingsService _settings = Substitute.For<IServerSettingsService>();
    private readonly IPostLoginNavigationService _postLoginNav = Substitute.For<IPostLoginNavigationService>();
    private readonly ManualClock _clock = new();
    private readonly DevicePairing _pairing;

    public PairingViewModelTests()
    {
        Shell.Current.Reset();
        _settings.ServerUrl.Returns("https://ksef.example.com");
        _pairing = new DevicePairing("WDJB-MJHT", _clock.GetUtcNow().AddSeconds(300), "dev-1", "verifier", TimeSpan.FromSeconds(5));
        _auth.StartPairingAsync(Arg.Any<CancellationToken>()).Returns((AuthResult.Success, _pairing));
    }

    private PairingViewModel CreateVm() => new(_auth, _settings, _postLoginNav) { Clock = _clock };

    [Fact]
    public async Task WhilePolling_ShowsCodeAndCountdown()
    {
        var approve = new TaskCompletionSource<AuthResult>();
        _auth.CompletePairingAsync(_pairing, Arg.Any<CancellationToken>()).Returns(approve.Task);
        _clock.Advance(TimeSpan.FromSeconds(1));
        var vm = CreateVm();

        var run = vm.StartAsync();

        Assert.Equal("WDJB-MJHT", vm.UserCode);
        Assert.Equal("Kod ważny jeszcze 4:59", vm.CountdownText);
        Assert.True(vm.IsBusy);
        Assert.False(vm.CanRequestNewCode);

        approve.SetResult(AuthResult.Success);
        await run;

        await _postLoginNav.Received(1).NavigateAsync();
        Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData(AuthResult.PairingExpired, "Kod wygasł. Poproś o nowy kod.", true)]
    [InlineData(AuthResult.PairingDenied, "Odrzucono parowanie w portalu.", true)]
    [InlineData(AuthResult.PairingNotSupported, "Ten serwer nie obsługuje parowania kodem. Wróć i zaloguj się przyciskiem Zaloguj się.", false)]
    [InlineData(AuthResult.ClientRejected, "Serwer ksef.example.com odrzucił aplikację mobilną. Skontaktuj się z administratorem serwera.", false)]
    [InlineData(AuthResult.ServerUnreachable, "Nie można połączyć się z serwerem ksef.example.com. Sprawdź połączenie z siecią.", true)]
    [InlineData(AuthResult.Rejected, "Parowanie nie powiodło się.", true)]
    public async Task PollFailure_HidesCode_ShowsPolishMessage(AuthResult outcome, string message, bool newCode)
    {
        _auth.CompletePairingAsync(_pairing, Arg.Any<CancellationToken>()).Returns(outcome);
        var vm = CreateVm();

        await vm.StartAsync();

        Assert.Equal(message, vm.ErrorMessage);
        Assert.Equal(newCode, vm.CanRequestNewCode);
        Assert.Null(vm.UserCode);
        Assert.Null(vm.CountdownText);
        Assert.False(vm.IsBusy);
        await _postLoginNav.DidNotReceive().NavigateAsync();
    }

    [Fact]
    public async Task StartFailure_ServerWithoutDeviceGrant_SaysNotSupported_NoPoll()
    {
        _auth.StartPairingAsync(Arg.Any<CancellationToken>()).Returns((AuthResult.PairingNotSupported, (DevicePairing?)null));
        var vm = CreateVm();

        await vm.StartAsync();

        Assert.Equal("Ten serwer nie obsługuje parowania kodem. Wróć i zaloguj się przyciskiem Zaloguj się.", vm.ErrorMessage);
        Assert.False(vm.CanRequestNewCode);
        await _auth.DidNotReceiveWithAnyArgs().CompletePairingAsync(default!, default);
    }

    [Fact]
    public async Task NewCode_AfterExpiry_StartsAgainAndClearsError()
    {
        _auth.CompletePairingAsync(_pairing, Arg.Any<CancellationToken>()).Returns(AuthResult.PairingExpired, AuthResult.Success);
        var vm = CreateVm();
        await vm.StartAsync();

        await vm.NewCodeCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        await _auth.Received(2).StartPairingAsync(Arg.Any<CancellationToken>());
        await _postLoginNav.Received(1).NavigateAsync();
    }

    [Fact]
    public async Task Stop_CancelsPolling_NoMessageNoNavigation()
    {
        CancellationToken seen = default;
        _auth.CompletePairingAsync(_pairing, Arg.Any<CancellationToken>()).Returns(call =>
        {
            seen = call.Arg<CancellationToken>();
            return Task.Delay(Timeout.Infinite, seen).ContinueWith(_ => AuthResult.Success, TaskContinuationOptions.OnlyOnRanToCompletion);
        });
        var vm = CreateVm();

        var run = vm.StartAsync();
        vm.Stop();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(seen.IsCancellationRequested);
        Assert.Null(vm.ErrorMessage);
        await _postLoginNav.DidNotReceive().NavigateAsync();
    }

    [Fact]
    public async Task Cancel_StopsAndGoesBack()
    {
        var vm = CreateVm();

        await vm.CancelCommand.ExecuteAsync(null);

        Assert.Equal("..", Shell.Current.LastRoute);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
