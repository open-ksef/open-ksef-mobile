namespace OpenKSeF.Mobile.Models;

public class CurrentStateDto
{
    public List<IssuedInvoiceLineDto> Lines { get; set; } = [];
    public List<VatSummaryDto> VatSummary { get; set; } = [];
    public decimal TotalNet { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalGross { get; set; }
    public PartyDto Buyer { get; set; } = new();
    public bool IsZero { get; set; }
}
