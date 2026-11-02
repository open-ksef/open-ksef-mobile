namespace OpenKSeF.Mobile.Models;

public class ValidationIssue
{
    public string Code { get; set; } = string.Empty;
    public string? Field { get; set; }
    public string Message { get; set; } = string.Empty;
}
