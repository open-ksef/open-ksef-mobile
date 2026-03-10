using System.Text.Json.Serialization;

namespace OpenKSeF.Mobile.Models;

public class QrSetupPayload
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("serverUrl")]
    public string ServerUrl { get; set; } = "";

    [JsonPropertyName("setupToken")]
    public string? SetupToken { get; set; }

    public bool IsValid =>
        Type == "openksef-setup" && Version >= 1 && !string.IsNullOrWhiteSpace(ServerUrl);
}
