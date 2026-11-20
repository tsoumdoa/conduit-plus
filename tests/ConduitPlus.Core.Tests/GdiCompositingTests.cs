using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ConduitPlus.Core;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class GdiCompositingTests
{
    [Theory]
    [InlineData("#12151CCC")]
    [InlineData("#0A2654CC")]
    [InlineData("#789ABCEB")]
    public void ClearTextureContainsExpectedPremultipliedSurface(string color)
    {
        foreach (var shape in new[] { "rect", "rounded", "gradient" })
        {
            using var bitmap = new Bitmap(40, 30, PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                using var canvas = new GdiCanvas(graphics, transparentBackground: true);
                if (shape == "rect") canvas.Rect(new(0, 0, 40, 30), color);
                else if (shape == "rounded") canvas.RoundedRect(new(0, 0, 40, 30), 5, color);
                else canvas.Gradient(new(0, 0, 40, 30), [color, color]);
                Assert.Equal(CompositingQuality.HighSpeed, graphics.CompositingQuality);
                Assert.Equal(TextRenderingHint.AntiAliasGridFit, graphics.TextRenderingHint);
            }
            var data = bitmap.LockBits(new(0, 0, 40, 30), ImageLockMode.ReadOnly, bitmap.PixelFormat);
            var raw = new byte[4];
            try { Marshal.Copy(data.Scan0 + data.Stride * 15 + 20 * 4, raw, 0, 4); }
            finally { bitmap.UnlockBits(data); }
            var source = ColorRgba.Parse(color);
            Assert.Equal(source.A, raw[3]);
            Assert.InRange(Math.Abs(raw[2] - source.R * source.A / 255d), 0, 1);
            Assert.InRange(Math.Abs(raw[1] - source.G * source.A / 255d), 0, 1);
            Assert.InRange(Math.Abs(raw[0] - source.B * source.A / 255d), 0, 1);
        }
    }

    [Fact]
    public void LayeredTranslucencyAndAntialiasedMarksKeepGammaCorrection()
    {
        using var bitmap = new Bitmap(40, 30, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        using var canvas = new GdiCanvas(graphics, transparentBackground: true);
        canvas.Rect(new(0, 0, 40, 30), "#FFFFFF");
        canvas.Rect(new(0, 0, 40, 30), Theme.Smoke.Surface);
        Assert.Equal(CompositingQuality.HighQuality, graphics.CompositingQuality);
        Assert.InRange(bitmap.GetPixel(20, 15).R, 123, 127);
        canvas.Rect(new(0, 0, 20, 20), "#FFFFFF");
        Assert.Equal(CompositingQuality.HighSpeed, graphics.CompositingQuality);
        canvas.Rect(new(.2, .2, 20.3, 20), "#FFFFFF");
        Assert.Equal(CompositingQuality.HighQuality, graphics.CompositingQuality);
        canvas.RoundedRect(new(0, 0, 20, 20), 5, "#FFFFFF");
        Assert.Equal(CompositingQuality.HighQuality, graphics.CompositingQuality);
        canvas.Line(new(1, 1), new(20, 20), "#FFFFFF");
        Assert.Equal(CompositingQuality.HighQuality, graphics.CompositingQuality);
        canvas.FillPolygon([new(1, 1), new(15, 10), new(1, 20)], "#FFFFFF");
        Assert.Equal(CompositingQuality.HighQuality, graphics.CompositingQuality);
        canvas.Text("test", new(0, 0), 12, "#FFFFFF", "Segoe UI");
        Assert.Equal(CompositingQuality.HighQuality, graphics.CompositingQuality);
        Assert.Equal(TextRenderingHint.AntiAliasGridFit, graphics.TextRenderingHint);
    }
}
