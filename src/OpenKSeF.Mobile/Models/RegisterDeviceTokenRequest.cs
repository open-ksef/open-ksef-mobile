namespace OpenKSeF.Mobile.Models;

public class RegisterDeviceTokenRequest
{
    public string Token { get; set; } = string.Empty;
    public int Platform { get; set; }
    public Guid? TenantId { get; set; }
}
