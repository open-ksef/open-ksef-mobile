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
    // A proxy that swallows the WebSocket upgrade leaves StartAsync pending forever, so every attempt needs its own limit.
    private static readonly TimeSpan ConnectAttemptTimeout = TimeSpan.FromSeconds(30);

    private readonly IAuthService _authService;
    private readonly IServerSettingsService _serverSettings;
    private readonly IDeviceTokenService _deviceTokenService;
    private readonly ILogger<NotificationHubService> _logger;
    private HubConnection? _connection;
    private bool _isStarted;
    private CancellationTokenSource? _reconnectCts;

    public NotificationHubService(
        IAuthService authService,
        IServerSettingsService serverSettings,
        IDeviceTokenService deviceTokenService,
        ILogger<NotificationHubService> logger)
    {
        _authService = authService;
        _serverSettings = serverSettings;
        _deviceTokenService = deviceTokenService;
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

        var connection = _connection;
        _connection = null;
        if (connection is not null)
        {
            try { await connection.StopAsync(); }
            catch { /* best-effort */ }
            await connection.DisposeAsync();
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

        _connection.On<NotificationPayload>("ReceiveNotification", payload => Receive(payload));

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
            await StartAttemptAsync(ct);
            _logger.LogInformation("SignalR notification hub connected");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "SignalR initial connection failed, will retry");
            _ = RetryConnectAsync(ct);
        }
    }

    private async Task StartAttemptAsync(CancellationToken ct)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attempt.CancelAfter(ConnectAttemptTimeout);
        await _connection!.StartAsync(attempt.Token);
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
                    await StartAttemptAsync(ct);
                    _logger.LogInformation("SignalR reconnected after retry");
                    return;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "SignalR retry failed, backing off");
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelay.TotalSeconds));
            }
        }
    }

    internal bool Receive(NotificationPayload payload)
    {
        _logger.LogDebug("SignalR notification received: {Title}", payload.Title);
        NotificationReceived?.Invoke(payload.Title, payload.Body, payload.Data);
        if (!_deviceTokenService.ShowHubNotification(payload.NativePush))
            return false;

        ShowLocalNotification(payload.Title, payload.Body, payload.Data);
        return true;
    }

    internal const string SummaryType = "newInvoicesSummary";

    private static (Guid TenantId, Guid? InvoiceId)? _tapped;

    /// <summary>A summary tap opens the tenant's received list, a per-invoice tap opens that invoice, now or after sign-in.</summary>
    internal static bool OnNotificationTapped(string? type, string? tenantId, string? invoiceId = null)
    {
        if (!Guid.TryParse(tenantId, out var tenant))
            return false;
        Guid? invoice = type == SummaryType ? null : Guid.TryParse(invoiceId, out var id) ? id : null;
        if (type != SummaryType && invoice is null)
            return false;

        _tapped = (tenant, invoice);
#if ANDROID || IOS
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Shell.Current?.CurrentState?.Location?.OriginalString.StartsWith("//main", StringComparison.Ordinal) != true)
                return;
            try { await Shell.Current.GoToAsync("//main/invoices"); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[OpenKSeF] Notification tap navigation failed: {ex}"); }
            if (Shell.Current.CurrentPage?.BindingContext is ViewModels.InvoiceListViewModel list)
                list.LoadInvoicesCommand.Execute(null);
        });
#endif
        return true;
    }

    internal static bool HasTappedTenant => _tapped is not null;

    internal static (Guid TenantId, Guid? InvoiceId)? TakeTapped()
    {
        var tapped = _tapped;
        _tapped = null;
        return tapped;
    }

    /// <summary>The backend sends one event over SignalR and FCM/APNs; the same key replaces the shown notification instead of adding a second one.</summary>
    internal static void ShowLocalNotification(string title, string body, IDictionary<string, string>? data)
    {
        var key = data is not null && (data.TryGetValue("invoiceId", out var id) || data.TryGetValue("issuedInvoiceId", out id))
            ? id
            : $"{title}\n{body}";
#if ANDROID
        var notificationId = key.GetHashCode();
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
        intent.AddFlags(Android.Content.ActivityFlags.ClearTop | Android.Content.ActivityFlags.SingleTop);
        if (data is not null)
            foreach (var (name, value) in data)
                intent.PutExtra(name, value);
        var pendingIntent = Android.App.PendingIntent.GetActivity(
            context, notificationId, intent,
            Android.App.PendingIntentFlags.OneShot | Android.App.PendingIntentFlags.Immutable);

        var builder = new Android.App.Notification.Builder(context, channelId)
            .SetContentTitle(title)
            .SetContentText(body)
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            .SetAutoCancel(true)
            .SetOnlyAlertOnce(true)
            .SetContentIntent(pendingIntent);

        notificationManager.Notify(notificationId, builder!.Build());
#elif IOS
        var content = new UserNotifications.UNMutableNotificationContent
        {
            Title = title,
            Body = body,
            Sound = UserNotifications.UNNotificationSound.Default
        };
        if (data is not null)
            content.UserInfo = new Foundation.NSDictionary("data", Foundation.NSDictionary.FromObjectsAndKeys(data.Values.ToArray<object>(), data.Keys.ToArray<object>()));
        var trigger = UserNotifications.UNTimeIntervalNotificationTrigger.CreateTrigger(1, false);
        var request = UserNotifications.UNNotificationRequest.FromIdentifier(
            key, content, trigger);
        UserNotifications.UNUserNotificationCenter.Current.AddNotificationRequest(request, null);
#endif
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>NativePush is null from an API older than T-618: the local notification is shown, a duplicate beats none.</summary>
    internal sealed record NotificationPayload(string Title, string Body, Dictionary<string, string>? Data, bool? NativePush = null);

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
