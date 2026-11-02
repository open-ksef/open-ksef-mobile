namespace OpenKSeF.Mobile.Models;

public class IssuedInvoiceDto
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "Draft";
    public string? Number { get; set; }
    public SellerPartyDto Seller { get; set; } = new();
    public PartyDto Buyer { get; set; } = new();
    public DateOnly IssueDate { get; set; }
    public DateOnly SaleDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string PaymentMethod { get; set; } = "Transfer";
    public bool SplitPayment { get; set; }
    public ExemptionBasisDto? ExemptionBasis { get; set; }
    public List<IssuedInvoiceLineDto> Lines { get; set; } = [];
    public List<VatSummaryDto> VatSummary { get; set; } = [];
    public decimal TotalNet { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalGross { get; set; }
    public string Currency { get; set; } = "PLN";
    public KsefSubmissionDto Ksef { get; set; } = new();
    public List<ValidationIssue> Warnings { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? IssuedAt { get; set; }
}
