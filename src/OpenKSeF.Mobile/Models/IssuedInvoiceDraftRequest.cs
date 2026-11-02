namespace OpenKSeF.Mobile.Models;

public class IssuedInvoiceDraftRequest
{
    public PartyDto Buyer { get; set; } = new();
    public DateOnly? IssueDate { get; set; }
    public DateOnly? SaleDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public string PaymentMethod { get; set; } = "Transfer";
    public bool SplitPayment { get; set; }
    public ExemptionBasisDto? ExemptionBasis { get; set; }
    public List<IssuedInvoiceLineRequest> Lines { get; set; } = [];
}
