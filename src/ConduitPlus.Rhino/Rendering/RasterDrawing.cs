using System.Drawing;
using System.Drawing.Imaging;
using ConduitPlus.Core;

namespace ConduitPlus.Rendering;

/// <summary>Rhino-free rasterization. All GDI+ objects are local to the calling thread.</summary>
public static class RasterDrawing
{
    static RasterDrawing() => GdiCanvas.RegisterTextMetrics();
    public static int Margin(Theme theme, double scale) => theme.Shadow ? checked((int)Math.Ceiling((theme.ShadowExtent + 1) * scale)) : 0;
    public static Bitmap Render(CardSpec spec, Theme theme, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        var size = CardMeasure.Measure(spec, theme);
        return Render(size, scale, canvas => CardPainter.Paint(canvas, spec, theme), Margin(theme, scale));
    }
    public static Bitmap RenderTooltip(IReadOnlyList<string> lines, Theme theme, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        using var measureBitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(measureBitmap);
        using var canvas = new GdiCanvas(graphics);
        return Render(TooltipPainter.Measure(canvas, lines, theme), scale, c => TooltipPainter.Paint(c, lines, theme), Margin(theme, scale));
    }
    private static Bitmap Render(Size2 size, double scale, Action<ICanvas2D> paint, int margin = 0)
    {
        var bitmap = new Bitmap(checked((int)Math.Ceiling(size.Width * scale) + 2 * margin), checked((int)Math.Ceiling(size.Height * scale) + 2 * margin), PixelFormat.Format32bppPArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            graphics.TranslateTransform(margin, margin);
            graphics.ScaleTransform((float)scale, (float)scale);
            using var canvas = new GdiCanvas(graphics);
            paint(canvas);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}
