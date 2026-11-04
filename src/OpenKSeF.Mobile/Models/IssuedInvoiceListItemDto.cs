using System.Text.Json.Serialization;

namespace OpenKSeF.Mobile.Models;

public class IssuedInvoiceListItemDto
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Number { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerNip { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal TotalNet { get; set; }
    public decimal TotalGross { get; set; }
    public string Currency { get; set; } = "PLN";
    public string? KsefNumber { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BuyerCountryCode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BuyerIdType { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BuyerForeignId { get; set; }
}
