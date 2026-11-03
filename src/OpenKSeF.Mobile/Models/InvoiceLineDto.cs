using System.Text.Json.Serialization;
using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Models;

public class InvoiceLineDto
{
    public int LineNumber { get; set; }
    public string? Name { get; set; }
    public string? Unit { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPriceNet { get; set; }
    public decimal? UnitPriceGross { get; set; }
    public decimal? AmountNet { get; set; }
    public decimal? AmountGross { get; set; }
    public decimal? AmountVat { get; set; }
    public string? VatRate { get; set; }
    [JsonIgnore]
    public string QuantityText => IssuingFormat.Quantity(Quantity);
    [JsonIgnore]
    public string UnitPriceNetText => IssuingFormat.Amount(UnitPriceNet);
    [JsonIgnore]
    public string AmountNetText => IssuingFormat.Amount(AmountNet);
    [JsonIgnore]
    public string AmountGrossText => IssuingFormat.Amount(AmountGross);
}
