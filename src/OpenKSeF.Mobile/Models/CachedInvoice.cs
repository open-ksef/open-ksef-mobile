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
    public DateTime CachedAt { get; set; }

    public InvoiceDto ToDto()
    {
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
            IssueDate = IssueDate,
            AcquisitionDate = AcquisitionDate,
            InvoiceType = InvoiceType,
            FirstSeenAt = FirstSeenAt
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
            CachedAt = DateTime.UtcNow
        };
    }
}
