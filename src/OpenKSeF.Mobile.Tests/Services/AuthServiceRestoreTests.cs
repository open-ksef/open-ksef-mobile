using System.Net;
using System.Text;
using NSubstitute;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
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
    public async Task PasswordLogin_RequestsOfflineAccess()
    {
        _handler.Respond = _ => Task.FromResult(Json(HttpStatusCode.OK, TokenJson));

        Assert.True(await _auth.LoginWithCredentialsAsync("a@b.pl", "x"));

        Assert.Contains("scope=openid+profile+email+offline_access", _handler.Bodies.Single());
    }

    [Fact]
    public void GoogleLogin_RequestsOfflineAccess() =>
        Assert.Equal("openid profile email offline_access", _auth.GoogleLoginScope);

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
        var never = new TaskCompletionSource<HttpResponseMessage>();
        _handler.Respond = _ => never.Task;

        Assert.Equal(SessionRestoreResult.Unverified, await _auth.RestoreSessionAsync());
        Assert.True(SecureStorage.Default.Count > 0);
    }

    private async Task LoginExpiringAsync()
    {
        _handler.Respond = _ => Task.FromResult(Json(HttpStatusCode.OK, """{"access_token":"old","refresh_token":"r1","expires_in":30}"""));
        Assert.True(await _auth.LoginWithCredentialsAsync("a@b.pl", "x"));
        _handler.Requests.Clear();
        _handler.Bodies.Clear();
    }

    private async Task<string?[]> ParallelCallersAsync(TaskCompletionSource release, int count = 20)
    {
        var callers = Enumerable.Range(0, count).Select(_ => Task.Run(_auth.GetAccessTokenAsync)).ToArray();
        await Task.Delay(100);
        release.SetResult();
        return await Task.WhenAll(callers).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ParallelCallers_SendOneRefreshPost_AndShareTheToken()
    {
        await LoginExpiringAsync();
        var release = new TaskCompletionSource();
        _handler.Respond = async _ =>
        {
            await release.Task;
            return Json(HttpStatusCode.OK, """{"access_token":"a2","refresh_token":"r2","expires_in":300}""");
        };

        var tokens = await ParallelCallersAsync(release);

        Assert.All(tokens, token => Assert.Equal("a2", token));
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task ParallelCallers_InvalidGrant_NoDeadlock_OnePost()
    {
        await LoginExpiringAsync();
        var release = new TaskCompletionSource();
        _handler.Respond = async _ =>
        {
            await release.Task;
            return Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
        };

        var tokens = await ParallelCallersAsync(release);

        Assert.All(tokens, Assert.Null);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task ParallelCallers_NetworkFailure_NoDeadlock_NextCallPostsAgain()
    {
        await LoginExpiringAsync();
        var release = new TaskCompletionSource();
        _handler.Respond = async _ =>
        {
            await release.Task;
            throw new HttpRequestException("no route to host");
        };

        var tokens = await ParallelCallersAsync(release);

        Assert.All(tokens, Assert.Null);
        Assert.Single(_handler.Requests);

        _handler.Respond = _ => Task.FromResult(Json(HttpStatusCode.OK, """{"access_token":"a2","refresh_token":"r2","expires_in":300}"""));
        Assert.Equal("a2", await _auth.GetAccessTokenAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task SequentialRefreshes_EachPost_WhenResponseCompletesSynchronously()
    {
        await LoginExpiringAsync();
        _handler.Respond = _ => Task.FromResult(Json(HttpStatusCode.OK, """{"access_token":"a2","refresh_token":"r2","expires_in":30}"""));

        Assert.Equal("a2", await _auth.GetAccessTokenAsync());
        Assert.Equal("a2", await _auth.GetAccessTokenAsync());

        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task RotatingRealm_SlowEndpoint_SessionSurvivesColdStartOverlap()
    {
        await LoginExpiringAsync();
        var current = "r1";
        var generation = 1;
        var rejected = 0;
        _handler.Respond = async request =>
        {
            var used = (await request.Content!.ReadAsStringAsync()).Contains($"refresh_token={current}");
            await Task.Delay(50);
            if (!used)
            {
                Interlocked.Increment(ref rejected);
                return Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
            }

            current = $"r{Interlocked.Increment(ref generation)}";
            return Json(HttpStatusCode.OK, $$"""{"access_token":"a{{generation}}","refresh_token":"{{current}}","expires_in":60}""");
        };

        Assert.Equal(SessionRestoreResult.Restored, await _auth.RestoreSessionAsync());
        for (var round = 0; round < 3; round++)
        {
            var tokens = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(_auth.GetAccessTokenAsync))).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.All(tokens, Assert.NotNull);
        }

        Assert.Equal(0, rejected);
        Assert.True(SecureStorage.Default.Count > 0);
    }

    [Fact]
    public async Task RestoreTimedOut_LaterCallerJoinsTheSamePost()
    {
        await LoginExpiringAsync();
        AuthService.RestoreTimeout = TimeSpan.FromMilliseconds(50);
        var release = new TaskCompletionSource();
        _handler.Respond = async _ =>
        {
            await release.Task;
            return Json(HttpStatusCode.OK, """{"access_token":"a2","refresh_token":"r2","expires_in":300}""");
        };

        Assert.Equal(SessionRestoreResult.Unverified, await _auth.RestoreSessionAsync());
        var caller = _auth.GetAccessTokenAsync();
        release.SetResult();

        Assert.Equal("a2", await caller.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task InvalidGrantDuringUse_RaisesSessionEndedOnce_ClearsTokens_AndStopsPosting()
    {
        await LoginExpiringAsync();
        var ended = 0;
        _auth.SessionEnded += () => Interlocked.Increment(ref ended);
        var release = new TaskCompletionSource();
        _handler.Respond = async _ =>
        {
            await release.Task;
            return Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
        };

        await ParallelCallersAsync(release);
        Assert.Null(await _auth.GetAccessTokenAsync());

        Assert.Equal(1, ended);
        Assert.Equal(0, SecureStorage.Default.Count);
        Assert.Single(_handler.Requests);
    }

    [Theory]
    [MemberData(nameof(ServerProblems))]
    public async Task ServerProblemDuringUse_DoesNotEndSession_KeepsTokens(Func<HttpRequestMessage, Task<HttpResponseMessage>> problem)
    {
        await LoginExpiringAsync();
        var stored = SecureStorage.Default.Count;
        var ended = 0;
        _auth.SessionEnded += () => ended++;
        _handler.Respond = problem;

        Assert.Null(await _auth.GetAccessTokenAsync());

        Assert.Equal(0, ended);
        Assert.Equal(stored, SecureStorage.Default.Count);
    }

    [Fact]
    public async Task RestoreInvalidGrant_DoesNotRaiseSessionEnded()
    {
        await LoginAsync();
        var ended = 0;
        _auth.SessionEnded += () => ended++;
        _handler.Respond = _ => Task.FromResult(Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));

        Assert.Equal(SessionRestoreResult.None, await _auth.RestoreSessionAsync());

        Assert.Equal(0, ended);
    }

    [Fact]
    public async Task RestoreTimedOut_LateInvalidGrant_RaisesSessionEnded()
    {
        await LoginAsync();
        AuthService.RestoreTimeout = TimeSpan.FromMilliseconds(50);
        var ended = new TaskCompletionSource();
        _auth.SessionEnded += ended.SetResult;
        var release = new TaskCompletionSource();
        _handler.Respond = async _ =>
        {
            await release.Task;
            return Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
        };

        Assert.Equal(SessionRestoreResult.Unverified, await _auth.RestoreSessionAsync());
        release.SetResult();

        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task LogoutDuringRefresh_LateResponseDoesNotRestoreTheSession()
    {
        await LoginExpiringAsync();
        var release = new TaskCompletionSource();
        _handler.Respond = async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/logout"))
                return Json(HttpStatusCode.NoContent, string.Empty);
            await release.Task;
            return Json(HttpStatusCode.OK, """{"access_token":"a2","refresh_token":"r2","expires_in":300}""");
        };

        var caller = Task.Run(_auth.GetAccessTokenAsync);
        await Task.Delay(100);
        await _auth.LogoutAsync();
        release.SetResult();

        Assert.Null(await caller.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, SecureStorage.Default.Count);
    }

    [Fact]
    public async Task Logout_ClearsStoredSession_SoRestoreGivesLogin()
    {
        await LoginAsync();

        await _auth.LogoutAsync();

        Assert.Equal(0, SecureStorage.Default.Count);
        Assert.Equal(SessionRestoreResult.None, await _auth.RestoreSessionAsync());
    }

    [Fact]
    public async Task Logout_SendsStoredRefreshToken()
    {
        await LoginAsync();
        _handler.Respond = _ => Task.FromResult(Json(HttpStatusCode.NoContent, string.Empty));

        await _auth.LogoutAsync();

        Assert.EndsWith("/protocol/openid-connect/logout", _handler.Requests.Single().RequestUri!.AbsolutePath);
        Assert.Contains("refresh_token=new-refresh", _handler.Bodies.Single());
    }

    [Fact]
    public async Task LogoutDuringRefresh_LateInvalidGrant_RaisesNoSessionEnded()
    {
        await LoginExpiringAsync();
        var ended = 0;
        _auth.SessionEnded += () => Interlocked.Increment(ref ended);
        var refreshSent = new TaskCompletionSource();
        var logoutSent = new TaskCompletionSource();
        Task<string?>? caller = null;
        _handler.Respond = async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/logout"))
            {
                logoutSent.SetResult();
                await caller!.WaitAsync(TimeSpan.FromSeconds(5));
                return Json(HttpStatusCode.NoContent, string.Empty);
            }

            refreshSent.SetResult();
            await logoutSent.Task;
            return Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
        };

        caller = Task.Run(_auth.GetAccessTokenAsync);
        await refreshSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _auth.LogoutAsync();

        Assert.Equal(0, ended);
        Assert.Equal(0, SecureStorage.Default.Count);
        Assert.Contains("refresh_token=r1", _handler.Bodies.Last());
    }

    private PostLoginNavigationService Navigation(IDeviceTokenService? device = null) => new(
        Substitute.For<IApiService>(),
        _auth,
        device ?? Substitute.For<IDeviceTokenService>(),
        Substitute.For<INotificationHubService>(),
        Substitute.For<IServerSettingsService>(),
        new LocalDbService());

    private async Task EndSessionAsync()
    {
        await LoginExpiringAsync();
        var ended = new TaskCompletionSource();
        _auth.SessionEnded += () => ended.TrySetResult();
        _handler.Respond = _ => Task.FromResult(Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));
        await _auth.GetAccessTokenAsync();
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SessionEnded_NoWindow_DoesNotThrow_TokensStayCleared()
    {
        await EndSessionAsync();
        var windows = Application.Current!.Windows;
        var saved = windows.ToList();
        windows.Clear();
        try
        {
            await Navigation().HandleSessionEndedAsync();

            Assert.Equal(0, SecureStorage.Default.Count);
        }
        finally
        {
            windows.AddRange(saved);
        }
    }

    [Fact]
    public async Task SessionEnded_WithWindow_ShowsNewShell()
    {
        await EndSessionAsync();
        var window = Application.Current!.Windows[0];
        window.Page = null;

        await Navigation().HandleSessionEndedAsync();

        Assert.IsType<AppShell>(window.Page);
        Assert.Equal(0, SecureStorage.Default.Count);
    }

    private IDeviceTokenService DeviceRefreshingOnUnregister()
    {
        var device = Substitute.For<IDeviceTokenService>();
        device.UnregisterCurrentDeviceAsync().Returns(async _ => await _auth.GetAccessTokenAsync());
        return device;
    }

    private void RespondToRefresh(HttpStatusCode status, string body) =>
        _handler.Respond = request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/logout")
            ? Json(HttpStatusCode.NoContent, string.Empty)
            : Json(status, body));

    [Fact]
    public async Task SignOut_InvalidGrantDuringUnregister_RaisesNoSessionEnded_NoAlert()
    {
        Shell.Current.Reset();
        await LoginExpiringAsync();
        var nav = Navigation(DeviceRefreshingOnUnregister());
        var ended = 0;
        _auth.SessionEnded += () =>
        {
            ended++;
            _ = nav.HandleSessionEndedAsync();
        };
        RespondToRefresh(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");

        await nav.SignOutAsync();

        Assert.Equal(0, ended);
        Assert.Null(Shell.Current.LastAlertMessage);
        Assert.Equal(0, SecureStorage.Default.Count);
        Assert.False(_auth.IsAuthenticated);
    }

    [Fact]
    public async Task Logout_InvalidGrantDuringUnregister_OpensLogin_WithoutSessionAlert()
    {
        Shell.Current.Reset();
        await LoginExpiringAsync();
        var nav = Navigation(DeviceRefreshingOnUnregister());
        _auth.SessionEnded += () => _ = nav.HandleSessionEndedAsync();
        RespondToRefresh(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
        var settings = Substitute.For<IServerSettingsService>();
        settings.ServerUrl.Returns("https://ksef.example.com");
        var vm = new AccountViewModel(settings, Substitute.For<IDeviceTokenService>(), Substitute.For<INotificationHubService>(), nav);
        var window = Application.Current!.Windows[0];
        window.Page = null;

        await vm.LogoutCommand.ExecuteAsync(null);

        Assert.Equal("Czy na pewno chcesz się wylogować?", Shell.Current.LastAlertMessage);
        Assert.IsType<AppShell>(window.Page);
    }

    [Fact]
    public async Task SignOut_LiveSession_SendsCurrentRefreshTokenToLogout()
    {
        await LoginExpiringAsync();
        RespondToRefresh(HttpStatusCode.OK, """{"access_token":"a2","refresh_token":"r2","expires_in":300}""");

        await Navigation(DeviceRefreshingOnUnregister()).SignOutAsync();

        Assert.EndsWith("/protocol/openid-connect/logout", _handler.Requests.Last().RequestUri!.AbsolutePath);
        Assert.Contains("refresh_token=r2", _handler.Bodies.Last());
        Assert.Equal(0, SecureStorage.Default.Count);
    }

    [Fact]
    public async Task SignOut_NextSessionDying_StillRaisesSessionEnded()
    {
        await LoginExpiringAsync();
        RespondToRefresh(HttpStatusCode.NoContent, string.Empty);
        await Navigation().SignOutAsync();

        await EndSessionAsync();

        Assert.Equal(0, SecureStorage.Default.Count);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; } = _ => throw new InvalidOperationException();
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return await Respond(request);
        }
    }
}
