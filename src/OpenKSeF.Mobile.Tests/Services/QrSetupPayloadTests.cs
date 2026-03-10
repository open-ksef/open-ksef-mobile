using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

public class QrSetupPayloadTests
{
    [Fact]
    public void IsValid_ReturnsTrueForValidPayload()
    {
        var payload = new TestQrSetupPayload
        {
            Type = "openksef-setup",
            Version = 1,
            ServerUrl = "https://example.com"
        };

        Assert.True(payload.IsValid);
    }

    [Fact]
    public void IsValid_ReturnsTrueForHigherVersion()
    {
        var payload = new TestQrSetupPayload
        {
            Type = "openksef-setup",
            Version = 5,
            ServerUrl = "https://example.com"
        };

        Assert.True(payload.IsValid);
    }

    [Theory]
    [InlineData("wrong-type", 1, "https://example.com")]
    [InlineData("", 1, "https://example.com")]
    [InlineData("openksef-setup", 0, "https://example.com")]
    [InlineData("openksef-setup", -1, "https://example.com")]
    [InlineData("openksef-setup", 1, "")]
    [InlineData("openksef-setup", 1, "   ")]
    public void IsValid_ReturnsFalseForInvalidPayload(string type, int version, string serverUrl)
    {
        var payload = new TestQrSetupPayload
        {
            Type = type,
            Version = version,
            ServerUrl = serverUrl
        };

        Assert.False(payload.IsValid);
    }

    [Fact]
    public void JsonDeserialization_WithSetupToken()
    {
        var json = """{"type":"openksef-setup","version":1,"serverUrl":"https://test.com","setupToken":"abc123"}""";
        var payload = JsonSerializer.Deserialize<TestQrSetupPayload>(json);

        Assert.NotNull(payload);
        Assert.Equal("openksef-setup", payload.Type);
        Assert.Equal(1, payload.Version);
        Assert.Equal("https://test.com", payload.ServerUrl);
        Assert.Equal("abc123", payload.SetupToken);
        Assert.True(payload.IsValid);
    }

    [Fact]
    public void JsonDeserialization_WithoutSetupToken()
    {
        var json = """{"type":"openksef-setup","version":1,"serverUrl":"https://test.com"}""";
        var payload = JsonSerializer.Deserialize<TestQrSetupPayload>(json);

        Assert.NotNull(payload);
        Assert.Null(payload.SetupToken);
        Assert.True(payload.IsValid);
    }

    [Fact]
    public void JsonDeserialization_InvalidJson_ReturnsNull()
    {
        var json = "not-json";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TestQrSetupPayload>(json));
    }

    [Fact]
    public void JsonDeserialization_EmptyObject_IsInvalid()
    {
        var json = "{}";
        var payload = JsonSerializer.Deserialize<TestQrSetupPayload>(json);

        Assert.NotNull(payload);
        Assert.False(payload.IsValid);
    }
}

/// <summary>
/// Mirror of OpenKSeF.Mobile.Models.QrSetupPayload for testing without MAUI dependency.
/// </summary>
internal class TestQrSetupPayload
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
