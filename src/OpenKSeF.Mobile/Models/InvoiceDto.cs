using System.Text.Json.Serialization;
using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Models;

public class InvoiceDto
{
    public Guid Id { get; set; }
    public string KSeFInvoiceNumber { get; set; } = string.Empty;
    public string KSeFReferenceNumber { get; set; } = string.Empty;
    public string? InvoiceNumber { get; set; }
    [JsonIgnore]
    public string DisplayNumber => InvoiceNumber ?? KSeFInvoiceNumber;
    public string VendorName { get; set; } = string.Empty;
    public string VendorNip { get; set; } = string.Empty;
    public string? VendorBankAccount { get; set; }
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
    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateOnly? DueDate { get; set; }
    public bool IsOverdue { get; set; }
    public List<InvoiceLineDto> Lines { get; set; } = new();
    [JsonIgnore]
    public string GrossText => IssuingFormat.Money(AmountGross, Currency);
    [JsonIgnore]
    public string GrossAmountText => IssuingFormat.Amount(AmountGross);
    [JsonIgnore]
    public string IssueDateText => IssuingFormat.Date(IssueDate);
    [JsonIgnore]
    public string AcquisitionDateText => IssuingFormat.Date(AcquisitionDate);
    [JsonIgnore]
    public string DueDateText => DueDate is { } dueDate ? IssuingFormat.Date(dueDate) : "—";
    [JsonIgnore]
    public string DueText => IsOverdue ? $"{DueDateText} po terminie" : DueDateText;

    public static bool Overdue(bool isPaid, DateOnly? dueDate, DateTimeOffset utcNow) =>
        !isPaid && dueDate < IssuingFormat.PolandDate(utcNow);
}
