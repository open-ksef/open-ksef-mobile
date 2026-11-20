using System.Text.Json.Serialization;

namespace OpenKSeF.Mobile.Models;

public class IssuedInvoiceLineDto : IssuedInvoiceLineRequest
{
    public int LineNo { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BaseLineNo { get; set; }
}
