namespace OpenKSeF.Mobile.Models;

public class KsefSubmissionDto
{
    public string Environment { get; set; } = string.Empty;
    public string? KsefNumber { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public int? StatusCode { get; set; }
    public string? StatusMessage { get; set; }
    public string? LastError { get; set; }
    public string? VerificationUrl { get; set; }
    public bool HasUpo { get; set; }
}
