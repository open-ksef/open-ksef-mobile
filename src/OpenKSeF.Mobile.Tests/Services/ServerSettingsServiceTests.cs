using System.Net;
using System.Text;
using NSubstitute;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;
using OidcBrowser = IdentityModel.OidcClient.Browser.IBrowser;

namespace OpenKSeF.Mobile.Tests.Services;

[Collection("Shell")]
public class ServerSettingsServiceTests
{
    private const string ServerUrlKey = "server_url";
    private const string EmptyServerMessage = "Adres serwera nie może być pusty.";

    private readonly RecordingHandler _handler = new();
    private readonly IPostLoginNavigationService _postLoginNav = Substitute.For<IPostLoginNavigationService>();

    public ServerSettingsServiceTests()
    {
        Shell.Current.Reset();
        Preferences.Default.Clear();
        SecureStorage.Default.Reset();
    }

    private AuthService CreateAuthService(IServerSettingsService settings) => new(
        new AuthOptions { Authority = settings.Authority, ClientId = "openksef-mobile", RedirectUri = "openksef://auth/callback", PostLogoutRedirectUri = "openksef://auth/logout" },
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

    [Theory]
    [InlineData("login")]
    [InlineData("google")]
    [InlineData("register")]
    public async Task StoredDeadServer_FailedSignInRevealsTheFieldAndKeepsTheUrl(string action)
    {
        Preferences.Default.Set(ServerUrlKey, "https://dead.example.pl");
        var vm = CreateLoginViewModel(Substitute.For<IAuthService>(), new ServerSettingsService());
        Assert.False(vm.IsServerVisible);

        await RunAsync(vm, action);

        Assert.True(vm.IsServerVisible);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Equal("https://dead.example.pl", Preferences.Default.Get(ServerUrlKey, string.Empty));
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
