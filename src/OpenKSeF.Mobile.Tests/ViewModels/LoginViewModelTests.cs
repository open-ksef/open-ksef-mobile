using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class LoginViewModelTests
{
    private readonly IAuthService _auth = Substitute.For<IAuthService>();
    private readonly IPostLoginNavigationService _postLoginNav = Substitute.For<IPostLoginNavigationService>();

    public LoginViewModelTests()
    {
        Shell.Current.Reset();
        Preferences.Default.Clear();
    }

    private static IServerSettingsService CreateServerSettings(string url, bool isConfigured)
    {
        var settings = Substitute.For<IServerSettingsService>();
        settings.ServerUrl.Returns(url);
        settings.IsConfigured.Returns(isConfigured);
        settings.TryUpdateServerUrl(Arg.Any<string>(), out Arg.Any<string>(), out Arg.Any<string?>())
            .Returns(x =>
            {
                x[1] = url;
                x[2] = (string?)null;
                return true;
            });
        return settings;
    }

    private LoginViewModel CreateViewModel(IServerSettingsService? serverSettings = null) =>
        new(_auth, serverSettings ?? CreateServerSettings("https://default.com", isConfigured: true), _postLoginNav);

    [Fact]
    public async Task RestoreSession_ShowsFormAfterwards()
    {
        var vm = CreateViewModel();
        Assert.True(vm.IsRestoringSession);

        await vm.RestoreSessionAsync();

        await _postLoginNav.Received(1).RestoreSessionAsync();
        Assert.False(vm.IsRestoringSession);
    }

    [Theory]
    [InlineData("https://example.com.", "example.com")]
    [InlineData("https://bаnk.pl", "xn--bnk-6cd.pl")]
    [InlineData("http://10.0.2.2:8080", "10.0.2.2:8080")]
    [InlineData("https://example.com:443", "example.com")]
    public void ActiveServerHost_ShowsNormalizedHost(string url, string expected)
    {
        Assert.Equal(expected, CreateViewModel(CreateServerSettings(url, isConfigured: true)).ActiveServerHost);
    }

    [Theory]
    [InlineData("", false, false)]
    [InlineData("https://ksef.example.com", true, true)]
    public void HasActiveServerHost_HiddenOnFreshInstall(string url, bool isConfigured, bool expected)
    {
        Assert.Equal(expected, CreateViewModel(CreateServerSettings(url, isConfigured)).HasActiveServerHost);
    }

    [Fact]
    public void Constructor_InitializesFromServerSettings()
    {
        var vm = CreateViewModel(CreateServerSettings("https://initial.com", isConfigured: true));

        Assert.Equal("https://initial.com", vm.ServerUrl);
        Assert.Equal("initial.com", vm.ActiveServerHost);
        Assert.False(vm.IsServerVisible);
    }

    [Fact]
    public void Constructor_ShowsServerSection_WhenNotConfigured()
    {
        Assert.True(CreateViewModel(CreateServerSettings("", isConfigured: false)).IsServerVisible);
    }

    [Fact]
    public void RefreshServerSettings_PicksUpChangedServer()
    {
        var serverSettings = CreateServerSettings("https://old.com", isConfigured: true);
        var vm = CreateViewModel(serverSettings);

        serverSettings.ServerUrl.Returns("https://new.com");
        serverSettings.IsConfigured.Returns(false);
        vm.RefreshServerSettings();

        Assert.Equal("https://new.com", vm.ServerUrl);
        Assert.True(vm.IsServerVisible);
    }

    [Fact]
    public async Task LoginWithCredentials_Success_NavigatesPostLogin()
    {
        _auth.LoginWithCredentialsAsync("user@example.com", "secret").Returns(AuthResult.Success);
        var vm = CreateViewModel();
        vm.Email = " user@example.com ";
        vm.Password = "secret";

        await vm.LoginWithCredentialsCommand.ExecuteAsync(null);

        await _postLoginNav.Received(1).NavigateAsync();
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task LoginWithCredentials_WrongPassword_ShowsErrorWithoutNavigation()
    {
        var vm = CreateViewModel();
        vm.Email = "user@example.com";
        vm.Password = "wrong";

        await vm.LoginWithCredentialsCommand.ExecuteAsync(null);

        Assert.Equal("Nieprawidłowy email lub hasło.", vm.ErrorMessage);
        await _postLoginNav.DidNotReceive().NavigateAsync();
        Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData(AuthResult.AccountNotSetUp, "Konto wymaga dokończenia konfiguracji. Potwierdź adres e-mail linkiem z wiadomości albo zaloguj się raz w portalu i wykonaj wskazane kroki, potem zaloguj się ponownie.", false)]
    [InlineData(AuthResult.AccountDisabled, "Konto jest wyłączone. Skontaktuj się z administratorem serwera.", false)]
    [InlineData(AuthResult.ClientRejected, "Serwer ksef.example.com odrzucił aplikację mobilną. Skontaktuj się z administratorem serwera.", true)]
    public async Task LoginWithCredentials_AccountOrClientRefused_ShowsSpecificMessage(AuthResult result, string expected, bool serverVisible)
    {
        _auth.LoginWithCredentialsAsync("user@example.com", "secret").Returns(result);
        var vm = CreateViewModel(CreateServerSettings("https://ksef.example.com", isConfigured: true));
        vm.Email = "user@example.com";
        vm.Password = "secret";

        await vm.LoginWithCredentialsCommand.ExecuteAsync(null);

        Assert.Equal(expected, vm.ErrorMessage);
        Assert.Equal(serverVisible, vm.IsServerVisible);
        await _postLoginNav.DidNotReceive().NavigateAsync();
    }

    [Fact]
    public async Task LoginWithGoogle_HttpServer_SaysHttpsRequired()
    {
        _auth.LoginWithGoogleAsync().Returns(AuthResult.GoogleRequiresHttps);
        var vm = CreateViewModel(CreateServerSettings("http://10.0.2.2:8080", isConfigured: true));

        await vm.LoginWithGoogleCommand.ExecuteAsync(null);

        Assert.Equal("Logowanie przez Google wymaga serwera z adresem https://. Zaloguj się emailem i hasłem.", vm.ErrorMessage);
    }

    [Fact]
    public async Task LoginWithCredentials_EmptyFields_DoesNotCallAuth()
    {
        var vm = CreateViewModel();

        await vm.LoginWithCredentialsCommand.ExecuteAsync(null);

        Assert.Equal("Podaj email i hasło.", vm.ErrorMessage);
        await _auth.DidNotReceiveWithAnyArgs().LoginWithCredentialsAsync(default!, default!);
    }

    [Theory]
    [InlineData("short", "short", "Hasło musi mieć co najmniej 8 znaków.")]
    [InlineData("password1", "password2", "Hasła nie są identyczne.")]
    public async Task Register_InvalidPassword_DoesNotCallAuth(string password, string confirm, string expectedError)
    {
        var vm = CreateViewModel();
        vm.RegEmail = "new@example.com";
        vm.RegPassword = password;
        vm.RegConfirmPassword = confirm;

        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(expectedError, vm.ErrorMessage);
        await _auth.DidNotReceiveWithAnyArgs().RegisterAsync(default!, default!, default, default);
    }
}

[Collection("Shell")]
public class PostLoginNavigationServiceTests
{
    private readonly IApiService _api = Substitute.For<IApiService>();
    private readonly IAuthService _auth = Substitute.For<IAuthService>();
    private readonly IDeviceTokenService _deviceTokenService = Substitute.For<IDeviceTokenService>();
    private readonly INotificationHubService _notificationHub = Substitute.For<INotificationHubService>();
    private readonly LocalDbService _localDb = new();
    private readonly PostLoginNavigationService _nav;

    public PostLoginNavigationServiceTests()
    {
        Shell.Current.Reset();
        Preferences.Default.Clear();
        var serverSettings = Substitute.For<IServerSettingsService>();
        serverSettings.ServerUrl.Returns("https://ksef.example.com");
        _api.GetMeAsync().Returns(new UserInfo { UserId = "u1" });
        _deviceTokenService.UnregisterCurrentDeviceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _nav = new PostLoginNavigationService(_api, _auth, _deviceTokenService, _notificationHub, serverSettings, _localDb);
    }

    private void OnboardingComplete(bool complete) =>
        _api.GetOnboardingStatusAsync().Returns(new OnboardingStatusDto { IsComplete = complete });

    [Fact]
    public async Task SignOut_DropsPendingNotificationTap()
    {
        NotificationHubService.OnNotificationTapped(null, Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

        await _nav.SignOutAsync();

        Assert.False(NotificationHubService.HasTappedTenant);
    }

    [Fact]
    public async Task SessionEnded_KeepsPendingNotificationTapForNextSignIn()
    {
        var tenant = Guid.NewGuid();
        NotificationHubService.OnNotificationTapped("newInvoicesSummary", tenant.ToString());
        try
        {
            await _nav.HandleSessionEndedAsync();

            Assert.Equal((tenant, (Guid?)null), NotificationHubService.TakeTapped());
        }
        finally
        {
            NotificationHubService.TakeTapped();
        }
    }

    [Fact]
    public async Task SignOut_RemovesInvoiceFilesAndShareCopiesFromCache()
    {
        var cache = FileSystem.CacheDirectory;
        File.WriteAllText(Path.Combine(cache, "FV_1_11_2026.pdf"), "x");
        File.WriteAllText(Path.Combine(cache, "FV_1_11_2026_UPO.xml"), "x");
        Directory.CreateDirectory(Path.Combine(cache, "share-copy"));
        File.WriteAllText(Path.Combine(cache, "share-copy", "FV_1_11_2026.xml"), "x");

        await _nav.SignOutAsync();

        Assert.Empty(Directory.GetFileSystemEntries(cache));
        await _auth.Received(1).LogoutAsync();
    }

    [Fact]
    public async Task SignOut_FileThatCannotBeDeleted_StillLogsOut_AndRemovesTheRest()
    {
        var cache = FileSystem.CacheDirectory;
        var locked = Path.Combine(cache, "locked.pdf");
        var other = Path.Combine(cache, "other.pdf");
        File.WriteAllText(other, "x");
        using (new FileStream(locked, FileMode.Create, FileAccess.Write, FileShare.None))
            await _nav.SignOutAsync();

        await _auth.Received(1).LogoutAsync();
        Assert.False(File.Exists(other));
        File.Delete(locked);
    }

    [Fact]
    public async Task SignOut_LogoutUnderwayFromHubStop_ClearedWhenLogoutThrows()
    {
        var duringHubStop = false;
        _notificationHub.StopAsync().Returns(_ =>
        {
            duringHubStop = _auth.LogoutUnderway;
            return Task.CompletedTask;
        });
        _auth.LogoutAsync().Returns(Task.FromException(new InvalidOperationException()));

        await Assert.ThrowsAsync<InvalidOperationException>(_nav.SignOutAsync);

        Assert.True(duringHubStop);
        Assert.False(_auth.LogoutUnderway);
    }

    [Fact]
    public async Task SignOut_HubStopHangs_StillLogsOutAndClearsLogoutUnderway()
    {
        _notificationHub.StopAsync().Returns(new TaskCompletionSource().Task);

        await _nav.SignOutAsync().WaitAsync(TimeSpan.FromSeconds(30));

        await _auth.Received(1).LogoutAsync();
        Assert.False(_auth.LogoutUnderway);
    }

    [Fact]
    public async Task SessionEnded_RemovesInvoiceFilesFromCache()
    {
        var file = Path.Combine(FileSystem.CacheDirectory, "FV_2_11_2026.pdf");
        File.WriteAllText(file, "x");

        await _nav.HandleSessionEndedAsync();

        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task SessionEnded_StopsHub_TellsUser_AndOpensLogin_KeepingLocalCache()
    {
        Application.Current!.Windows[0].Page = null;

        await _nav.HandleSessionEndedAsync();

        await _notificationHub.Received(1).StopAsync();
        Assert.Equal("Zaloguj się ponownie.", Shell.Current.LastAlertMessage);
        Assert.IsType<OpenKSeF.Mobile.AppShell>(Application.Current.Windows[0].Page);
        await _auth.DidNotReceive().LogoutAsync();
    }

    [Fact]
    public async Task Navigate_OnboardingComplete_RegistersDeviceStartsHubAndOpensInvoices()
    {
        OnboardingComplete(true);

        await _nav.NavigateAsync();

        await _deviceTokenService.Received(1).EnsureDeviceRegisteredAsync();
        await _notificationHub.Received(1).StartAsync();
        Assert.Equal("//main/invoices", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Navigate_PushOff_DoesNotStartHub()
    {
        OnboardingComplete(true);
        _deviceTokenService.PushDisabledByUser.Returns(true);

        await _nav.NavigateAsync();

        await _notificationHub.DidNotReceive().StartAsync();
        Assert.Equal("//main/invoices", Shell.Current.LastRoute);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task RestoreUnverified_StartsHubOnlyWhenPushOn(bool pushOff, int hubStarts)
    {
        _auth.RestoreSessionAsync().Returns(SessionRestoreResult.Unverified);
        _deviceTokenService.PushDisabledByUser.Returns(pushOff);

        await _nav.RestoreSessionAsync();

        await _notificationHub.Received(hubStarts).StartAsync();
    }

    [Fact]
    public async Task Navigate_HubThatNeverConnects_DoesNotBlockNavigation()
    {
        OnboardingComplete(true);
        _notificationHub.StartAsync().Returns(new TaskCompletionSource().Task);

        await _nav.NavigateAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("//main/invoices", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Navigate_HubThatFails_DoesNotBlockNavigation()
    {
        OnboardingComplete(true);
        _notificationHub.StartAsync().ThrowsAsync(new Exception("hub"));

        await _nav.NavigateAsync();

        Assert.Equal("//main/invoices", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Navigate_OnboardingNeeded_SkipsDeviceRegistration()
    {
        OnboardingComplete(false);

        await _nav.NavigateAsync();

        await _deviceTokenService.DidNotReceive().EnsureDeviceRegisteredAsync();
        await _notificationHub.DidNotReceive().StartAsync();
        Assert.Equal("//onboarding", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Navigate_DeviceRegistrationFails_StillNavigates()
    {
        OnboardingComplete(true);
        _deviceTokenService.EnsureDeviceRegisteredAsync().ThrowsAsync(new Exception("Network error"));

        await _nav.NavigateAsync();

        await _notificationHub.Received(1).StartAsync();
        Assert.Equal("//main/invoices", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Navigate_DifferentUser_ClearsCachedInvoicesOfPreviousUser()
    {
        OnboardingComplete(true);
        var tenantId = Guid.NewGuid();
        await _localDb.EnsureCurrentUserAsync("https://ksef.example.com|previous-user");
        await _localDb.SaveInvoicesAsync(tenantId, [new InvoiceDto { Id = Guid.NewGuid(), IssueDate = DateTime.UtcNow }]);
        Assert.Single(await _localDb.GetInvoicesAsync(tenantId));

        await _nav.NavigateAsync();

        Assert.Empty(await _localDb.GetInvoicesAsync(tenantId));
    }

    [Fact]
    public async Task Navigate_SameUser_KeepsOfflineCache()
    {
        OnboardingComplete(true);
        var tenantId = Guid.NewGuid();
        await _localDb.EnsureCurrentUserAsync("https://ksef.example.com|u1");
        await _localDb.SaveInvoicesAsync(tenantId, [new InvoiceDto { Id = Guid.NewGuid(), IssueDate = DateTime.UtcNow }]);

        await _nav.NavigateAsync();

        Assert.Single(await _localDb.GetInvoicesAsync(tenantId));
    }

    [Fact]
    public async Task Restore_ValidSession_OpensInvoicesAndRegistersDevice()
    {
        OnboardingComplete(true);
        _auth.RestoreSessionAsync().Returns(SessionRestoreResult.Restored);

        await _nav.RestoreSessionAsync();

        await _deviceTokenService.Received(1).EnsureDeviceRegisteredAsync();
        Assert.Equal("//main/invoices", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Restore_OnboardingNeeded_OpensOnboarding()
    {
        OnboardingComplete(false);
        _auth.RestoreSessionAsync().Returns(SessionRestoreResult.Restored);

        await _nav.RestoreSessionAsync();

        Assert.Equal("//onboarding", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Restore_DeadSession_StaysOnLogin()
    {
        _auth.RestoreSessionAsync().Returns(SessionRestoreResult.None);

        await _nav.RestoreSessionAsync();

        Assert.Null(Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Restore_Offline_OpensInvoicesAndKeepsCacheWithoutCallingApi()
    {
        var tenantId = Guid.NewGuid();
        await _localDb.EnsureCurrentUserAsync("https://ksef.example.com|u1");
        await _localDb.SaveInvoicesAsync(tenantId, [new InvoiceDto { Id = Guid.NewGuid(), IssueDate = DateTime.UtcNow }]);
        _auth.RestoreSessionAsync().Returns(SessionRestoreResult.Unverified);

        await _nav.RestoreSessionAsync();

        Assert.Equal("//main/invoices", Shell.Current.LastRoute);
        Assert.Single(await _localDb.GetInvoicesAsync(tenantId));
        await _api.DidNotReceive().GetMeAsync();
        await _auth.DidNotReceive().LogoutAsync();
    }

    [Fact]
    public async Task Restore_ApiDownAfterRefresh_KeepsCache()
    {
        var tenantId = Guid.NewGuid();
        await _localDb.EnsureCurrentUserAsync("https://ksef.example.com|u1");
        await _localDb.SaveInvoicesAsync(tenantId, [new InvoiceDto { Id = Guid.NewGuid(), IssueDate = DateTime.UtcNow }]);
        _auth.RestoreSessionAsync().Returns(SessionRestoreResult.Restored);
        _api.GetMeAsync().ThrowsAsync(new ApiException(System.Net.HttpStatusCode.ServiceUnavailable, "down"));

        await _nav.RestoreSessionAsync();

        Assert.Single(await _localDb.GetInvoicesAsync(tenantId));
        Assert.Equal("//main/invoices", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task SignOut_UnregistersDeviceStopsHubAndLogsOut()
    {
        await _nav.SignOutAsync();

        Received.InOrder(() =>
        {
            _notificationHub.StopAsync();
            _deviceTokenService.UnregisterCurrentDeviceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            _auth.LogoutAsync();
        });
    }

    [Fact]
    public async Task Navigate_RegistrationHangs_OpensInvoicesWithoutWaiting()
    {
        OnboardingComplete(true);
        _deviceTokenService.EnsureDeviceRegisteredAsync().Returns(new TaskCompletionSource().Task);

        await _nav.NavigateAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("//main/invoices", Shell.Current.LastRoute);
        await _notificationHub.Received(1).StartAsync();
    }

    [Fact]
    public async Task SignOut_FromAnotherInstance_WaitsForPendingRegistration_ThenRemovesDevice()
    {
        OnboardingComplete(true);
        var post = new TaskCompletionSource();
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).Returns(new List<DeviceTokenDto>());
        _api.RegisterDeviceTokenAsync(Arg.Any<RegisterDeviceTokenRequest>(), Arg.Any<CancellationToken>()).Returns(post.Task);
        var devices = new DeviceTokenService(_api, _auth);
        var serverSettings = Substitute.For<IServerSettingsService>();
        var login = new PostLoginNavigationService(_api, _auth, devices, _notificationHub, serverSettings, _localDb);
        var account = new PostLoginNavigationService(_api, _auth, devices, _notificationHub, serverSettings, _localDb);
        await login.NavigateAsync().WaitAsync(TimeSpan.FromSeconds(5));

        var signOut = account.SignOutAsync();
        var mine = new DeviceTokenDto { Id = Guid.NewGuid(), Token = Preferences.Default.Get("device_stable_id", "") };
        _api.GetDevicesAsync(null, Arg.Any<CancellationToken>()).Returns([mine]);
        post.SetResult();
        await signOut.WaitAsync(TimeSpan.FromSeconds(5));

        await _api.Received(1).UnregisterDeviceAsync(mine.Id, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignOut_HubRegistrationAndUnregisterHang_ShareOneTimeBudget()
    {
        OnboardingComplete(true);
        _deviceTokenService.EnsureDeviceRegisteredAsync().Returns(new TaskCompletionSource().Task);
        _notificationHub.StopAsync().Returns(new TaskCompletionSource().Task);
        _deviceTokenService.UnregisterCurrentDeviceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<bool>().Task);
        await _nav.NavigateAsync();

        var limit = PostLoginNavigationService.SignOutLimit;
        PostLoginNavigationService.SignOutLimit = TimeSpan.FromMilliseconds(500);
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await _nav.SignOutAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1.4), $"sign-out took {watch.Elapsed}");
            await _auth.Received(1).DeferLogoutAsync(Arg.Any<string>());
            await _auth.DidNotReceive().LogoutAsync();
            await _deviceTokenService.Received(1).CompleteSignOutCleanupAsync();
        }
        finally
        {
            PostLoginNavigationService.SignOutLimit = limit;
        }
    }

    [Fact]
    public async Task SignOut_RemovesCachedInvoiceTextFromDatabaseFile()
    {
        var tenantId = Guid.NewGuid();
        await _localDb.SaveInvoicesAsync(tenantId, [new InvoiceDto { Id = Guid.NewGuid(), IssueDate = DateTime.UtcNow, VendorName = "ZZTOPSECRETVENDORZZ" }]);

        await _nav.SignOutAsync();

        using var file = new FileStream(Path.Combine(FileSystem.AppDataDirectory, "openksef.db"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var copy = new MemoryStream();
        file.CopyTo(copy);
        Assert.DoesNotContain("ZZTOPSECRETVENDORZZ", System.Text.Encoding.UTF8.GetString(copy.ToArray()));
    }
}
