using System.Reflection;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Mobile.Services;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

public class NotificationHubServiceTests
{
    private static readonly FieldInfo ConnectionField = typeof(NotificationHubService).GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [Fact]
    public async Task StopOutlivingSignOutWait_DoesNotDisposeNextLoginConnection()
    {
        var hungToken = new TaskCompletionSource<string?>();
        var tokenAsked = new TaskCompletionSource();
        var auth = Substitute.For<IAuthService>();
        auth.GetAccessTokenAsync().Returns(_ => { tokenAsked.TrySetResult(); return hungToken.Task; }, _ => Task.FromResult<string?>(null));
        var settings = Substitute.For<IServerSettingsService>();
        settings.ServerUrl.Returns("http://127.0.0.1:1");
        var hub = new NotificationHubService(auth, settings, Substitute.For<IDeviceTokenService>(), NullLogger<NotificationHubService>.Instance);

        var firstStart = hub.StartAsync();
        await tokenAsked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var first = (HubConnection)ConnectionField.GetValue(hub)!;

        var hungStop = hub.StopAsync();
        await Assert.ThrowsAsync<TimeoutException>(() => hungStop.WaitAsync(TimeSpan.FromMilliseconds(500)));

        await hub.StartAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var next = (HubConnection)ConnectionField.GetValue(hub)!;
        Assert.NotSame(first, next);

        hungToken.SetResult(null);
        await hungStop.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Same(next, ConnectionField.GetValue(hub));
        await next.StartAsync().ContinueWith(t => Assert.IsNotType<ObjectDisposedException>(t.Exception?.InnerException));

        await hub.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }
}
