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
    public static Bitmap RenderStack(IReadOnlyList<CardSpec> specs, Theme theme, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        if (specs.Count == 1) return Render(specs[0], theme, scale);
        var layout = new CanvasStackLayout(specs, theme);
        var margin = Margin(theme, scale);
        var bitmap = new Bitmap(checked((int)Math.Ceiling(layout.Size.Width * scale) + 2 * margin),
            checked((int)Math.Ceiling(layout.Size.Height * scale) + 2 * margin), PixelFormat.Format32bppPArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            graphics.TranslateTransform(margin, margin);
            graphics.ScaleTransform((float)scale, (float)scale);
            using var canvas = new GdiCanvas(graphics);
            for (var i = 0; i < specs.Count; i++)
            {
                var saved = graphics.Save();
                try
                {
                    graphics.TranslateTransform((float)layout.Cards[i].X, (float)layout.Cards[i].Y);
                    if (!theme.Shadow) canvas.BeginTransparentRegion();
                    CardPainter.Paint(canvas, specs[i], theme);
                }
                finally { graphics.Restore(saved); }
            }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
    public static Bitmap RenderTooltip(IReadOnlyList<string> lines, Theme theme, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        using var measureBitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(measureBitmap);
        using var canvas = new GdiCanvas(graphics);
        return Render(TooltipPainter.Measure(canvas, lines, theme), scale, c => TooltipPainter.Paint(c, lines, theme), Margin(theme, scale));
    }
    public static Bitmap RenderLabel(LabelRasterKey key)
    {
        var size = key.Form == LabelForm.Dot ? new Size2(5, 5) : new Size2(key.Width, 18);
        var bitmap = Render(size, key.Dpi, canvas => LabelPainter.Paint(canvas, key));
        if (key.Opacity == 1) return bitmap;
        using (bitmap)
        {
            var faded = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppPArgb);
            try
            {
                using var graphics = Graphics.FromImage(faded);
                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(new ColorMatrix { Matrix33 = (float)key.Opacity });
                graphics.DrawImage(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, attributes);
                return faded;
            }
            catch { faded.Dispose(); throw; }
        }
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
            using var canvas = new GdiCanvas(graphics, transparentBackground: true);
            paint(canvas);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}
