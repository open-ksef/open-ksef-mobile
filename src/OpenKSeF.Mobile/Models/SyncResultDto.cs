using System.Text.Json.Serialization;

namespace OpenKSeF.Mobile.Models;

public class SyncResultDto
{
    [JsonPropertyName("fetchedInvoices")]
    public int FetchedInvoices { get; set; }

    [JsonPropertyName("newInvoices")]
    public int NewInvoices { get; set; }
}
