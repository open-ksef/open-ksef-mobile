using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace OpenKSeF.Mobile.Services;

/// <summary>
/// Maintains a SignalR connection to the API's notification hub.
/// Receives real-time push notifications when the app is connected,
/// without requiring Firebase/APNs.
/// </summary>
public class NotificationHubService : INotificationHubService, IAsyncDisposable
{
    private readonly IAuthService _authService;
    private readonly IServerSettingsService _serverSettings;
    private readonly ILogger<NotificationHubService> _logger;
    private HubConnection? _connection;
    private bool _isStarted;
    private CancellationTokenSource? _reconnectCts;

    public NotificationHubService(
        IAuthService authService,
        IServerSettingsService serverSettings,
        ILogger<NotificationHubService> logger)
    {
        _authService = authService;
        _serverSettings = serverSettings;
        _logger = logger;
    }

    public event Action<string, string, IDictionary<string, string>?>? NotificationReceived;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task StartAsync()
    {
        if (_isStarted) return;
        _isStarted = true;

        _reconnectCts = new CancellationTokenSource();
        await ConnectAsync(_reconnectCts.Token);
    }

    public async Task StopAsync()
    {
        _isStarted = false;
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;

        if (_connection is not null)
        {
            try { await _connection.StopAsync(); }
            catch { /* best-effort */ }
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        var serverUrl = _serverSettings.ServerUrl.TrimEnd('/');
        var hubUrl = $"{serverUrl}/hubs/notifications";

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = async () => await _authService.GetAccessTokenAsync();
            })
            .WithAutomaticReconnect(new RetryPolicy())
            .Build();

        _connection.On<NotificationPayload>("ReceiveNotification", payload =>
        {
            _logger.LogDebug("SignalR notification received: {Title}", payload.Title);
            NotificationReceived?.Invoke(payload.Title, payload.Body, payload.Data);
            ShowLocalNotification(payload.Title, payload.Body);
        });

        _connection.Closed += async (error) =>
        {
            if (!_isStarted) return;
            _logger.LogWarning(error, "SignalR connection closed, will reconnect");
            await Task.CompletedTask;
        };

        _connection.Reconnected += (connectionId) =>
        {
            _logger.LogInformation("SignalR reconnected: {ConnectionId}", connectionId);
            return Task.CompletedTask;
        };

        try
        {
            await _connection.StartAsync(ct);
            _logger.LogInformation("SignalR notification hub connected");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "SignalR initial connection failed, will retry");
            _ = RetryConnectAsync(ct);
        }
    }

    private async Task RetryConnectAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(5);
        var maxDelay = TimeSpan.FromMinutes(2);

        while (_isStarted && !ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, ct);
                if (_connection?.State == HubConnectionState.Disconnected)
                {
                    await _connection.StartAsync(ct);
                    _logger.LogInformation("SignalR reconnected after retry");
                    return;
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "SignalR retry failed, backing off");
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelay.TotalSeconds));
            }
        }
    }

    private static void ShowLocalNotification(string title, string body)
    {
#if ANDROID
        var context = Android.App.Application.Context;
        const string channelId = "openksef_invoices";

        var notificationManager = (Android.App.NotificationManager?)
            context.GetSystemService(Android.Content.Context.NotificationService);
        if (notificationManager is null) return;

        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
        {
            var channel = new Android.App.NotificationChannel(
                channelId, "Faktury", Android.App.NotificationImportance.Default)
            {
                Description = "Powiadomienia o nowych fakturach z KSeF"
            };
            notificationManager.CreateNotificationChannel(channel);
        }

        var intent = new Android.Content.Intent(context, typeof(MainActivity));
        intent.AddFlags(Android.Content.ActivityFlags.ClearTop);
        var pendingIntent = Android.App.PendingIntent.GetActivity(
            context, 0, intent,
            Android.App.PendingIntentFlags.OneShot | Android.App.PendingIntentFlags.Immutable);

        var builder = new Android.App.Notification.Builder(context, channelId)
            .SetContentTitle(title)
            .SetContentText(body)
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            .SetAutoCancel(true)
            .SetContentIntent(pendingIntent);

        notificationManager.Notify(DateTime.UtcNow.Millisecond, builder!.Build());
#elif IOS
        var content = new UserNotifications.UNMutableNotificationContent
        {
            Title = title,
            Body = body,
            Sound = UserNotifications.UNNotificationSound.Default
        };
        var trigger = UserNotifications.UNTimeIntervalNotificationTrigger.CreateTrigger(1, false);
        var request = UserNotifications.UNNotificationRequest.FromIdentifier(
            Guid.NewGuid().ToString(), content, trigger);
        UserNotifications.UNUserNotificationCenter.Current.AddNotificationRequest(request, null);
#endif
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }

    private sealed record NotificationPayload(string Title, string Body, Dictionary<string, string>? Data);

    private sealed class RetryPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Delays =
        [
            TimeSpan.FromSeconds(0),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60),
        ];

        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            var idx = Math.Min(retryContext.PreviousRetryCount, Delays.Length - 1);
            return Delays[idx];
        }
    }
}
