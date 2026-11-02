namespace OpenKSeF.Mobile.Models;

public class VatSummaryDto
{
    public string VatRate { get; set; } = string.Empty;
    public decimal Net { get; set; }
    public decimal Vat { get; set; }
}
