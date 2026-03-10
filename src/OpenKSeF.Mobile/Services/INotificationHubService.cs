namespace OpenKSeF.Mobile.Services;

public interface INotificationHubService
{
    bool IsConnected { get; }
    event Action<string, string, IDictionary<string, string>?>? NotificationReceived;
    Task StartAsync();
    Task StopAsync();
}
