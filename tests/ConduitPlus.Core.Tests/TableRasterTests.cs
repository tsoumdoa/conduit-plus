using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

[Collection("Table text metrics")]
public sealed class TableRasterTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void FittedTableTextHasNoAdditionalGdiEllipsesAcrossFourHundredWidths(double scale)
    {
        var previous = TextMetrics.Measurer;
        GdiCanvas.RegisterTextMetrics();
        try
        {
            foreach (var six in new[] { false, true })
            {
                var source = new CardSpec { Id = "raster", Kind = CardKind.Table, Title = "Analysis", Decimals = 1,
                    Headers = six ? ["Room", "Level", "Area m²", "DA %", "UDI %", "Glare"] : ["Room", "Daylight autonomy %", "UDI %"],
                    Rows = six ? [new("r", ["Room", "1", "25.5", "48.7", "61.4", "0.3"])] : [new("r", ["Room", "48.7", "61.4"])] };
                for (var width = 200; width <= 600; width++)
                {
                    var card = source with { Width = width, SortColumn = width % source.Headers.Length };
                    using var actual = RasterDrawing.Render(card, Theme.Paper, scale);
                    using var expected = new Bitmap(actual.Width, actual.Height, PixelFormat.Format32bppPArgb);
                    using (var graphics = Graphics.FromImage(expected))
                    {
                        graphics.ScaleTransform((float)scale, (float)scale);
                        using var canvas = new GdiCanvas(graphics);
                        // Same paint commands and clips, with GDI's implicit ellipsis disabled.
                        // Pixel equality proves the real renderer drew every already-fitted string.
                        CardPainter.Paint(new NoImplicitEllipsis(canvas, graphics), card, Theme.Paper);
                    }
                    Assert.True(Pixels(actual).AsSpan().SequenceEqual(Pixels(expected)), $"Unexpected GDI trimming: {source.Headers.Length} columns, width {width}, scale {scale}");
                }
            }
        }
        finally { TextMetrics.Measurer = previous; }
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { bitmap.UnlockBits(data); }
    }

    private sealed class NoImplicitEllipsis(GdiCanvas canvas, Graphics graphics) : ICanvas2D
    {
        public Size2 MeasureText(string text, double size, string font) => canvas.MeasureText(text, size, font);
        public void Text(string text, Point2 p, double size, string color, string font)
        {
            if (p.Y < CardMeasure.BodyTop) { canvas.Text(text, p, size, color, font); return; }
            using var typeface = new Font(font, (float)size, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(GdiCanvas.ParseColor(color));
            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces,
                Trimming = StringTrimming.None
            };
            var bounds = graphics.ClipBounds;
            graphics.DrawString(text, typeface, brush, new RectangleF((float)p.X, (float)p.Y,
                bounds.Right - (float)p.X, Math.Max((float)(size * 1.5), bounds.Bottom - (float)p.Y)), format);
        }
        public void Rect(Rect2 rect, string fill) => canvas.Rect(rect, fill);
        public void Gradient(Rect2 rect, IReadOnlyList<string> colors) => canvas.Gradient(rect, colors);
        public void RoundedRect(Rect2 rect, double radius, string fill) => canvas.RoundedRect(rect, radius, fill);
        public void Line(Point2 from, Point2 to, string color, double width = 1) => canvas.Line(from, to, color, width);
        public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1) => canvas.Polyline(points, color, width);
        public void FillPolygon(IReadOnlyList<Point2> points, string fill) => canvas.FillPolygon(points, fill);
        public IDisposable Clip(Rect2 rect) => canvas.Clip(rect);
    }
}
