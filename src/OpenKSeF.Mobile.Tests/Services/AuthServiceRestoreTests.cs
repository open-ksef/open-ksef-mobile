using System.Net;
using System.Text;
using NSubstitute;
using OpenKSeF.Mobile.Services;
using Xunit;
using OidcBrowser = IdentityModel.OidcClient.Browser.IBrowser;

namespace OpenKSeF.Mobile.Tests.Services;

[Collection("Shell")]
public class AuthServiceRestoreTests
{
    private const string TokenJson = """{"access_token":"new-access","refresh_token":"new-refresh","expires_in":300}""";

    private readonly StubHandler _handler = new();
    private readonly AuthService _auth;

    public AuthServiceRestoreTests()
    {
        SecureStorage.Default.Reset();
        AuthService.RestoreTimeout = TimeSpan.FromSeconds(5);

        var settings = Substitute.For<IServerSettingsService>();
        settings.ServerUrl.Returns("https://ksef.example.com");
        settings.Authority.Returns("https://ksef.example.com/auth/realms/openksef");

        _auth = new AuthService(
            new AuthOptions { Authority = settings.Authority, ClientId = "openksef-mobile", RedirectUri = "openksef://auth/callback", PostLogoutRedirectUri = "openksef://auth/logout" },
            Substitute.For<OidcBrowser>(),
            settings,
            new HttpClient(_handler));
    }

    private async Task LoginAsync()
    {
        _handler.Respond = _ => Task.FromResult(Json(HttpStatusCode.OK, TokenJson));
        Assert.True(await _auth.LoginWithCredentialsAsync("a@b.pl", "x"));
        _handler.Requests.Clear();
        _handler.Bodies.Clear();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task NoStoredTokens_NoRequest()
    {
        Assert.Equal(SessionRestoreResult.None, await _auth.RestoreSessionAsync());
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task UnreadableKeystore_GivesLogin_NoCrash()
    {
        await LoginAsync();
        SecureStorage.Default.ThrowOnRead = true;

        Assert.Equal(SessionRestoreResult.None, await _auth.RestoreSessionAsync());
    }

    [Fact]
    public async Task ValidRefreshToken_RestoresAndSendsRefreshGrant()
    {
        await LoginAsync();
        _handler.Respond = _ => Task.FromResult(Json(HttpStatusCode.OK, """{"access_token":"a2","refresh_token":"r2","expires_in":300}"""));

        Assert.Equal(SessionRestoreResult.Restored, await _auth.RestoreSessionAsync());

        Assert.Equal("a2", await _auth.GetAccessTokenAsync());
        var body = _handler.Bodies.Single();
        Assert.Contains("grant_type=refresh_token", body);
        Assert.Contains("refresh_token=new-refresh", body);
    }

    [Theory]
    [InlineData("Token is not active")]
    [InlineData("Session not active")]
    public async Task ExpiredOrRevoked_ClearsTokens(string description)
    {
        await LoginAsync();
        _handler.Respond = _ => Task.FromResult(Json(HttpStatusCode.BadRequest, $$"""{"error":"invalid_grant","error_description":"{{description}}"}"""));

        Assert.Equal(SessionRestoreResult.None, await _auth.RestoreSessionAsync());

        Assert.Equal(0, SecureStorage.Default.Count);
    }

    public static TheoryData<Func<HttpRequestMessage, Task<HttpResponseMessage>>> ServerProblems => new()
    {
        _ => throw new HttpRequestException("no route to host"),
        _ => Task.FromResult(Json(HttpStatusCode.BadGateway, "<html>bad gateway</html>")),
        _ => Task.FromResult(Json(HttpStatusCode.InternalServerError, "{}")),
        _ => Task.FromResult(Json(HttpStatusCode.BadRequest, "<html>proxy</html>")),
        _ => Task.FromResult(Json(HttpStatusCode.BadRequest, """{"error":"unknown_error"}""")),
    };

    [Theory]
    [MemberData(nameof(ServerProblems))]
    public async Task OfflineOrServerProblem_KeepsTokens(Func<HttpRequestMessage, Task<HttpResponseMessage>> problem)
    {
        await LoginAsync();
        var stored = SecureStorage.Default.Count;
        _handler.Respond = problem;

        Assert.Equal(SessionRestoreResult.Unverified, await _auth.RestoreSessionAsync());

        Assert.Equal(stored, SecureStorage.Default.Count);
    }

    [Fact]
    public async Task UnreachableServerThatHangs_TimesOutAsUnverified()
    {
        await LoginAsync();
        AuthService.RestoreTimeout = TimeSpan.FromMilliseconds(50);
        _handler.Respond = async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), _handler.LastToken);
            return Json(HttpStatusCode.OK, TokenJson);
        };

        Assert.Equal(SessionRestoreResult.Unverified, await _auth.RestoreSessionAsync());
        Assert.True(SecureStorage.Default.Count > 0);
    }

    [Fact]
    public async Task Logout_ClearsStoredSession_SoRestoreGivesLogin()
    {
        await LoginAsync();

        await _auth.LogoutAsync();

        Assert.Equal(0, SecureStorage.Default.Count);
        Assert.Equal(SessionRestoreResult.None, await _auth.RestoreSessionAsync());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; } = _ => throw new InvalidOperationException();
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];
        public CancellationToken LastToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            LastToken = cancellationToken;
            return await Respond(request);
        }
    }
}
