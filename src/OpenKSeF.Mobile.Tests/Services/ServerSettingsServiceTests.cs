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

    private LoginViewModel CreateLoginViewModel(IAuthService auth, IServerSettingsService settings) => new(auth, settings, _postLoginNav)
    {
        Email = "a@b.pl",
        Password = "password1",
        RegEmail = "a@b.pl",
        RegPassword = "password1",
        RegConfirmPassword = "password1"
    };

    private static Task RunAsync(LoginViewModel vm, string action) => action switch
    {
        "login" => vm.LoginWithCredentialsCommand.ExecuteAsync(null),
        "google" => vm.LoginWithGoogleCommand.ExecuteAsync(null),
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

        await vm.LoginWithCredentialsCommand.ExecuteAsync(null);

        Assert.Equal("https://faktury.example.pl/auth/realms/openksef/protocol/openid-connect/token", _handler.Requests.Single().RequestUri!.ToString());
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

        Assert.True(new LoginViewModel(Substitute.For<IAuthService>(), settings, _postLoginNav).IsServerVisible);
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

        await vm.LoginWithCredentialsCommand.ExecuteAsync(null);

        Assert.Equal(UnreachableMessage, vm.ErrorMessage);
        Assert.True(vm.IsServerVisible);
        Assert.Equal(StoredServer, Preferences.Default.Get(ServerUrlKey, string.Empty));
        Assert.Equal(0, SecureStorage.Default.Count);
        await _postLoginNav.DidNotReceive().NavigateAsync();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    public async Task Login_RejectedCredentials_ShowsBadPassword_FieldVisibilityUnchanged(HttpStatusCode status, bool fieldVisible)
    {
        var (vm, _) = StoredServerLogin();
        if (fieldVisible)
            vm.ToggleServerCommand.Execute(null);
        _handler.Respond = _ => Task.FromResult(Response(status, """{"error":"invalid_grant","error_description":"Invalid user credentials"}"""));

        await vm.LoginWithCredentialsCommand.ExecuteAsync(null);

        Assert.Equal("Nieprawidłowy email lub hasło.", vm.ErrorMessage);
        Assert.Equal(fieldVisible, vm.IsServerVisible);
        await _postLoginNav.DidNotReceive().NavigateAsync();
    }

    [Fact]
    public async Task Login_RightPassword_Navigates()
    {
        var (vm, _) = StoredServerLogin();
        _handler.Respond = _ => Task.FromResult(Response(HttpStatusCode.OK, """{"access_token":"a","refresh_token":"r","expires_in":300}"""));

        await vm.LoginWithCredentialsCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        await _postLoginNav.Received(1).NavigateAsync();
    }

    [Fact]
    public async Task Register_NetworkFailure_NamesTheServerAndRevealsTheField()
    {
        var (vm, _) = StoredServerLogin();
        _handler.Respond = _ => throw new HttpRequestException("connection refused");

        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(UnreachableMessage, vm.ErrorMessage);
        Assert.True(vm.IsServerVisible);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "<html>not found</html>", "text/html")]
    [InlineData(HttpStatusCode.NotFound, "", "text/plain")]
    [InlineData(HttpStatusCode.Forbidden, "<html>403</html>", "text/html")]
    [InlineData(HttpStatusCode.Forbidden, "", "text/plain")]
    public async Task Register_UnreadableErrorBody_KeepsFixedTextAndRevealsTheField(HttpStatusCode status, string body, string mediaType)
    {
        var (vm, _) = StoredServerLogin();
        _handler.Respond = _ => Task.FromResult(Response(status, body, mediaType));

        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.Equal("Rejestracja nie powiodła się. Sprawdź adres serwera.", vm.ErrorMessage);
        Assert.True(vm.IsServerVisible);
    }

    [Theory]
    [InlineData("""{"code":"registration_disabled","error":"Rejestracja nowych kont jest wyłączona. Skontaktuj się z administratorem."}""", "Rejestracja nowych kont jest wyłączona. Skontaktuj się z administratorem.")]
    [InlineData("""{"title":"Forbidden","status":403}""", "Rejestracja nie powiodła się.")]
    public async Task Register_ForbiddenWithJson_ShowsServerText_FieldUnchanged_NoLoginAttempt(string body, string message)
    {
        var (vm, _) = StoredServerLogin();
        _handler.Respond = _ => Task.FromResult(Response(HttpStatusCode.Forbidden, body));

        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(message, vm.ErrorMessage);
        Assert.False(vm.IsServerVisible);
        Assert.Single(_handler.Requests);
        await _postLoginNav.DidNotReceive().NavigateAsync();
    }

    [Fact]
    public async Task Register_ServerError_ShowsTheServerMessage()
    {
        var (vm, _) = StoredServerLogin();
        _handler.Respond = _ => Task.FromResult(Response(HttpStatusCode.Conflict, """{"error":"Konto już istnieje."}"""));

        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.Equal("Konto już istnieje.", vm.ErrorMessage);
        Assert.False(vm.IsServerVisible);
    }

    [Fact]
    public async Task Register_CreatedButLoginCannotReachServer_SaysAccountCreated()
    {
        var (vm, _) = StoredServerLogin();
        _handler.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("/token")
            ? throw new HttpRequestException("connection refused")
            : Task.FromResult(Response(HttpStatusCode.OK, "{}"));

        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.Equal("Konto zostało utworzone. Zaloguj się za pomocą formularza logowania.", vm.ErrorMessage);
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

        await vm.LoginWithCredentialsCommand.ExecuteAsync(null);

        Assert.Equal("dead.example.pl", _handler.Requests.Single().RequestUri!.Host);
        Assert.Equal(StoredServer, Preferences.Default.Get(ServerUrlKey, string.Empty));
    }

    [Fact]
    public async Task VisibleDraft_IsUsedByLogin()
    {
        var (vm, _) = StoredServerLogin();
        vm.ToggleServerCommand.Execute(null);
        vm.ServerUrl = "https://draft.example.pl";

        await vm.LoginWithCredentialsCommand.ExecuteAsync(null);

        Assert.Equal("draft.example.pl", _handler.Requests.Single().RequestUri!.Host);
    }

    [Fact]
    public async Task HiddenEmptyField_ValidationErrorRevealsTheField()
    {
        var vm = CreateLoginViewModel(Substitute.For<IAuthService>(), new ServerSettingsService());
        vm.ToggleServerCommand.Execute(null);
        Assert.False(vm.IsServerVisible);

        await vm.LoginWithCredentialsCommand.ExecuteAsync(null);

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
