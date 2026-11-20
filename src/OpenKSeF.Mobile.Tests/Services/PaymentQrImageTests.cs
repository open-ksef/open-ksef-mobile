using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

public class PaymentQrImageTests
{
    private static float Mono(string s) => s.Length * 10f;

    [Theory]
    [InlineData("FV 1/2026", "QR FV 1_2026.png")]
    [InlineData(@"A\B:C*D?E""F<G>H|I", "QR A_B_C_D_E_F_G_H_I.png")]
    [InlineData("FV\t7\n2026", "QR FV_7_2026.png")]
    [InlineData("Faktura ŻÓŁĆ-ęśąń 3", "QR Faktura ŻÓŁĆ-ęśąń 3.png")]
    [InlineData("  ", "QR.png")]
    [InlineData(null, "QR.png")]
    public void FileName_ReplacesForbiddenCharsAndKeepsPolish(string? number, string expected) =>
        Assert.Equal(expected, PaymentQrImage.FileName(number));

    [Theory]
    [InlineData("FV‮1/2026gnp.exe", "QR FV_1_2026gnp.exe.png")]
    [InlineData("FV​1﻿", "QR FV_1_.png")]
    public void FileName_ReplacesFormatChars(string number, string expected) =>
        Assert.Equal(expected, PaymentQrImage.FileName(number));

    [Fact]
    public void FileName_LongPolishNumber_CutTo100Chars()
    {
        var number = string.Concat(Enumerable.Repeat("ŻÓŁĆ", 64));

        var name = PaymentQrImage.FileName(number);

        Assert.Equal($"QR {number[..100]}.png", name);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(name) < 255);
    }

    [Fact]
    public void FileName_TwoInvoices_DifferAndContainTheirNumbers()
    {
        var a = PaymentQrImage.FileName("FV 1/2026");
        var b = PaymentQrImage.FileName("FV 2/2026");

        Assert.NotEqual(a, b);
        Assert.Contains("FV 1_2026", a);
        Assert.Contains("FV 2_2026", b);
    }

    [Fact]
    public void Caption_IsNumberMoneyPayee()
    {
        var transfer = new TransferDetailsDto { Title = "FV 1/2026", Amount = 1476.00m, Currency = "PLN", RecipientName = "Hurtownia Żółć Sp. z o.o." };

        Assert.Equal(["FV 1/2026", "1 476,00 PLN", "Hurtownia Żółć Sp. z o.o."], PaymentQrImage.Caption(transfer));
    }

    [Fact]
    public void Caption_SkipsBlankPayee()
    {
        var transfer = new TransferDetailsDto { Title = "FV 1/2026", Amount = 10m, Currency = "EUR", RecipientName = " " };

        Assert.Equal(["FV 1/2026", "10,00 EUR"], PaymentQrImage.Caption(transfer));
    }

    [Fact]
    public void Fit_ShortLine_Unchanged() =>
        Assert.Equal("Żółć", PaymentQrImage.Fit("Żółć", 40, Mono));

    [Fact]
    public void Fit_LongLine_EndsWithEllipsisWithinWidth()
    {
        var fitted = PaymentQrImage.Fit("Przedsiębiorstwo Usługowo-Handlowe Źdźbło Ślęża", 200, Mono);

        Assert.Equal("Przedsiębiorstwo Us…", fitted);
        Assert.True(Mono(fitted) <= 200);
    }

    [Fact]
    public void Fit_DropsTrailingSpaceBeforeEllipsis() =>
        Assert.Equal("Ab…", PaymentQrImage.Fit("Ab cdef", 40, Mono));

    [Fact]
    public void Fit_NeverSplitsASurrogatePair()
    {
        var fitted = PaymentQrImage.Fit("ab\U0001F600cd", 40, Mono);

        Assert.Equal("ab…", fitted);
    }

    [Fact]
    public void Fit_NothingFits_OnlyEllipsis() =>
        Assert.Equal("…", PaymentQrImage.Fit("Żółć", 5, Mono));

    [Theory]
    [InlineData(820, 3)]
    [InlineData(820, 1)]
    [InlineData(100, 3)]
    public void Layout_TextBandSitsBelowQrAndInsideMargins(int qr, int lines)
    {
        var layout = PaymentQrImage.Measure(qr, qr, lines);

        for (var i = 0; i < lines; i++)
        {
            var (x, y, width, height) = layout.Line(i);
            Assert.True(y >= qr, $"line {i} overlaps the QR");
            Assert.True(x > 0 && x + width < qr, $"line {i} leaves the image");
            Assert.True(y + height <= layout.Height, $"line {i} below the image");
        }
        Assert.Equal(qr, layout.QrHeight);
    }
}
