using System.Text.Json.Serialization;

namespace OpenKSeF.Mobile.Models;

public class TransferDetailsDto
{
    private string? _qrMessage;

    public string RecipientName { get; set; } = string.Empty;
    public string? RecipientAccount { get; set; }
    public string RecipientNip { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string TransferText { get; set; } = string.Empty;
    public string? QrCodeBase64 { get; set; }

    // System.Text.Json calls this setter for an explicit null and not for an absent key.
    // v0.0.7 has no key and draws a QR for any invoice, also EUR, so its QR must not be shown.
    public string? QrMessage
    {
        get => _qrMessage;
        set
        {
            _qrMessage = value;
            ServerHasQrRules = true;
        }
    }

    [JsonIgnore]
    public bool ServerHasQrRules { get; private set; }
}
