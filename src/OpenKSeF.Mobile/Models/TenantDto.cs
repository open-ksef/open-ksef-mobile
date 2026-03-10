namespace OpenKSeF.Mobile.Models;

public class TenantDto
{
    public Guid Id { get; set; }
    public string Nip { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool HasKSeFToken { get; set; }
    public DateTime CreatedAt { get; set; }
}
