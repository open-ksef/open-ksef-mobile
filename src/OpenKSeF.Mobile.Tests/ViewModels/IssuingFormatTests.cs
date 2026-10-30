using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

public class IssuingFormatTests
{
    [Theory]
    [InlineData(15162, "15 162,00 PLN")]
    [InlineData(0.5, "0,50 PLN")]
    [InlineData(1234567.89, "1 234 567,89 PLN")]
    public void Money_AlwaysPolish(decimal amount, string expected) =>
        Assert.Equal(expected, IssuingFormat.Money(amount).Replace(' ', ' '));

    [Theory]
    [InlineData("12000", 12000)]
    [InlineData("12 000,00", 12000)]
    [InlineData("12000.00", 12000)]
    [InlineData("0,333333", 0.333333)]
    public void TryParseDecimal_AcceptsPolishAndDotInput(string text, decimal expected)
    {
        Assert.True(IssuingFormat.TryParseDecimal(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void TryParseDecimal_RejectsGarbage(string text) => Assert.False(IssuingFormat.TryParseDecimal(text, out _));

    [Fact]
    public void Nrb_GroupsTwentySixDigits() =>
        Assert.Equal("61 1090 1014 0000 0712 1981 2874", IssuingFormat.Nrb("61109010140000071219812874"));

    [Theory]
    [InlineData("23", "23%")]
    [InlineData("0 KR", "0% (kraj)")]
    [InlineData("zw", "zw")]
    public void VatRateLabel_RoundTripsCode(string code, string label)
    {
        Assert.Equal(label, IssuingFormat.VatRateLabel(code));
        Assert.Equal(code, IssuingFormat.VatRateCode(label));
    }

    [Theory]
    [InlineData("Draft", false, "Szkic", "Neutral")]
    [InlineData("Sending", false, "Wysyłanie", "Info")]
    [InlineData("Accepted", false, "Przyjęta w KSeF", "Success")]
    [InlineData("Accepted", true, "Przyjęta", "Success")]
    [InlineData("Rejected", false, "Odrzucona", "Error")]
    public void Status_LabelAndTone(string status, bool shortForm, string label, string tone)
    {
        Assert.Equal(label, IssuingFormat.StatusLabel(status, shortForm));
        Assert.Equal(tone, IssuingFormat.StatusTone(status));
    }
}
