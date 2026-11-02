namespace OpenKSeF.Mobile.Models;

public class ValidationEnvelope
{
    public List<ValidationIssue> Errors { get; set; } = [];
    public List<ValidationIssue> Warnings { get; set; } = [];
}
