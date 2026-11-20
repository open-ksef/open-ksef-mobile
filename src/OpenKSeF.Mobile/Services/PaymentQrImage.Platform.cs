using Microsoft.Maui.Graphics.Platform;

namespace OpenKSeF.Mobile.Services;

public static partial class PaymentQrImage
{
    public static partial byte[] Compose(byte[] qrPng, IReadOnlyList<string> lines)
    {
        using var qr = PlatformImage.FromStream(new MemoryStream(qrPng));
        var layout = Measure((int)qr.Width, (int)qr.Height, lines.Count);

        using var context = new PlatformBitmapExportService().CreateContext(layout.Width, layout.Height, 1);
        var canvas = context.Canvas;
        canvas.FillColor = Colors.White;
        canvas.FillRectangle(0, 0, layout.Width, layout.Height);
        canvas.DrawImage(qr, 0, 0, layout.Width, layout.QrHeight);

        canvas.FontColor = Colors.Black;
        canvas.FontSize = layout.FontSize;
        for (var i = 0; i < lines.Count; i++)
        {
            var font = i == 0 ? Microsoft.Maui.Graphics.Font.DefaultBold : Microsoft.Maui.Graphics.Font.Default;
            var (x, y, width, height) = layout.Line(i);
            canvas.Font = font;
            var text = Fit(lines[i], width, s => canvas.GetStringSize(s, font, layout.FontSize).Width);
            canvas.DrawString(text, x, y, width, height, HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        using var output = new MemoryStream();
        context.WriteToStream(output);
        return output.ToArray();
    }
}
