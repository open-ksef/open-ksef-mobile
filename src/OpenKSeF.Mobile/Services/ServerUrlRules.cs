namespace OpenKSeF.Mobile.Services;

/// <summary>Pure server URL rules: validation, server-switch detection and the host shown to the user.</summary>
public static class ServerUrlRules
{
#if DEBUG
    public const bool AllowDevCleartext = true;
#else
    public const bool AllowDevCleartext = false;
#endif

    private static readonly HashSet<string> CleartextDevHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "10.0.2.2",
    };

    public static bool TryNormalize(string url, bool allowDevCleartext, out string normalizedUrl, out string? validationError)
    {
        normalizedUrl = (url ?? string.Empty).Trim().TrimEnd('/');
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

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            validationError = "Adres serwera nie moze zawierac nazwy uzytkownika ani hasla.";
            return false;
        }

        if (uri.Scheme == "http" && !(allowDevCleartext && CleartextDevHosts.Contains(uri.Host)))
        {
            validationError = allowDevCleartext
                ? "Polaczenie HTTP dozwolone tylko dla localhost / 10.0.2.2. Uzyj HTTPS."
                : "Wymagane jest polaczenie HTTPS.";
            return false;
        }

        normalizedUrl = $"{uri.Scheme}://{DisplayHost(uri)}";
        return true;
    }

    public static bool IsDifferentServer(string currentUrl, string newNormalizedUrl) =>
        !string.Equals(currentUrl.TrimEnd('/'), newNormalizedUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    public static string DisplayHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? DisplayHost(uri) : url;

    private static string DisplayHost(Uri uri)
    {
        var host = uri.HostNameType == UriHostNameType.IPv6 ? uri.Host : uri.IdnHost.TrimEnd('.');
        return uri.IsDefaultPort ? host : $"{host}:{uri.Port}";
    }
}
