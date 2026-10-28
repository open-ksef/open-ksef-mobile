namespace OpenKSeF.Mobile.Services;

public class ServerSettingsService : IServerSettingsService
{
    private const string ServerUrlKey = "server_url";
    private const string DefaultServerUrl = "https://demo.open-ksef.pl";
    private const string RealmPath = "/auth/realms/openksef";

    public ServerSettingsService()
    {
        IsConfigured = Preferences.Default.ContainsKey(ServerUrlKey);
        var stored = Preferences.Default.Get(ServerUrlKey, DefaultServerUrl);
        ServerUrl = stored;
    }

    public string ServerUrl { get; private set; }

    public string Authority => $"{ServerUrl}{RealmPath}";

    public bool IsConfigured { get; private set; }

    public void MarkAsConfigured()
    {
        IsConfigured = true;
    }

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
