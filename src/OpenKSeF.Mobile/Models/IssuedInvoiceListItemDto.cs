namespace OpenKSeF.Mobile.Models;

public class IssuedInvoiceListItemDto
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Number { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerNip { get; set; }
    public DateOnly IssueDate { get; set; }
    public decimal TotalGross { get; set; }
    public string Currency { get; set; } = "PLN";
    public string? KsefNumber { get; set; }
}
