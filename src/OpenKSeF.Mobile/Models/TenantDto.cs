namespace OpenKSeF.Mobile.Models;

public class TenantDto
{
    public Guid Id { get; set; }
    public string Nip { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? NotificationEmail { get; set; }
    public DateTime CreatedAt { get; set; }
}
