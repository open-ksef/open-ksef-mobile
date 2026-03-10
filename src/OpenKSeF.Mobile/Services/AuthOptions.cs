namespace OpenKSeF.Mobile.Services;

public class AuthOptions
{
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "openksef-mobile";
    public string RedirectUri { get; set; } = "openksef://auth/callback";
    public string PostLogoutRedirectUri { get; set; } = "openksef://auth/logout";
}
