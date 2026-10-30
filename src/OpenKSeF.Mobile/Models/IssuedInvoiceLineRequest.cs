namespace OpenKSeF.Mobile.Models;

public class IssuedInvoiceLineRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitNetPrice { get; set; }
    public string VatRate { get; set; } = "23";
}
