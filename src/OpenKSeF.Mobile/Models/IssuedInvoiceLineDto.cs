namespace OpenKSeF.Mobile.Models;

public class IssuedInvoiceLineDto : IssuedInvoiceLineRequest
{
    public int LineNo { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
}
