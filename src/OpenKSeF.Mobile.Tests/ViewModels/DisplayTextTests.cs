using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

public class DisplayTextTests
{
    private const char Nbsp = ' ';

    private static void UnderCulture(string culture, Action assert)
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try { assert(); }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pl-PL")]
    [InlineData("th-TH")]
    public void InvoiceDto_PolishOnAnyDevice(string culture) => UnderCulture(culture, () =>
    {
        var invoice = new InvoiceDto
        {
            AmountGross = 1230.50m,
            Currency = "EUR",
            IssueDate = new DateTime(2026, 11, 3, 23, 30, 0),
            AcquisitionDate = new DateTime(2026, 1, 9)
        };

        Assert.Equal($"1{Nbsp}230,50 EUR", invoice.GrossText);
        Assert.Equal($"1{Nbsp}230,50", invoice.GrossAmountText);
        Assert.Equal("03.11.2026", invoice.IssueDateText);
        Assert.Equal("09.01.2026", invoice.AcquisitionDateText);
    });

    [Fact]
    public void InvoiceDto_NoAcquisitionDate_ShowsDash() =>
        Assert.Equal("—", new InvoiceDto().AcquisitionDateText);

    [Theory]
    [InlineData("en-US")]
    [InlineData("pl-PL")]
    public void FourDigitAmount_GroupsLikeThePortal(string culture) => UnderCulture(culture, () =>
    {
        Assert.Equal($"1{Nbsp}476,00 PLN", IssuingFormat.Money(1476m));
        Assert.Equal($"1{Nbsp}476,00", IssuingFormat.Amount(1476m));
        Assert.Equal($"1{Nbsp}476", IssuingFormat.Quantity(1476m));
    });

    [Theory]
    [InlineData("en-US")]
    [InlineData("pl-PL")]
    public void InvoiceLineDto_PolishOnAnyDevice(string culture) => UnderCulture(culture, () =>
    {
        var line = new InvoiceLineDto { Quantity = 2.5m, UnitPriceNet = 1000m, AmountNet = 2500m, AmountGross = 3075.5m };

        Assert.Equal("2,5", line.QuantityText);
        Assert.Equal($"1{Nbsp}000,00", line.UnitPriceNetText);
        Assert.Equal($"2{Nbsp}500,00", line.AmountNetText);
        Assert.Equal($"3{Nbsp}075,50", line.AmountGrossText);
    });

    [Fact]
    public void InvoiceLineDto_MissingValues_ShowDash()
    {
        var line = new InvoiceLineDto();

        Assert.Equal("—", line.QuantityText);
        Assert.Equal("—", line.UnitPriceNetText);
        Assert.Equal("—", line.AmountNetText);
        Assert.Equal("—", line.AmountGrossText);
        Assert.Equal("—", line.VatRateText);
    }

    [Fact]
    public void InvoiceLineDto_VatRate_ShownAsSent() =>
        Assert.Equal("23", new InvoiceLineDto { VatRate = "23" }.VatRateText);

    [Theory]
    [InlineData("en-US")]
    [InlineData("pl-PL")]
    public void ReceivedUnitPrice_KeepsPortalPrecision(string culture) => UnderCulture(culture, () =>
    {
        Assert.Equal("0,1234", new InvoiceLineDto { UnitPriceNet = 0.1234m }.UnitPriceNetText);
        Assert.Equal($"1{Nbsp}000,50", new InvoiceLineDto { UnitPriceNet = 1000.5m }.UnitPriceNetText);
        Assert.Equal("10,00", new InvoiceLineDto { UnitPriceNet = 10m }.UnitPriceNetText);
    });

    [Theory]
    [InlineData("en-US")]
    [InlineData("pl-PL")]
    [InlineData("th-TH")]
    public void DateAndTime_IgnoreDeviceCalendar(string culture) => UnderCulture(culture, () =>
    {
        var at = new DateTime(2026, 11, 3, 14, 5, 0);

        Assert.Equal("03.11.2026, 14:05", IssuingFormat.DateAndTime(at));
        Assert.Equal("14:05", IssuingFormat.Time(at));
        Assert.Equal("03.11.2026", IssuingFormat.Date(DateOnly.FromDateTime(at)));
    });

    [Fact]
    public void EditableNumber_StaysUngrouped_ForTheInvoiceForm() =>
        Assert.Equal("1476,5", IssuingFormat.EditableNumber(1476.5m));

    [Theory]
    [InlineData(typeof(InvoiceDto), new[] { "GrossText", "GrossAmountText", "IssueDateText", "AcquisitionDateText", "DisplayNumber" })]
    [InlineData(typeof(InvoiceLineDto), new[] { "QuantityText", "UnitPriceNetText", "AmountNetText", "AmountGrossText", "VatRateText" })]
    public void DisplayProperties_NeverReachTheWire(Type dto, string[] display)
    {
        var json = JsonSerializer.Serialize(Activator.CreateInstance(dto), dto);
        var names = JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();

        Assert.All(display, name => Assert.DoesNotContain(name, names));
        Assert.Equal(dto.GetProperties().Where(p => p.CanWrite).Select(p => p.Name).ToHashSet(), names);
    }

    [Fact]
    public void ServerResponse_DeserializesUnchanged_AndCannotSetDisplayText()
    {
        const string body = """
            {"id":"6f9619ff-8b86-d011-b42d-00c04fd430c8","amountGross":1476.00,"currency":"PLN","issueDate":"2026-11-03T00:00:00Z",
             "grossText":"from server","lines":[{"lineNumber":1,"amountNet":1200.00,"amountNetText":"from server"}]}
            """;

        var invoice = JsonSerializer.Deserialize<InvoiceDto>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(1476.00m, invoice.AmountGross);
        Assert.Equal($"1{Nbsp}476,00 PLN", invoice.GrossText);
        Assert.Equal($"1{Nbsp}200,00", invoice.Lines[0].AmountNetText);
    }

    [Fact]
    public void Screens_HaveNoCultureDependentFormat()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../OpenKSeF.Mobile"));
        var files = Directory.GetFiles(Path.Combine(root, "Views"), "*.xaml")
            .Concat(Directory.GetFiles(Path.Combine(root, "ViewModels"), "*.cs"));
        var cultureDependent = new Regex(@":N\d|:dd\b|:HH\b|:yyyy\b|ToString\(""[^""]*(N\d|yyyy|MM|dd|HH)[^""]*""\)");

        var hits = files.Where(f => cultureDependent.IsMatch(File.ReadAllText(f))).Select(Path.GetFileName);

        Assert.Empty(hits);
    }
}
