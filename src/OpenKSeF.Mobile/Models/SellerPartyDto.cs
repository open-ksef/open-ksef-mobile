using System.Text.Json.Serialization;

namespace OpenKSeF.Mobile.Models;

public class SellerPartyDto
{
    public string Nip { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? BankAccount { get; set; }
    public bool CashAccounting { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Iban { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Swift { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BankName { get; set; }
}
