namespace OpenKSeF.Mobile.Models;

public class CreateTenantRequest
{
    public string Nip { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? NotificationEmail { get; set; }
}
