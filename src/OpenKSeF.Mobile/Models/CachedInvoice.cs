using SQLite;

namespace OpenKSeF.Mobile.Models;

public class CachedInvoice
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
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
    public bool IsPaid { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CachedAt { get; set; }

    public InvoiceDto ToDto() => ToDto(DateTimeOffset.UtcNow);

    public InvoiceDto ToDto(DateTimeOffset utcNow)
    {
        var dueDate = DueDate is { } due ? DateOnly.FromDateTime(due) : (DateOnly?)null;
        return new InvoiceDto
        {
            Id = Guid.Parse(Id),
            KSeFInvoiceNumber = KSeFInvoiceNumber,
            KSeFReferenceNumber = KSeFReferenceNumber,
            InvoiceNumber = InvoiceNumber,
            VendorName = VendorName,
            VendorNip = VendorNip,
            BuyerName = BuyerName,
            BuyerNip = BuyerNip,
            AmountNet = AmountNet,
            AmountVat = AmountVat,
            AmountGross = AmountGross,
            Currency = Currency,
            IssueDate = DateTime.SpecifyKind(IssueDate, DateTimeKind.Utc),
            AcquisitionDate = AcquisitionDate is { } acquisitionDate ? DateTime.SpecifyKind(acquisitionDate, DateTimeKind.Utc) : null,
            InvoiceType = InvoiceType,
            FirstSeenAt = DateTime.SpecifyKind(FirstSeenAt, DateTimeKind.Utc),
            IsPaid = IsPaid,
            DueDate = dueDate,
            IsOverdue = InvoiceDto.Overdue(IsPaid, dueDate, utcNow)
        };
    }

    public static CachedInvoice FromDto(InvoiceDto dto, Guid tenantId)
    {
        return new CachedInvoice
        {
            Id = dto.Id.ToString(),
            TenantId = tenantId.ToString(),
            KSeFInvoiceNumber = dto.KSeFInvoiceNumber,
            KSeFReferenceNumber = dto.KSeFReferenceNumber,
            InvoiceNumber = dto.InvoiceNumber,
            VendorName = dto.VendorName,
            VendorNip = dto.VendorNip,
            BuyerName = dto.BuyerName,
            BuyerNip = dto.BuyerNip,
            AmountNet = dto.AmountNet,
            AmountVat = dto.AmountVat,
            AmountGross = dto.AmountGross,
            Currency = dto.Currency,
            IssueDate = dto.IssueDate,
            AcquisitionDate = dto.AcquisitionDate,
            InvoiceType = dto.InvoiceType,
            FirstSeenAt = dto.FirstSeenAt,
            IsPaid = dto.IsPaid,
            DueDate = dto.DueDate?.ToDateTime(TimeOnly.MinValue),
            CachedAt = DateTime.UtcNow
        };
    }
}
