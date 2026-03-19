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

    private static readonly HashSet<string> CleartextAllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "10.0.2.2",
    };

    private static bool IsDevHost(string host) => CleartextAllowedHosts.Contains(host);

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

        if (uri.Scheme == "http" && !IsDevHost(uri.Host))
        {
            validationError = "Polaczenie HTTP dozwolone tylko dla localhost / 10.0.2.2. Uzyj HTTPS.";
            return false;
        }

        normalizedUrl = uri.GetLeftPart(UriPartial.Authority);
        ServerUrl = normalizedUrl;
        IsConfigured = true;
        Preferences.Default.Set(ServerUrlKey, normalizedUrl);
        return true;
    }
}
