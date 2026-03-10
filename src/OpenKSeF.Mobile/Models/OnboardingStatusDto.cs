using System.Text.Json.Serialization;

namespace OpenKSeF.Mobile.Models;

public class OnboardingStatusDto
{
    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; set; }

    [JsonPropertyName("hasTenant")]
    public bool HasTenant { get; set; }

    [JsonPropertyName("hasCredential")]
    public bool HasCredential { get; set; }

    [JsonPropertyName("firstTenantId")]
    public string? FirstTenantId { get; set; }
}
