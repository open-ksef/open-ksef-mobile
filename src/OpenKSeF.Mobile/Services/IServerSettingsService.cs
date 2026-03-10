namespace OpenKSeF.Mobile.Services;

public interface IServerSettingsService
{
    string ServerUrl { get; }
    string Authority { get; }
    bool IsConfigured { get; }
    void MarkAsConfigured();
    bool TryUpdateServerUrl(string url, out string normalizedUrl, out string? validationError);
}
