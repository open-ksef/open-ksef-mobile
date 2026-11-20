namespace OpenKSeF.Mobile.Models;

public class CorrectionRowDto
{
    public int RowNo { get; set; }
    public bool IsBefore { get; set; }
    public int? BaseLineNo { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitNetPrice { get; set; }
    public string VatRate { get; set; } = string.Empty;
    public decimal NetAmount { get; set; }
}
