using System.Text.Json.Serialization;

namespace OpenKSeF.Mobile.Models;

public class TenantDto
{
    public Guid Id { get; set; }
    public string Nip { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? NotificationEmail { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Role { get; set; }
    public List<string>? Permissions { get; set; }

    [JsonIgnore]
    public bool CanEdit => Can("tenant.edit");
    [JsonIgnore]
    public bool CanReadSellerProfile => Can("invoices.read");
    [JsonIgnore]
    public bool CanDelete => Can("tenant.delete");
    [JsonIgnore]
    public bool CanManageCredential => Can("credential.manage");

    [JsonIgnore]
    public string? RoleName => Role switch
    {
        "Owner" => "Właściciel",
        "Administrator" => "Administrator",
        "Accountant" => "Księgowy",
        "Employee" => "Pracownik",
        _ => null,
    };

    private bool Can(string permission) => Permissions is null || Permissions.Contains(permission);
}
