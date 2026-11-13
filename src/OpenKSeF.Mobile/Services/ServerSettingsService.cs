namespace OpenKSeF.Mobile.Services;

public class ServerSettingsService : IServerSettingsService
{
    private const string ServerUrlKey = "server_url";
    private const string RealmPath = "/auth/realms/openksef";

    public ServerSettingsService()
    {
        IsConfigured = ServerUrlRules.TryNormalize(Preferences.Default.Get(ServerUrlKey, string.Empty), ServerUrlRules.AllowDevCleartext, out var stored, out _);
        ServerUrl = IsConfigured ? stored : string.Empty;
    }

    public string ServerUrl { get; private set; }

    public string Authority => $"{ServerUrl}{RealmPath}";

    public bool IsConfigured { get; private set; }

    public bool TryUpdateServerUrl(string url, out string normalizedUrl, out string? validationError)
    {
        if (!ServerUrlRules.TryNormalize(url, ServerUrlRules.AllowDevCleartext, out normalizedUrl, out validationError))
            return false;

        ServerUrl = normalizedUrl;
        IsConfigured = true;
        Preferences.Default.Set(ServerUrlKey, normalizedUrl);
        return true;
    }
}
