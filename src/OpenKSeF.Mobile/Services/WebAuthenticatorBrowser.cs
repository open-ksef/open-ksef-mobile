using IdentityModel.OidcClient.Browser;

namespace OpenKSeF.Mobile.Services;

/// <summary>
/// OIDC browser implementation using MAUI's WebAuthenticator (system browser).
/// </summary>
public class WebAuthenticatorBrowser : IdentityModel.OidcClient.Browser.IBrowser
{
    public async Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var callbackUri = new Uri(options.EndUrl);

            var authResult = await WebAuthenticator.Default.AuthenticateAsync(
                new Uri(options.StartUrl),
                callbackUri);

            // Reconstruct the callback URL with all query parameters from the result
            var queryParams = string.Join("&",
                authResult.Properties.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

            var resultUrl = $"{options.EndUrl}?{queryParams}";

            return new BrowserResult
            {
                ResultType = BrowserResultType.Success,
                Response = resultUrl
            };
        }
        catch (TaskCanceledException)
        {
            return new BrowserResult
            {
                ResultType = BrowserResultType.UserCancel
            };
        }
        catch (Exception ex)
        {
            return new BrowserResult
            {
                ResultType = BrowserResultType.UnknownError,
                Error = ex.Message
            };
        }
    }
}
