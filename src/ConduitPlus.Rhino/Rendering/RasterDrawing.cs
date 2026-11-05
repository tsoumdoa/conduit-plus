using System.Drawing;
using System.Drawing.Imaging;
using ConduitPlus.Core;

namespace ConduitPlus.Rendering;

/// <summary>Rhino-free rasterization. All GDI+ objects are local to the calling thread.</summary>
public static class RasterDrawing
{
    public static Bitmap Render(CardSpec spec, Theme theme, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        var size = CardMeasure.Measure(spec, theme);
        var bitmap = new Bitmap(checked((int)Math.Ceiling(size.Width * scale)), checked((int)Math.Ceiling(size.Height * scale)), PixelFormat.Format32bppPArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            graphics.ScaleTransform((float)scale, (float)scale);
            using var canvas = new GdiCanvas(graphics);
            CardPainter.Paint(canvas, spec, theme);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}
