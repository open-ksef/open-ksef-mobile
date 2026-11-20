namespace OpenKSeF.Mobile.Models;

public class CorrectedInvoiceRefDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public string? KsefNumber { get; set; }
    public DateOnly IssueDate { get; set; }
    public string InvoiceType { get; set; } = "VAT";
}
