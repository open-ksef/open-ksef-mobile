namespace OpenKSeF.Mobile.Models;

public class InvoiceDto
{
    public Guid Id { get; set; }
    public string KSeFInvoiceNumber { get; set; } = string.Empty;
    public string KSeFReferenceNumber { get; set; } = string.Empty;
    public string? InvoiceNumber { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public string VendorNip { get; set; } = string.Empty;
    public string? BuyerName { get; set; }
    public string? BuyerNip { get; set; }
    public decimal AmountNet { get; set; }
    public decimal AmountVat { get; set; }
    public decimal AmountGross { get; set; }
    public string Currency { get; set; } = "PLN";
    public DateTime IssueDate { get; set; }
    public DateTime? AcquisitionDate { get; set; }
    public string? InvoiceType { get; set; }
    public DateTime FirstSeenAt { get; set; }
}
