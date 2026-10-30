namespace OpenKSeF.Mobile.Models;

public class SellerProfileRequest
{
    public string? LegalName { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? BankAccount { get; set; }
    public bool CashAccounting { get; set; }
    public string? NumberingPattern { get; set; }
    public int? NextNumber { get; set; }
    public string? NextNumberPeriod { get; set; }
}
