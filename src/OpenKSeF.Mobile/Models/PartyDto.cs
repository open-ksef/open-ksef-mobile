namespace OpenKSeF.Mobile.Models;

public class PartyDto
{
    public string Kind { get; set; } = "Company";
    public string? Nip { get; set; }
    public string? Name { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
}
