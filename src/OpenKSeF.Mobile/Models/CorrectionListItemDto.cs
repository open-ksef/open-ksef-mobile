namespace OpenKSeF.Mobile.Models;

public class CorrectionListItemDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public string InvoiceType { get; set; } = "VAT";
    public string Status { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public decimal TotalGross { get; set; }
}
