using System.Globalization;
using OpenKSeF.Mobile.Services;
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

    [Theory]
    [InlineData("5,", 5)]
    [InlineData("5.", 5)]
    [InlineData(".5", 0.5)]
    [InlineData("00,5", 0.5)]
    [InlineData(" 2,5 ", 2.5)]
    [InlineData("1 000,50", 1000.5)]
    [InlineData("1 000,50", 1000.5)]
    public void TryParseDecimal_AcceptsDeviceInputForms(string text, decimal expected)
    {
        Assert.True(IssuingFormat.TryParseDecimal(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("٢,٥")]
    [InlineData("١٢٫٥")]
    [InlineData("२")]
    [InlineData("５")]
    [InlineData("−2,5")]
    [InlineData("1,5,5")]
    [InlineData("1e5")]
    public void TryParseDecimal_RefusesNonAsciiDigitsAndOddForms(string text) =>
        Assert.False(IssuingFormat.TryParseDecimal(text, out _));

    [Theory]
    [InlineData("2,5", 2.5)]
    [InlineData("2.5", 2.5)]
    [InlineData("100,50", 100.5)]
    [InlineData("100.50", 100.5)]
    [InlineData("1 000", 1000)]
    [InlineData("1 000,50", 1000.5)]
    [InlineData("1 000.50", 1000.5)]
    [InlineData("0,125", 0.125)]
    [InlineData("0.125", 0.125)]
    [InlineData("1234,567", 1234.567)]
    [InlineData("1,0000", 1)]
    [InlineData("1,1250", 1.125)]
    [InlineData(",5", 0.5)]
    [InlineData("-2,5", -2.5)]
    public void TryParseDecimal_BothSeparatorsSameValue(string text, decimal expected)
    {
        Assert.True(IssuingFormat.TryParseDecimal(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("99999999999999999")]
    [InlineData("1 000 000 000 000")]
    [InlineData("-1000000000000")]
    [InlineData("0,99999999999999999999999999999999")]
    [InlineData("1,00000000000000000000000000000001")]
    [InlineData("1,1234567")]
    public void TryParseDecimal_TooManyDigits_IsRefusedWithError(string text)
    {
        Assert.False(IssuingFormat.TryParseDecimal(text, out _));
        Assert.Contains("Za dużo cyfr", IssuingFormat.DecimalInputError(text));
    }

    [Theory]
    [InlineData("999999999999", "999999999999")]
    [InlineData("999 999 999 999,999999", "999999999999.999999")]
    [InlineData("-999999999999.999999", "-999999999999.999999")]
    [InlineData("0,000001", "0.000001")]
    [InlineData("1,123456", "1.123456")]
    public void TryParseDecimal_AtTheLimit_KeepsEveryDigit(string text, string exact)
    {
        Assert.True(IssuingFormat.TryParseDecimal(text, out var value));
        Assert.Equal(exact, value.ToString(CultureInfo.InvariantCulture));
        Assert.Null(IssuingFormat.DecimalInputError(text));
    }

    [Fact]
    public void Totals_LargestAcceptedLinesTimes1000_DoNotThrow()
    {
        var line = new DraftLineViewModel { Quantity = "999999999999,999999", UnitNetPrice = "999999999999,999999" };

        var totals = IssuedInvoiceTotals.Compute(Enumerable.Repeat(line.ToRequest(), 1000));

        Assert.True(totals.TotalGross > 1e27m);
        Assert.NotEqual("0,00 PLN", line.Gross);
    }

    [Fact]
    public void Line_OverflowingInput_DoesNotThrowFromSetter()
    {
        var line = new DraftLineViewModel { Quantity = "99999999999999999" };

        line.UnitNetPrice = "99999999999999";

        Assert.Equal("0,00 PLN", line.Gross);
        Assert.Contains("Za dużo cyfr", line.Error);
    }

    [Theory]
    [InlineData("1.000")]
    [InlineData("1,000")]
    [InlineData("12.345")]
    [InlineData("123,456")]
    [InlineData("-1,000")]
    public void TryParseDecimal_ThousandsLookingIsRefusedWithHint(string text)
    {
        Assert.False(IssuingFormat.TryParseDecimal(text, out _));
        Assert.Contains("Nie wiadomo", IssuingFormat.AmbiguousDecimalError(text));
    }

    [Theory]
    [InlineData("1.000.000")]
    [InlineData("1,000,000")]
    [InlineData("1.000,50")]
    [InlineData("1,000.50")]
    [InlineData("1 2")]
    [InlineData("12 34,5")]
    [InlineData(",")]
    [InlineData("-")]
    public void TryParseDecimal_RefusesMalformedGrouping(string text) => Assert.False(IssuingFormat.TryParseDecimal(text, out _));

    [Theory]
    [InlineData(1.125, "1,1250")]
    [InlineData(12.345, "12,3450")]
    [InlineData(100, "100")]
    [InlineData(2.5, "2,5")]
    [InlineData(1234.567, "1234,567")]
    public void EditableNumber_RoundTripsThroughTheParser(decimal value, string expected)
    {
        var text = IssuingFormat.EditableNumber(value);

        Assert.Equal(expected, text);
        Assert.True(IssuingFormat.TryParseDecimal(text, out var parsed));
        Assert.Equal(value, parsed);
    }

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
