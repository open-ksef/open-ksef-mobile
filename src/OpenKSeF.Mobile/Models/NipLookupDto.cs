namespace OpenKSeF.Mobile.Models;

public class NipLookupDto
{
    public string Nip { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? VatStatus { get; set; }
    public List<string> BankAccounts { get; set; } = [];
}
