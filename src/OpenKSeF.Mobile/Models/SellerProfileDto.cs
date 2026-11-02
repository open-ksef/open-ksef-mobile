namespace OpenKSeF.Mobile.Models;

public class SellerProfileDto : SellerProfileRequest
{
    public string Nip { get; set; } = string.Empty;
    public bool IsComplete { get; set; }
    public string? NextNumberPreview { get; set; }
    public string KsefEnvironment { get; set; } = string.Empty;
}
