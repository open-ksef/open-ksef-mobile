using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using NSubstitute;
using OpenKSeF.Mobile.Services;
using Xunit;
using OidcBrowser = Duende.IdentityModel.OidcClient.Browser.IBrowser;

namespace OpenKSeF.Mobile.Tests.Services;

[Collection("Shell")]
public class AuthServicePairingTests
{
    private const string Authority = "https://ksef.example.com/auth/realms/openksef";
    private const string DeviceJson = """{"device_code":"dev-1","user_code":"WDJB-MJHT","verification_uri":"https://ksef.example.com/auth/realms/openksef/device","expires_in":300,"interval":5}""";
    private const string TokenJson = """{"access_token":"p-access","refresh_token":"p-offline","expires_in":300}""";

    private readonly Handler _handler = new();
    private readonly ManualClock _clock = new();
    private readonly List<TimeSpan> _delays = [];
    private readonly AuthService _auth;

    public AuthServicePairingTests()
    {
        SecureStorage.Default.Reset();
        var settings = Substitute.For<IServerSettingsService>();
        settings.ServerUrl.Returns("https://ksef.example.com");
        settings.Authority.Returns(Authority);

        _auth = new AuthService(new AuthOptions(), Substitute.For<OidcBrowser>(), settings, new HttpClient(_handler))
        {
            Clock = _clock,
            Delay = (interval, _) =>
            {
                _delays.Add(interval);
                _clock.Advance(interval);
                return Task.CompletedTask;
            }
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage OAuthError(string error) => Json(HttpStatusCode.BadRequest, $$"""{"error":"{{error}}"}""");

    private void DeviceThenTokens(params Func<HttpResponseMessage>[] polls)
    {
        var next = 0;
        _handler.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("/auth/device")
            ? Json(HttpStatusCode.OK, DeviceJson)
            : polls[Math.Min(next++, polls.Length - 1)]();
    }

    private async Task<AuthResult> PairAsync()
    {
        var (result, pairing) = await _auth.StartPairingAsync(CancellationToken.None);
        return pairing is null ? result : await _auth.CompletePairingAsync(pairing, CancellationToken.None);
    }

    private IEnumerable<(string Path, System.Collections.Specialized.NameValueCollection Form)> Sent =>
        _handler.Sent.Select(s => (s.Path, HttpUtility.ParseQueryString(s.Body)));

    [Fact]
    public async Task Start_SendsPkceS256AndOfflineScope_PollSendsMatchingVerifier()
    {
        DeviceThenTokens(() => Json(HttpStatusCode.OK, TokenJson));

        Assert.Equal(AuthResult.Success, await PairAsync());

        var device = Sent.First();
        Assert.Equal("/auth/realms/openksef/protocol/openid-connect/auth/device", device.Path);
        Assert.Equal("openksef-mobile", device.Form["client_id"]);
        Assert.Equal("openid profile email offline_access", device.Form["scope"]);
        Assert.Equal("S256", device.Form["code_challenge_method"]);

        var poll = Sent.Last();
        Assert.Equal("/auth/realms/openksef/protocol/openid-connect/token", poll.Path);
        Assert.Equal("urn:ietf:params:oauth:grant-type:device_code", poll.Form["grant_type"]);
        Assert.Equal("dev-1", poll.Form["device_code"]);
        Assert.Equal("openksef-mobile", poll.Form["client_id"]);
        var verifier = poll.Form["code_verifier"]!;
        Assert.InRange(verifier.Length, 43, 128);
        Assert.Equal(System.Buffers.Text.Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), device.Form["code_challenge"]);
    }

    [Fact]
    public async Task Start_ReturnsUserCodeAndExpiry()
    {
        DeviceThenTokens();
        var now = _clock.GetUtcNow();

        var (result, pairing) = await _auth.StartPairingAsync(CancellationToken.None);

        Assert.Equal(AuthResult.Success, result);
        Assert.Equal("WDJB-MJHT", pairing!.UserCode);
        Assert.Equal(now.AddSeconds(300), pairing.ExpiresAt);
        Assert.Equal(TimeSpan.FromSeconds(5), pairing.Interval);
    }

    [Fact]
    public async Task Start_ZeroInterval_PollsEveryFiveSeconds()
    {
        _handler.Respond = _ => Json(HttpStatusCode.OK, """{"device_code":"dev-1","user_code":"WDJB-MJHT","expires_in":300,"interval":0}""");

        var (_, pairing) = await _auth.StartPairingAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(5), pairing!.Interval);
    }

    [Fact]
    public async Task Poll_WaitsInterval_SlowDownAddsFiveSeconds_ThenStoresOfflineTokens()
    {
        DeviceThenTokens(
            () => OAuthError("authorization_pending"),
            () => OAuthError("slow_down"),
            () => OAuthError("authorization_pending"),
            () => OAuthError("slow_down"),
            () => Json(HttpStatusCode.OK, TokenJson));
        var generation = _auth.SessionGeneration;

        Assert.Equal(AuthResult.Success, await PairAsync());

        Assert.Equal([5, 5, 10, 10, 15], _delays.Select(d => d.TotalSeconds));
        Assert.Equal(5, Sent.Count(s => s.Path.EndsWith("/token")));
        Assert.True(_auth.HasSession);
        Assert.True(_auth.SessionGeneration > generation);
        Assert.Equal("p-access", await _auth.GetAccessTokenAsync());
        Assert.Equal(3, SecureStorage.Default.Count);
    }

    [Theory]
    [InlineData("expired_token", AuthResult.PairingExpired)]
    [InlineData("access_denied", AuthResult.PairingDenied)]
    [InlineData("unauthorized_client", AuthResult.PairingNotSupported)]
    [InlineData("invalid_client", AuthResult.ClientRejected)]
    [InlineData("invalid_grant", AuthResult.Rejected)]
    public async Task Poll_TerminalError_StopsAndStoresNothing(string error, AuthResult expected)
    {
        DeviceThenTokens(() => OAuthError("authorization_pending"), () => OAuthError(error));

        Assert.Equal(expected, await PairAsync());

        Assert.Equal(2, Sent.Count(s => s.Path.EndsWith("/token")));
        Assert.False(_auth.HasSession);
        Assert.Equal(0, SecureStorage.Default.Count);
    }

    public static TheoryData<string> TransientFailures => ["network", "timeout", "502", "500json", "html"];

    [Theory]
    [MemberData(nameof(TransientFailures))]
    public async Task Poll_TransientFailure_KeepsPolling(string failure)
    {
        DeviceThenTokens(
            () => failure switch
            {
                "network" => throw new HttpRequestException("offline"),
                "timeout" => throw new TaskCanceledException("timeout", new TimeoutException()),
                "502" => Json(HttpStatusCode.BadGateway, "<html>bad gateway</html>"),
                "500json" => Json(HttpStatusCode.InternalServerError, """{"error":"server_error"}"""),
                _ => Json(HttpStatusCode.BadRequest, "<html>proxy</html>")
            },
            () => Json(HttpStatusCode.OK, TokenJson));

        Assert.Equal(AuthResult.Success, await PairAsync());

        Assert.Equal(2, Sent.Count(s => s.Path.EndsWith("/token")));
    }

    [Fact]
    public async Task Poll_StopsAtLocalExpiry_WithoutAnotherRequest()
    {
        DeviceThenTokens(() => OAuthError("authorization_pending"));

        Assert.Equal(AuthResult.PairingExpired, await PairAsync());

        Assert.Equal(59, Sent.Count(s => s.Path.EndsWith("/token")));
        Assert.Equal(300, _delays.Sum(d => d.TotalSeconds));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"unauthorized_client","error_description":"Client is not allowed to initiate OAuth 2.0 Device Authorization Grant. The flow is disabled for the client."}""", AuthResult.PairingNotSupported)]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""", AuthResult.ClientRejected)]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_request"}""", AuthResult.Rejected)]
    [InlineData(HttpStatusCode.NotFound, "<html>not found</html>", AuthResult.ServerUnreachable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"error":"temporarily_unavailable"}""", AuthResult.ServerUnreachable)]
    [InlineData(HttpStatusCode.OK, "<html>portal</html>", AuthResult.ServerUnreachable)]
    [InlineData(HttpStatusCode.OK, "{}", AuthResult.ServerUnreachable)]
    public async Task Start_Error_MapsToResult_NoPoll(HttpStatusCode status, string body, AuthResult expected)
    {
        _handler.Respond = _ => Json(status, body);

        var (result, pairing) = await _auth.StartPairingAsync(CancellationToken.None);

        Assert.Equal(expected, result);
        Assert.Null(pairing);
        Assert.Single(_handler.Sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Start_NetworkErrorOrTimeout_IsServerUnreachable(bool timeout)
    {
        _handler.Respond = _ => timeout ? throw new TaskCanceledException("timeout", new TimeoutException()) : throw new HttpRequestException("offline");

        Assert.Equal((AuthResult.ServerUnreachable, null), await _auth.StartPairingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Poll_Cancelled_ThrowsAndStoresNothing()
    {
        DeviceThenTokens(() => OAuthError("authorization_pending"));
        var (_, pairing) = await _auth.StartPairingAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource();
        _auth.Delay = (_, token) =>
        {
            cts.Cancel();
            return Task.Delay(Timeout.Infinite, token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _auth.CompletePairingAsync(pairing!, cts.Token));

        Assert.False(_auth.HasSession);
        Assert.Single(_handler.Sent);
    }

    [Fact]
    public async Task Poll_CancelledDuringRequest_ThrowsAndStoresNothing()
    {
        using var cts = new CancellationTokenSource();
        _handler.Respond = request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/device"))
                return Json(HttpStatusCode.OK, DeviceJson);
            cts.Cancel();
            return Json(HttpStatusCode.OK, TokenJson);
        };
        var (_, pairing) = await _auth.StartPairingAsync(CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _auth.CompletePairingAsync(pairing!, cts.Token));

        Assert.False(_auth.HasSession);
        Assert.Equal(0, SecureStorage.Default.Count);
    }

    [Fact]
    public async Task PairedSession_RefreshesWithOfflineToken()
    {
        DeviceThenTokens(() => Json(HttpStatusCode.OK, """{"access_token":"p-access","refresh_token":"p-offline","expires_in":0}"""));
        Assert.Equal(AuthResult.Success, await PairAsync());
        _handler.Sent.Clear();
        _handler.Respond = _ => Json(HttpStatusCode.OK, """{"access_token":"a2","refresh_token":"p-offline-2","expires_in":300}""");

        Assert.Equal("a2", await _auth.GetAccessTokenAsync());

        var refresh = Sent.Single();
        Assert.Equal("refresh_token", refresh.Form["grant_type"]);
        Assert.Equal("p-offline", refresh.Form["refresh_token"]);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => throw new InvalidOperationException();
        public List<(string Path, string Body)> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent.Add((request.RequestUri!.AbsolutePath, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return Respond(request);
        }
    }
}
