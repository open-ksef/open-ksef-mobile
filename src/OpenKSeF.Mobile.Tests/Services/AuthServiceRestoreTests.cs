using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using IdentityModel.Jwk;
using IdentityModel.OidcClient;
using IdentityModel.OidcClient.Browser;
using IdentityModel.OidcClient.Results;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Mobile.Models;
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
    private readonly IServerSettingsService _settings = Substitute.For<IServerSettingsService>();
    private readonly OidcBrowser _browser = Substitute.For<OidcBrowser>();

    public AuthServiceRestoreTests()
    {
        SecureStorage.Default.Reset();
        AuthService.RestoreTimeout = TimeSpan.FromSeconds(5);

        _settings.ServerUrl.Returns("https://ksef.example.com");
        _settings.Authority.Returns("https://ksef.example.com/auth/realms/openksef");

        _auth = new AuthService(
            new AuthOptions { Authority = _settings.Authority, ClientId = "openksef-mobile", RedirectUri = "openksef://auth/callback", PostLogoutRedirectUri = "openksef://auth/logout" },
            _browser,
            _settings,
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
    public async Task ServerRevokedAccessToken_LocallyValid_ApiCallRefreshesSilently()
    {
        await LoginAsync();
        var ended = 0;
        _auth.SessionEnded += () => ended++;
        _handler.Respond = request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/token")
            ? Json(HttpStatusCode.OK, """{"access_token":"a2","refresh_token":"r2","expires_in":300}""")
            : request.Headers.Authorization?.Parameter == "a2"
                ? Json(HttpStatusCode.OK, "[]")
                : Json(HttpStatusCode.Unauthorized, string.Empty));
        var settings = Substitute.For<IServerSettingsService>();
        settings.ServerUrl.Returns("https://ksef.example.com");

        Assert.Empty(await new ApiService(new HttpClient(_handler), _auth, settings, NullLogger<ApiService>.Instance).GetTenantsAsync());

        Assert.Equal(["/api/tenants", "/auth/realms/openksef/protocol/openid-connect/token", "/api/tenants"], _handler.Requests.Select(r => r.RequestUri!.AbsolutePath));
        Assert.Contains("grant_type=refresh_token", _handler.Bodies[1]);
        Assert.Contains("refresh_token=new-refresh", _handler.Bodies[1]);
        Assert.Equal("a2", await _auth.GetAccessTokenAsync());
        Assert.Equal(0, ended);
    }

    [Fact]
    public async Task ForcedRefresh_InvalidGrant_ReturnsNull_RaisesSessionEnded_ClearsTokens()
    {
        await LoginAsync();
        var ended = 0;
        _auth.SessionEnded += () => ended++;
        _handler.Respond = _ => Task.FromResult(Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));

        Assert.Null(await _auth.RefreshAccessTokenAsync());

        Assert.Equal(1, ended);
        Assert.Equal(0, SecureStorage.Default.Count);
    }

    [Theory]
    [MemberData(nameof(ServerProblems))]
    public async Task ForcedRefresh_ServerProblem_ReturnsNull_DoesNotEndSession_KeepsTokens(Func<HttpRequestMessage, Task<HttpResponseMessage>> problem)
    {
        await LoginAsync();
        var stored = SecureStorage.Default.Count;
        var ended = 0;
        _auth.SessionEnded += () => ended++;
        _handler.Respond = problem;

        Assert.Null(await _auth.RefreshAccessTokenAsync());

        Assert.Equal(0, ended);
        Assert.Equal(stored, SecureStorage.Default.Count);
    }

    [Fact]
    public async Task ForcedRefresh_NoSession_NoRequest()
    {
        Assert.Null(await _auth.RefreshAccessTokenAsync());
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task ConcurrentForcedRefreshes_RotatingRealm_OnePost_LateCallerSendsRotatedToken()
    {
        await LoginAsync();
        var current = "new-refresh";
        var generation = 1;
        var rejected = 0;
        var release = new TaskCompletionSource();
        _handler.Respond = async request =>
        {
            var used = (await request.Content!.ReadAsStringAsync()).Contains($"refresh_token={current}");
            await release.Task;
            if (!used)
            {
                Interlocked.Increment(ref rejected);
                return Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
            }

            current = $"r{++generation}";
            return Json(HttpStatusCode.OK, $$"""{"access_token":"a{{generation}}","refresh_token":"{{current}}","expires_in":300}""");
        };

        var callers = Enumerable.Range(0, 20).Select(_ => Task.Run(_auth.RefreshAccessTokenAsync)).ToArray();
        await Task.Delay(100);
        release.SetResult();
        var tokens = await Task.WhenAll(callers).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(tokens, token => Assert.Equal("a2", token));
        Assert.Single(_handler.Requests);

        Assert.Equal("a3", await _auth.RefreshAccessTokenAsync());
        Assert.Equal(2, _handler.Requests.Count);
        Assert.Equal(0, rejected);
    }

    private ApiService CreateApi() => new(new HttpClient(_handler), _auth, _settings, NullLogger<ApiService>.Instance);

    private static bool IsTokenRequest(HttpRequestMessage request) => request.RequestUri!.AbsolutePath.EndsWith("/token");

    [Fact]
    public async Task Api401_RefreshInvalidGrant_EndsSessionOnce_ThrowsSessionExpired_WithoutRetry()
    {
        await LoginAsync();
        var ended = 0;
        _auth.SessionEnded += () => ended++;
        _handler.Respond = request => Task.FromResult(IsTokenRequest(request)
            ? Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""")
            : Json(HttpStatusCode.Unauthorized, string.Empty));

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateApi().GetTenantsAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal("Sesja wygasła. Zaloguj się ponownie.", ex.Message);
        Assert.Equal(2, _handler.Requests.Count);
        Assert.True(IsTokenRequest(_handler.Requests[1]));
        Assert.Equal(1, ended);
        Assert.Equal(0, SecureStorage.Default.Count);

        _handler.Requests.Clear();
        await Assert.ThrowsAsync<ApiException>(() => CreateApi().GetTenantsAsync());
        Assert.DoesNotContain(_handler.Requests, IsTokenRequest);
        Assert.Equal(1, ended);
    }

    [Theory]
    [MemberData(nameof(ServerProblems))]
    public async Task Api401_RefreshServerProblem_ShowsNoConnection_KeepsSession_NextCallRecovers(Func<HttpRequestMessage, Task<HttpResponseMessage>> problem)
    {
        await LoginAsync();
        var stored = SecureStorage.Default.Count;
        var ended = 0;
        _auth.SessionEnded += () => ended++;
        _handler.Respond = request => IsTokenRequest(request)
            ? problem(request)
            : Task.FromResult(Json(HttpStatusCode.Unauthorized, string.Empty));
        var api = CreateApi();

        var ex = await Assert.ThrowsAsync<ApiException>(api.GetTenantsAsync);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Equal("Brak połączenia z serwerem. Spróbuj ponownie po powrocie sieci.", ex.Message);
        Assert.True(ApiException.IsNetworkFailure(ex));
        Assert.Equal(2, _handler.Requests.Count);
        Assert.Equal(0, ended);
        Assert.Equal(stored, SecureStorage.Default.Count);

        _handler.Respond = request => Task.FromResult(IsTokenRequest(request)
            ? Json(HttpStatusCode.OK, """{"access_token":"a2","refresh_token":"r2","expires_in":300}""")
            : request.Headers.Authorization?.Parameter == "a2"
                ? Json(HttpStatusCode.OK, "[]")
                : Json(HttpStatusCode.Unauthorized, string.Empty));

        Assert.Empty(await api.GetTenantsAsync());
    }

    [Fact]
    public async Task Api401_AfterSuccessfulRefresh_OnePostPerCall_NoLoop_KeepsSession_ShowsTokenRejected()
    {
        await LoginAsync();
        var stored = SecureStorage.Default.Count;
        var ended = 0;
        _auth.SessionEnded += () => ended++;
        _handler.Respond = request => Task.FromResult(IsTokenRequest(request)
            ? Json(HttpStatusCode.OK, TokenJson)
            : Json(HttpStatusCode.Unauthorized, string.Empty));
        var api = CreateApi();

        for (var call = 0; call < 3; call++)
        {
            var ex = await Assert.ThrowsAsync<ApiException>(() => api.GetTenantsAsync());
            Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
            Assert.Contains("Serwer nie przyjął logowania", ex.Message);
            Assert.Contains("wyloguj się", ex.Message);
            Assert.False(ApiException.IsNetworkFailure(ex));
        }

        Assert.Equal(9, _handler.Requests.Count);
        Assert.Equal(3, _handler.Requests.Count(IsTokenRequest));
        Assert.Equal(0, ended);
        Assert.Equal(stored, SecureStorage.Default.Count);
    }

    private const string UserBTokens = """{"access_token":"b-access","refresh_token":"b-refresh","expires_in":300}""";

    private static bool IsLogoutRequest(HttpRequestMessage request) => request.RequestUri!.AbsolutePath.EndsWith("/logout");

    private async Task SwitchToUserBAsync()
    {
        await _auth.LogoutAsync();
        Assert.True(await _auth.LoginWithCredentialsAsync("b@b.pl", "x"));
    }

    private IEnumerable<HttpRequestMessage> ApiRequests => _handler.Requests.Where(r => r.RequestUri!.AbsolutePath.StartsWith("/api/"));

    [Fact]
    public async Task OneApiInstance_UserSwitch_EachRequestCarriesThatUsersToken()
    {
        await LoginAsync();
        var http = new HttpClient(_handler);
        var api = new ApiService(http, _auth, _settings, NullLogger<ApiService>.Instance);
        _handler.Respond = request => Task.FromResult(request.RequestUri!.AbsolutePath.StartsWith("/api/")
            ? Json(HttpStatusCode.OK, "[]")
            : IsLogoutRequest(request) ? Json(HttpStatusCode.NoContent, string.Empty) : Json(HttpStatusCode.OK, UserBTokens));

        await api.GetTenantsAsync();
        await SwitchToUserBAsync();
        await api.GetTenantsAsync();

        Assert.Equal(["new-access", "b-access"], ApiRequests.Select(r => r.Headers.Authorization?.Parameter));
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task AfterSessionEnded_NextRequestCarriesNoBearer()
    {
        await LoginAsync();
        var api = CreateApi();
        _handler.Respond = request => Task.FromResult(IsTokenRequest(request)
            ? Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""")
            : Json(HttpStatusCode.Unauthorized, string.Empty));

        await Assert.ThrowsAsync<ApiException>(api.GetTenantsAsync);
        _handler.Requests.Clear();
        var ex = await Assert.ThrowsAsync<ApiException>(api.GetTenantsAsync);

        Assert.Equal("Sesja wygasła. Zaloguj się ponownie.", ex.Message);
        Assert.Null(Assert.Single(_handler.Requests).Headers.Authorization);
    }

    [Fact]
    public async Task RequestAfterLogout_CarriesNoBearer_NoRefresh()
    {
        await LoginAsync();
        var api = CreateApi();
        _handler.Respond = request => Task.FromResult(request.RequestUri!.AbsolutePath.StartsWith("/api/")
            ? request.Headers.Authorization is null ? Json(HttpStatusCode.Unauthorized, string.Empty) : Json(HttpStatusCode.OK, "[]")
            : Json(HttpStatusCode.NoContent, string.Empty));
        await api.GetTenantsAsync();
        await _auth.LogoutAsync();
        _handler.Requests.Clear();

        var ex = await Assert.ThrowsAsync<ApiException>(() => api.DeleteTenantAsync(Guid.NewGuid()));

        Assert.Equal("Sesja wygasła. Zaloguj się ponownie.", ex.Message);
        Assert.Null(Assert.Single(_handler.Requests).Headers.Authorization);
    }

    [Fact]
    public async Task Late401_AfterLogout_NoRefresh_NoRetry_SessionExpired()
    {
        await LoginAsync();
        var release = new TaskCompletionSource();
        _handler.Respond = async request =>
        {
            if (IsLogoutRequest(request))
                return Json(HttpStatusCode.NoContent, string.Empty);
            await release.Task;
            return Json(HttpStatusCode.Unauthorized, string.Empty);
        };

        var delete = CreateApi().DeleteTenantAsync(Guid.NewGuid());
        await Task.Delay(50);
        await _auth.LogoutAsync();
        release.SetResult();

        var ex = await Assert.ThrowsAsync<ApiException>(() => delete.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("Sesja wygasła. Zaloguj się ponownie.", ex.Message);
        Assert.Single(ApiRequests);
        Assert.DoesNotContain(_handler.Requests, IsTokenRequest);
    }

    [Fact]
    public async Task Late401_AfterUserSwitch_NotRetriedWithNextUsersToken()
    {
        await LoginAsync();
        var release = new TaskCompletionSource();
        _handler.Respond = async request =>
        {
            if (IsLogoutRequest(request))
                return Json(HttpStatusCode.NoContent, string.Empty);
            if (IsTokenRequest(request))
                return Json(HttpStatusCode.OK, UserBTokens);
            await release.Task;
            return Json(HttpStatusCode.Unauthorized, string.Empty);
        };

        var delete = CreateApi().DeleteTenantAsync(Guid.NewGuid());
        await Task.Delay(50);
        await SwitchToUserBAsync();
        _handler.Bodies.Clear();
        release.SetResult();

        var ex = await Assert.ThrowsAsync<ApiException>(() => delete.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal("new-access", Assert.Single(ApiRequests).Headers.Authorization?.Parameter);
        Assert.Empty(_handler.Bodies);
        Assert.Equal("b-access", await _auth.GetAccessTokenAsync());
    }

    [Fact]
    public async Task UserSwitchDuringForcedRefresh_NotRetriedWithNextUsersToken()
    {
        await LoginAsync();
        var refreshSent = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var firstTokenPost = true;
        _handler.Respond = async request =>
        {
            if (IsLogoutRequest(request))
                return Json(HttpStatusCode.NoContent, string.Empty);
            if (!IsTokenRequest(request))
                return Json(HttpStatusCode.Unauthorized, string.Empty);
            if (!firstTokenPost)
                return Json(HttpStatusCode.OK, UserBTokens);
            firstTokenPost = false;
            refreshSent.SetResult();
            await release.Task;
            return Json(HttpStatusCode.OK, """{"access_token":"a2","refresh_token":"r2","expires_in":300}""");
        };

        var delete = CreateApi().DeleteTenantAsync(Guid.NewGuid());
        await refreshSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await SwitchToUserBAsync();
        release.SetResult();

        var ex = await Assert.ThrowsAsync<ApiException>(() => delete.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("Sesja wygasła. Zaloguj się ponownie.", ex.Message);
        Assert.Single(ApiRequests);
        Assert.Equal("b-access", await _auth.GetAccessTokenAsync());
    }

    [Fact]
    public async Task UserSwitchWhileWaitingForToken_RequestNeverGoesOut()
    {
        await LoginExpiringAsync();
        var refreshSent = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var firstTokenPost = true;
        _handler.Respond = async request =>
        {
            if (IsLogoutRequest(request))
                return Json(HttpStatusCode.NoContent, string.Empty);
            if (!firstTokenPost)
                return Json(HttpStatusCode.OK, UserBTokens);
            firstTokenPost = false;
            refreshSent.SetResult();
            await release.Task;
            return Json(HttpStatusCode.OK, """{"access_token":"a2","refresh_token":"r2","expires_in":300}""");
        };

        var delete = CreateApi().DeleteTenantAsync(Guid.NewGuid());
        await refreshSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await SwitchToUserBAsync();
        release.SetResult();

        var ex = await Assert.ThrowsAsync<ApiException>(() => delete.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("Sesja wygasła. Zaloguj się ponownie.", ex.Message);
        Assert.Empty(ApiRequests);
    }

    [Fact]
    public async Task ServerSwitch_NewServerTokensLoaded_Late401NotRefreshedNorRetried()
    {
        await LoginAsync();
        UseServer("other.example.com");
        _handler.Respond = _ => Task.FromResult(Json(HttpStatusCode.OK, UserBTokens));
        Assert.True(await _auth.LoginWithCredentialsAsync("b@b.pl", "x"));
        UseServer("ksef.example.com");

        var sent = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        _handler.Respond = async request =>
        {
            if (request.Headers.Authorization?.Parameter == "b-access")
                return Json(HttpStatusCode.OK, "[]");
            sent.SetResult();
            await release.Task;
            return Json(HttpStatusCode.Unauthorized, string.Empty);
        };
        var api = CreateApi();
        var delete = api.DeleteTenantAsync(Guid.NewGuid());
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

        UseServer("other.example.com");
        Assert.Equal("b-access", await _auth.GetAccessTokenAsync());
        _handler.Requests.Clear();
        _handler.Bodies.Clear();
        release.SetResult();

        var ex = await Assert.ThrowsAsync<ApiException>(() => delete.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("Sesja wygasła. Zaloguj się ponownie.", ex.Message);
        Assert.Empty(_handler.Requests);

        Assert.Empty(await api.GetTenantsAsync());
        Assert.Equal("other.example.com", Assert.Single(_handler.Requests).RequestUri!.Host);
        Assert.Equal("b-access", _handler.Requests[0].Headers.Authorization?.Parameter);
    }

    private void UseServer(string host)
    {
        _settings.ServerUrl.Returns($"https://{host}");
        _settings.Authority.Returns($"https://{host}/auth/realms/openksef");
    }

    private async Task AssertNoBearerRequestNotRetriedAcrossLoginAsync(Func<Task<bool>> login, string tokens = UserBTokens)
    {
        var sent = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        _handler.Respond = async request =>
        {
            if (request.Method != HttpMethod.Delete)
                return Json(HttpStatusCode.OK, tokens);
            sent.SetResult();
            await release.Task;
            return Json(HttpStatusCode.Unauthorized, string.Empty);
        };

        var delete = CreateApi().DeleteTenantAsync(Guid.NewGuid());
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await login());
        _handler.Bodies.Clear();
        release.SetResult();

        var ex = await Assert.ThrowsAsync<ApiException>(() => delete.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("Sesja wygasła. Zaloguj się ponownie.", ex.Message);
        Assert.Null(Assert.Single(_handler.Requests, r => r.Method == HttpMethod.Delete).Headers.Authorization);
        Assert.Empty(_handler.Bodies);
    }

    [Fact]
    public Task FirstPasswordLogin_DuringNoBearerRequest_Late401NotRetriedAsNewUser() =>
        AssertNoBearerRequestNotRetriedAcrossLoginAsync(() => _auth.LoginWithCredentialsAsync("b@b.pl", "x"));

    [Fact]
    public Task SetupTokenRedeem_DuringNoBearerRequest_Late401NotRetriedAsNewUser() =>
        AssertNoBearerRequestNotRetriedAcrossLoginAsync(
            () => _auth.RedeemSetupTokenAsync("https://ksef.example.com", "setup"),
            """{"accessToken":"b-access","refreshToken":"b-refresh","expiresIn":300}""");

    [Fact]
    public Task GoogleLogin_DuringNoBearerRequest_Late401NotRetriedAsNewUser()
    {
        const string authority = "https://ksef.example.com/auth/realms/openksef";
        typeof(AuthService).GetField("_oidcClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_auth, new OidcClient(new OidcClientOptions
        {
            Authority = authority,
            ClientId = "openksef-mobile",
            Scope = "openid",
            RedirectUri = "openksef://auth/callback",
            Browser = _browser,
            BackchannelHandler = _handler,
            LoadProfile = false,
            DisablePushedAuthorization = true,
            IdentityTokenValidator = new AcceptAnyIdentityToken(),
            ProviderInformation = new ProviderInformation
            {
                IssuerName = authority,
                AuthorizeEndpoint = $"{authority}/protocol/openid-connect/auth",
                TokenEndpoint = $"{authority}/protocol/openid-connect/token",
                KeySet = new JsonWebKeySet()
            }
        }));
        _browser.InvokeAsync(Arg.Any<BrowserOptions>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var state = Regex.Match(call.Arg<BrowserOptions>().StartUrl, "state=([^&]+)").Groups[1].Value;
            return new BrowserResult { ResultType = BrowserResultType.Success, Response = $"openksef://auth/callback?code=c&state={state}" };
        });

        return AssertNoBearerRequestNotRetriedAcrossLoginAsync(
            _auth.LoginWithGoogleAsync,
            """{"access_token":"b-access","refresh_token":"b-refresh","id_token":"i","token_type":"Bearer","expires_in":300}""");
    }

    private sealed class AcceptAnyIdentityToken : IIdentityTokenValidator
    {
        public Task<IdentityTokenValidationResult> ValidateAsync(string identityToken, OidcClientOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(new IdentityTokenValidationResult { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "b")], "test")) });
    }

    [Fact]
    public async Task LogoutWhileWaitingForToken_RequestNeverGoesOut()
    {
        await LoginExpiringAsync();
        var refreshSent = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        _handler.Respond = async request =>
        {
            if (IsLogoutRequest(request))
                return Json(HttpStatusCode.NoContent, string.Empty);
            if (request.RequestUri!.AbsolutePath.StartsWith("/api/"))
                return Json(HttpStatusCode.Unauthorized, string.Empty);
            refreshSent.SetResult();
            await release.Task;
            return Json(HttpStatusCode.OK, """{"access_token":"a2","refresh_token":"r2","expires_in":300}""");
        };

        var delete = CreateApi().DeleteTenantAsync(Guid.NewGuid());
        await refreshSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _auth.LogoutAsync();
        release.SetResult();

        var ex = await Assert.ThrowsAsync<ApiException>(() => delete.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("Sesja wygasła. Zaloguj się ponownie.", ex.Message);
        Assert.Empty(ApiRequests);
    }

    [Fact]
    public async Task SignOut_DevicesListSlowerThanFiveSeconds_DeleteStillCarriesBearer()
    {
        await LoginAsync();
        Preferences.Default.Clear();
        Preferences.Default.Set("device_push_token", "fcm-token");
        var deviceId = Guid.NewGuid();
        _handler.Respond = async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                await Task.Delay(TimeSpan.FromSeconds(6));
                return Json(HttpStatusCode.OK, $$"""[{"id":"{{deviceId}}","token":"fcm-token"}]""");
            }

            return Json(request.Method == HttpMethod.Delete && request.Headers.Authorization is null ? HttpStatusCode.Unauthorized : HttpStatusCode.NoContent, string.Empty);
        };

        await Navigation(new DeviceTokenService(CreateApi())).SignOutAsync();

        var delete = Assert.Single(_handler.Requests, r => r.Method == HttpMethod.Delete);
        Assert.EndsWith($"/api/devices/{deviceId}", delete.RequestUri!.AbsolutePath);
        Assert.Equal("new-access", delete.Headers.Authorization?.Parameter);
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
