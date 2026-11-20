namespace OpenKSeF.Mobile.Models;

public class OrderChainDto
{
    public decimal OrderValue { get; set; }
    public List<CorrectionListItemDto> Advances { get; set; } = [];
    public CorrectionListItemDto? Final { get; set; }
    public List<CorrectionListItemDto> Corrections { get; set; } = [];
    public decimal Remaining { get; set; }
    public Guid? OpenDocumentId { get; set; }
}
