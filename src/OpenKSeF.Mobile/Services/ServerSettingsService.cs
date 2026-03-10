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
        normalizedUrl = url.TrimEnd('/');
        validationError = null;

        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            validationError = "Adres serwera nie moze byc pusty.";
            return false;
        }

        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            validationError = "Podaj poprawny adres URL (http:// lub https://).";
            return false;
        }

        normalizedUrl = uri.GetLeftPart(UriPartial.Authority);
        ServerUrl = normalizedUrl;
        IsConfigured = true;
        Preferences.Default.Set(ServerUrlKey, normalizedUrl);
        return true;
    }
}
