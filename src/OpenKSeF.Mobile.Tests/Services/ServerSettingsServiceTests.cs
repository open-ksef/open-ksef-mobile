using System.Net;
using System.Text;
using NSubstitute;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;
using OidcBrowser = Duende.IdentityModel.OidcClient.Browser.IBrowser;

namespace OpenKSeF.Mobile.Tests.Services;

[Collection("Shell")]
public class ServerSettingsServiceTests
{
    private const string ServerUrlKey = "server_url";
    private const string EmptyServerMessage = "Adres serwera nie może być pusty.";
    private const string StoredServer = "https://dead.example.pl";
    private const string UnreachableMessage = "Nie można połączyć się z serwerem dead.example.pl. Sprawdź adres serwera i połączenie z siecią.";

    private readonly RecordingHandler _handler = new();
    private readonly IPostLoginNavigationService _postLoginNav = Substitute.For<IPostLoginNavigationService>();

    public ServerSettingsServiceTests()
    {
        Shell.Current.Reset();
        Preferences.Default.Clear();
        SecureStorage.Default.Reset();
    }

    private AuthService CreateAuthService(IServerSettingsService settings) => new(
        new AuthOptions { ClientId = "openksef-mobile", RedirectUri = "openksef://auth/callback", PostLogoutRedirectUri = "openksef://auth/logout" },
        Substitute.For<OidcBrowser>(),
        settings,
        new HttpClient(_handler));

    private LoginViewModel CreateLoginViewModel(IAuthService auth, IServerSettingsService settings) => new(auth, settings, _postLoginNav, Substitute.For<IApiService>());

    private static Task RunAsync(LoginViewModel vm, string action) => action switch
    {
        "login" => vm.LoginCommand.ExecuteAsync(null),
        "google" => vm.LoginWithGoogleCommand.ExecuteAsync(null),
        "pair" => vm.PairCommand.ExecuteAsync(null),
        _ => vm.RegisterCommand.ExecuteAsync(null)
    };

    [Fact]
    public void FreshInstall_HasNoServerAndIsNotConfigured()
    {
        var settings = new ServerSettingsService();

        Assert.Equal(string.Empty, settings.ServerUrl);
        Assert.False(settings.IsConfigured);
    }

    [Theory]
    [InlineData("https://faktury.example.pl")]
    [InlineData("https://demo.open-ksef.pl")]
    public void StoredServer_IsKeptAndConfigured(string stored)
    {
        Preferences.Default.Set(ServerUrlKey, stored);

        var settings = new ServerSettingsService();

        Assert.Equal(stored, settings.ServerUrl);
        Assert.True(settings.IsConfigured);
        Assert.Equal(stored, Preferences.Default.Get(ServerUrlKey, string.Empty));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://faktury.example.pl")]
    [InlineData("https://user:secret@faktury.example.pl")]
    [InlineData("http://faktury.example.pl")]
    [InlineData("")]
    public void InvalidStoredServer_NoAddress_FieldVisible(string stored)
    {
        Preferences.Default.Set(ServerUrlKey, stored);

        var settings = new ServerSettingsService();
        var vm = CreateLoginViewModel(Substitute.For<IAuthService>(), settings);

        Assert.Equal(string.Empty, settings.ServerUrl);
        Assert.False(settings.IsConfigured);
        Assert.True(vm.IsServerVisible);
        Assert.Equal(string.Empty, vm.ServerUrl);
    }

    [Fact]
    public void StoredServer_IsNormalized()
    {
        Preferences.Default.Set(ServerUrlKey, " https://Faktury.Example.pl:443/ ");

        var settings = new ServerSettingsService();

        Assert.Equal("https://faktury.example.pl", settings.ServerUrl);
        Assert.True(settings.IsConfigured);
    }

    [Fact]
    public async Task FreshInstall_AuthServiceStartsAndRestoresNothingWithoutNetwork()
    {
        var auth = CreateAuthService(new ServerSettingsService());

        Assert.Equal(SessionRestoreResult.None, await auth.RestoreSessionAsync());
        Assert.Null(await auth.GetAccessTokenAsync());
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task FreshInstall_LoginUsesTheServerEnteredInTheField()
    {
        var settings = new ServerSettingsService();
        var vm = CreateLoginViewModel(CreateAuthService(settings), settings);
        vm.ServerUrl = "https://faktury.example.pl";

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal("https://faktury.example.pl/auth/realms/openksef/.well-known/openid-configuration", _handler.Requests.First().RequestUri!.ToString());
        Assert.Equal("https://faktury.example.pl", Preferences.Default.Get(ServerUrlKey, string.Empty));
    }

    [Fact]
    public void FreshInstall_LoginFormShowsAnEmptyServerField()
    {
        var vm = CreateLoginViewModel(Substitute.For<IAuthService>(), new ServerSettingsService());

        Assert.True(vm.IsServerVisible);
        Assert.Equal(string.Empty, vm.ServerUrl);
    }

    [Theory]
    [InlineData("login")]
    [InlineData("google")]
    [InlineData("register")]
    [InlineData("pair")]
    public async Task EmptyServer_ShowsMessageAndStoresNothing(string action)
    {
        var settings = new ServerSettingsService();
        var auth = Substitute.For<IAuthService>();
        var vm = CreateLoginViewModel(auth, settings);

        await RunAsync(vm, action);

        Assert.Equal(EmptyServerMessage, vm.ErrorMessage);
        Assert.Empty(auth.ReceivedCalls());
        Assert.False(Preferences.Default.ContainsKey(ServerUrlKey));
        Assert.False(settings.IsConfigured);
    }

    [Fact]
    public async Task FreshInstall_SkippingQrKeepsTheServerFieldVisible()
    {
        var settings = new ServerSettingsService();

        await new ScanSetupQrViewModel(settings, _postLoginNav).SkipCommand.ExecuteAsync(null);

        Assert.True(new LoginViewModel(Substitute.For<IAuthService>(), settings, _postLoginNav, Substitute.For<IApiService>()).IsServerVisible);
    }

    private (LoginViewModel Vm, ServerSettingsService Settings) StoredServerLogin()
    {
        Preferences.Default.Set(ServerUrlKey, StoredServer);
        var settings = new ServerSettingsService();
        var vm = CreateLoginViewModel(CreateAuthService(settings), settings);
        Assert.False(vm.IsServerVisible);
        return (vm, settings);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    public static TheoryData<Func<HttpRequestMessage, Task<HttpResponseMessage>>> ServerProblems => new()
    {
        _ => throw new HttpRequestException("connection refused"),
        _ => throw new HttpRequestException("ssl", new System.Security.Authentication.AuthenticationException("certificate")),
        _ => throw new IOException("java.net.ssl handshake"),
        _ => throw new TaskCanceledException("timeout"),
        _ => Task.FromResult(Response(HttpStatusCode.NotFound, string.Empty)),
        _ => Task.FromResult(Response(HttpStatusCode.NotFound, "<html>404</html>", "text/html")),
        _ => Task.FromResult(Response(HttpStatusCode.BadGateway, "<html>502</html>", "text/html")),
        _ => Task.FromResult(Response(HttpStatusCode.Unauthorized, "<html>proxy</html>", "text/html")),
        _ => Task.FromResult(Response(HttpStatusCode.OK, "<html>portal</html>", "text/html")),
        _ => Task.FromResult(Response(HttpStatusCode.OK, "{}")),
    };

    [Theory]
    [MemberData(nameof(ServerProblems))]
    public async Task Login_ServerProblem_NamesTheServerAndRevealsTheField(Func<HttpRequestMessage, Task<HttpResponseMessage>> problem)
    {
        var (vm, _) = StoredServerLogin();
        _handler.Respond = problem;

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal(UnreachableMessage, vm.ErrorMessage);
        Assert.True(vm.IsServerVisible);
        Assert.Equal(StoredServer, Preferences.Default.Get(ServerUrlKey, string.Empty));
        Assert.Equal(0, SecureStorage.Default.Count);
        await _postLoginNav.DidNotReceive().NavigateAsync();
    }

    [Theory]
    [InlineData(AuthResult.ServerUnreachable, UnreachableMessage, true)]
    [InlineData(AuthResult.Rejected, "Logowanie przez Google nie powiodło się.", false)]
    public async Task Google_Failure_ShowsMessage_RevealsFieldOnlyWhenUnreachable(AuthResult result, string message, bool fieldVisible)
    {
        Preferences.Default.Set(ServerUrlKey, StoredServer);
        var auth = Substitute.For<IAuthService>();
        auth.LoginWithGoogleAsync().Returns(result);
        var vm = CreateLoginViewModel(auth, new ServerSettingsService());

        await vm.LoginWithGoogleCommand.ExecuteAsync(null);

        Assert.Equal(message, vm.ErrorMessage);
        Assert.Equal(fieldVisible, vm.IsServerVisible);
    }

    [Fact]
    public async Task HiddenDraft_IsDiscarded_LoginGoesToTheStoredServer()
    {
        var (vm, _) = StoredServerLogin();
        vm.ToggleServerCommand.Execute(null);
        vm.ServerUrl = "https://draft.example.pl";

        vm.ToggleServerCommand.Execute(null);

        Assert.Equal(StoredServer, vm.ServerUrl);

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal("dead.example.pl", _handler.Requests.First().RequestUri!.Host);
        Assert.Equal(StoredServer, Preferences.Default.Get(ServerUrlKey, string.Empty));
    }

    [Fact]
    public async Task VisibleDraft_IsUsedByLogin()
    {
        var (vm, _) = StoredServerLogin();
        vm.ToggleServerCommand.Execute(null);
        vm.ServerUrl = "https://draft.example.pl";

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal("draft.example.pl", _handler.Requests.First().RequestUri!.Host);
    }

    [Fact]
    public async Task HiddenEmptyField_ValidationErrorRevealsTheField()
    {
        var vm = CreateLoginViewModel(Substitute.For<IAuthService>(), new ServerSettingsService());
        vm.ToggleServerCommand.Execute(null);
        Assert.False(vm.IsServerVisible);

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.True(vm.IsServerVisible);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; } = _ => Task.FromResult(Response(HttpStatusCode.Unauthorized, "{}"));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Respond(request);
        }
    }
}
