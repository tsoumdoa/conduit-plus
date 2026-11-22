using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ConduitPlus.Core;

namespace ConduitPlus.Rendering;

public sealed class GdiCanvas : ICanvas2D, IDisposable
{
    public static void RegisterTextMetrics() => TextMetrics.Measurer = MeasureCssText;
    // Wrapping measures many substrings; keep one measuring canvas (and its font cache) per thread.
    [ThreadStatic] private static GdiCanvas? _measure;
    private static Size2 MeasureCssText(string text, double size, string font)
        => (_measure ??= new GdiCanvas(Graphics.FromImage(new Bitmap(1, 1)))).MeasureText(text, size, font);
    private readonly Graphics graphics;
    private bool _transparentBackground;
    // Raster stacks have disjoint card bodies. The caller must guarantee that the next
    // paint touches only cleared pixels; never reset this for overlapping decorations.
    internal void BeginTransparentRegion() => _transparentBackground = true;
    private readonly Dictionary<(string Font, double Size), Font> _fonts = [];
    private readonly Dictionary<string, SolidBrush> _brushes = [];
    private readonly StringFormat _format = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces,
        Trimming = StringTrimming.EllipsisCharacter
    };
    public GdiCanvas(Graphics graphics, bool transparentBackground = false)
    {
        this.graphics = graphics;
        _transparentBackground = transparentBackground;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.CompositingMode = CompositingMode.SourceOver;
        // ClearType assumes an opaque background: never use it on our alpha textures.
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }
    private Font FontOf(string font, double size)
    {
        if (!_fonts.TryGetValue((font, size), out var result)) _fonts.Add((font, size), result = new Font(Family(font), (float)size, FontStyle.Regular, GraphicsUnit.Pixel));
        return result;
    }
    private static readonly Dictionary<string, string> Families = new(StringComparer.Ordinal);
    /// <summary>
    /// The installed family used to measure and paint <paramref name="font"/>. Windows: the name unchanged.
    /// macOS (no Segoe UI): Helvetica Neue, so measurement and paint always agree on one real face.
    /// </summary>
    public static string Family(string font)
    {
        if (!OperatingSystem.IsMacOS()) return font;
        lock (Families)
        {
            if (!Families.TryGetValue(font, out var family)) Families[font] = family = FontFallback.Resolve(font, true, Installed);
            return family;
        }
    }
    private static HashSet<string>? _installed;
    private static bool Installed(string family)
    {
        if (_installed is null)
        {
            try
            {
                using var fonts = new InstalledFontCollection();
                _installed = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch { _installed = new(StringComparer.OrdinalIgnoreCase); }
            // An empty or failing enumeration must not reject every family: Helvetica Neue ships with macOS.
            if (_installed.Count == 0) _installed.Add(FontFallback.MacFamilies[0]);
        }
        return _installed.Contains(family);
    }
    private SolidBrush BrushOf(string color)
    {
        if (!_brushes.TryGetValue(color, out var brush)) _brushes.Add(color, brush = new SolidBrush(C(color)));
        return brush;
    }
    public static Color ParseColor(string value)
    {
        var rgba = ColorRgba.Parse(value);
        return Color.FromArgb(rgba.A, rgba.R, rgba.G, rgba.B);
    }
    private static Color C(string value) => ParseColor(value);
    private void Composite(bool opaque, bool antialiased = false)
    {
        // Gamma correction is costly and changes translucent layering visibly. Preserve it
        // there (including partial edge coverage), but opaque rectangle interiors need none.
        // On a cleared texture, ordinary source-over
        // also gives the correct premultiplied source colour (HighQuality loses a byte).
        graphics.CompositingQuality = opaque && !antialiased || _transparentBackground
            ? CompositingQuality.HighSpeed : CompositingQuality.HighQuality;
        _transparentBackground = false;
    }
    public Size2 MeasureText(string text, double size, string font)
    {
        var measured = graphics.MeasureString(text, FontOf(font, size), int.MaxValue, _format); return new(measured.Width, measured.Height);
    }
    public void Rect(Rect2 r, string fill)
    {
        var brush = BrushOf(fill);
        Composite(brush.Color.A == 255, antialiased: !PixelAligned(r));
        graphics.FillRectangle(brush, (float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);
    }
    private bool PixelAligned(Rect2 r)
    {
        // Fractional rectangle edges have translucent coverage too. In particular, bar
        // ends and line-chart bands must retain their original gamma-correct edge pixels.
        if (graphics.PageUnit is not (GraphicsUnit.Pixel or GraphicsUnit.Display) || graphics.PageScale != 1) return false;
        using var transform = graphics.Transform;
        var m = transform.Elements;
        static bool Integer(double value) => Math.Abs(value - Math.Round(value)) < .0001;
        return m[1] == 0 && m[2] == 0 && Integer(r.X * m[0] + m[4]) && Integer(r.Y * m[3] + m[5]) &&
            Integer((r.X + r.Width) * m[0] + m[4]) && Integer((r.Y + r.Height) * m[3] + m[5]);
    }
    public void Gradient(Rect2 r, IReadOnlyList<string> colors) => Gradient(r, colors, []);
    public void Gradient(Rect2 r, IReadOnlyList<string> colors, IReadOnlyList<double> positions)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        Composite(colors.All(c => C(c).A == 255));
        var state = graphics.Save();
        try
        {
            // HighQuality's half-pixel offset also affects TransformPoints. Remove
            // it before both locating columns and mapping their centres back.
            graphics.PixelOffsetMode = PixelOffsetMode.None;
            var gradient = new ColourGradient(colors, positions);
            PointF[] corners = [new((float)r.X, (float)r.Y), new((float)(r.X + r.Width), (float)r.Y),
                new((float)(r.X + r.Width), (float)(r.Y + r.Height)), new((float)r.X, (float)(r.Y + r.Height))];
            graphics.TransformPoints(CoordinateSpace.Device, CoordinateSpace.World, corners);
            var left = (int)Math.Floor(corners.Min(p => p.X)); var right = (int)Math.Ceiling(corners.Max(p => p.X));
            var top = (int)Math.Floor(corners.Min(p => p.Y)); var bottom = (int)Math.Ceiling(corners.Max(p => p.Y));
            PointF[] samples = [new(left + .5f, top + .5f), new(left + 1.5f, top + .5f), new(left + .5f, top + 1.5f)];
            graphics.TransformPoints(CoordinateSpace.World, CoordinateSpace.Device, samples);
            var dx = samples[1].X - samples[0].X; var dy = samples[2].X - samples[0].X;
            // Preserve the caller's clip (including any shaped clip) and the bar's
            // transformed outline. Paint adjoining integer device rectangles with
            // antialiasing disabled so neither knots nor columns can leave seams.
            graphics.SetClip(new RectangleF((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height), CombineMode.Intersect);
            graphics.ResetTransform();
            graphics.PageUnit = GraphicsUnit.Pixel;
            graphics.PageScale = 1;
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.PixelOffsetMode = PixelOffsetMode.None;
            using var brush = new SolidBrush(Color.Black);
            for (var x = left; x < right; x++)
            {
                // Normal card transforms are scale + translation. For a rotated
                // or sheared canvas, sample each device row as well.
                var step = dy == 0 ? Math.Max(1, bottom - top) : 1;
                for (var y = top; y < bottom; y += step)
                {
                    var worldX = samples[0].X + (double)(x - left) * dx + (double)(y - top) * dy;
                    var c = gradient.ColourAt((worldX - r.X) / r.Width);
                    brush.Color = Color.FromArgb(c.A, c.R, c.G, c.B);
                    graphics.FillRectangle(brush, x, y, 1, step);
                }
            }
        }
        finally { graphics.Restore(state); }
    }
    public void RoundedRect(Rect2 r, double radius, string fill)
    {
        if (radius <= 0) { Rect(r, fill); return; }
        var brush = BrushOf(fill); Composite(brush.Color.A == 255, antialiased: true);
        using var path = new GraphicsPath(); var d = (float)(Math.Min(radius, Math.Min(r.Width, r.Height) / 2) * 2); var x = (float)r.X; var y = (float)r.Y; var w = (float)r.Width; var h = (float)r.Height;
        path.AddArc(x, y, d, d, 180, 90); path.AddArc(x + w - d, y, d, d, 270, 90); path.AddArc(x + w - d, y + h - d, d, d, 0, 90); path.AddArc(x, y + h - d, d, d, 90, 90); path.CloseFigure();
        graphics.FillPath(brush, path);
    }
    public void Line(Point2 a, Point2 b, string color, double width = 1)
    { var c = C(color); Composite(c.A == 255, antialiased: true); using var pen = new Pen(c, (float)width); graphics.DrawLine(pen, (float)a.X, (float)a.Y, (float)b.X, (float)b.Y); }
    public void Text(string text, Point2 p, double size, string color, string font)
    {
        var bounds = graphics.ClipBounds;
        var width = bounds.Right - (float)p.X;
        var height = bounds.Bottom - (float)p.Y;
        if (width <= 0 || height <= 0) return;
        // Glyph coverage is translucent even when the ink is opaque.
        Composite(false);
        graphics.DrawString(text, FontOf(font, size), BrushOf(color), new RectangleF((float)p.X, (float)p.Y, width, Math.Max((float)(size * 1.5), height)), _format);
    }
    public void Polyline(IReadOnlyList<Point2> points, string color, double width = 1)
    {
        if (points.Count < 2) return;
        var c = C(color); Composite(c.A == 255, antialiased: true);
        using var pen = new Pen(c, (float)width) { LineJoin = LineJoin.Round };
        graphics.DrawLines(pen, points.Select(p => new PointF((float)p.X, (float)p.Y)).ToArray());
    }
    public void FillPolygon(IReadOnlyList<Point2> points, string fill)
    {
        if (points.Count < 3) return;
        var brush = BrushOf(fill); Composite(brush.Color.A == 255, antialiased: true);
        using var path = new GraphicsPath();
        path.AddPolygon(points.Select(p => new PointF((float)p.X, (float)p.Y)).ToArray());
        graphics.FillPath(brush, path);
    }
    public IDisposable Clip(Rect2 r)
    {
        var old = graphics.Save(); graphics.SetClip(new RectangleF((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height), CombineMode.Intersect);
        return new Restore(() => graphics.Restore(old));
    }
    private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
    public void Dispose()
    {
        foreach (var font in _fonts.Values) font.Dispose(); _fonts.Clear();
        foreach (var brush in _brushes.Values) brush.Dispose(); _brushes.Clear();
        _format.Dispose();
    }
}
